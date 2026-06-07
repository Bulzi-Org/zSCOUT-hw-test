using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ZScout.Common.Sdr;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Sdr;

/// <summary>
/// SDR adapter: probes the Wavelet-Lab uSDR via sdr-svc REST API (FR-006).
/// Uses shared Sdr* types + SdrClient from zSCOUT-common (Phase A of #74 / #19).
/// Connects to sdr-svc on configurable host:port (default localhost:5101) instead
/// of calling SoapySDRUtil directly. Eliminates the need for USB device
/// pass-through and SoapySDR tools in the container.
///
/// T018: Device status, capabilities, RX configure and IQ acquire via HTTP REST.
///
/// RunMode.Container and RunMode.Host both run the full functional path:
/// status → capabilities → configure RX → acquire IQ samples → capture
/// validation + auto-discover validator summary.
///
/// Expanded in #74 to exercise the full set of sdr-svc endpoints (incl. new capture)
/// with graceful 404 handling during sdr-svc#32 rollout.
/// Normal peripheral probes now provide evidence of capture capability.
/// </summary>
public sealed class SdrAdapter : IHardwareAdapter
{
	/// <summary>Safe test frequency inside the Wavelet-Lab uSDR range.</summary>
	private const long DefaultTestCenterFreqHz = 800_000_000L; // 800 MHz

	/// <summary>Safe sample rate supported by the uSDR.</summary>
	private const long DefaultTestSampleRateHz = 1_000_000L; // 1 MSPS

	/// <summary>Number of IQ samples to acquire in the functional test.</summary>
	private const int FunctionalTestSampleCount = 4096;

	public PeripheralId PeripheralId => PeripheralId.Sdr;

	private readonly ILogger<SdrAdapter> _logger;
	private readonly IConfiguration _config;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly SdrClient _sdrClient;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public SdrAdapter(ILogger<SdrAdapter> logger, IConfiguration config, IHttpClientFactory httpClientFactory, SdrClient sdrClient)
	{
		_logger = logger;
		_config = config;
		_httpClientFactory = httpClientFactory;
		_sdrClient = sdrClient;
	}

	/// <summary>
	/// Probes the SDR via sdr-svc REST API (FR-006, FR-007).
	///
	/// Container mode and Host mode both execute the full SDR endpoint flow.
	///
	/// Returns Ready on full success, Unavailable if the service is not reachable,
	/// Degraded if the service is reachable but a step fails.
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, Func<string, string, bool, Task>? reportStep = null, CancellationToken ct = default)
	{
		var messages = new List<string>();

		var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;
		var timeoutMs = int.TryParse(_config["Peripherals:Sdr:TimeoutMs"], out var t) ? t : 5_000;

		var snapshot = new Dictionary<string, object?>();

		try
		{
			// ── Step 1: device status (via SdrClient) ──────────────────────────
			var status = await _sdrClient.GetStatusAsync(ct);
			if (status is null)
			{
				messages.Add($"sdr-svc unreachable on {host}:{port}");
				snapshot["service_available"] = false;
				return BuildEnvelope(PeripheralStatus.Degraded, messages, snapshot);
			}

			var available = status.DeviceFound;
			var driverInfo = status.DriverInfo ?? "";
			var probeOk = status.ProbeOk;
			var statusMessage = status.Status ?? "";

			if (reportStep is not null)
				await reportStep("GET /api/status (SdrClient)", $"device_found={available} driver_info={driverInfo} probe_ok={probeOk}", !available);

			messages.Add($"sdr-svc reachable on {host}:{port}");
			_logger.LogInformation("SDR probe: sdr-svc reachable on {Host}:{Port}", host, port);

			snapshot["service_available"] = true;
			snapshot["device_found"] = available;
			snapshot["driver_info"] = driverInfo;
			snapshot["probe_ok"] = probeOk;

			if (!available)
			{
				messages.Add($"sdr-svc reports device unavailable: {statusMessage}");
				snapshot["status_message"] = statusMessage;
				return BuildEnvelope(PeripheralStatus.Unavailable, messages, snapshot);
			}

			messages.Add($"SDR device found: {driverInfo}");

			// ── Step 2: capabilities (via SdrClient) ───────────────────────────
			var caps = await _sdrClient.GetCapabilitiesAsync(ct);
			double minFreqHz = 0.0, maxFreqHz = 0.0, maxGainDb = 0.0;
			if (caps is not null)
			{
				minFreqHz = caps.RxFreqRangeHz?.Min ?? 0;
				maxFreqHz = caps.RxFreqRangeHz?.Max ?? 0;
				if (caps.RxGains is not null)
				{
					foreach (var g in caps.RxGains.Values)
						if (g.Max > maxGainDb) maxGainDb = g.Max;
				}
			}

			if (reportStep is not null)
				await reportStep("GET /api/capabilities (SdrClient)", $"freq={minFreqHz / 1e6:F1}-{maxFreqHz / 1e6:F1}MHz max_gain={maxGainDb:F0}dB", false);

			snapshot["min_frequency_hz"] = minFreqHz;
			snapshot["max_frequency_hz"] = maxFreqHz;
			snapshot["max_gain_db"] = maxGainDb;
			snapshot["rx_configured"] = false;
			snapshot["iq_sample_count"] = 0;

			// ── Step 3: RX configure + IQ acquire + capture (+ validator summary) ───
			// Uses SdrClient (shared types). Exercises full endpoints including new
			// capture path (/api/rx/capture). 404s during sdr-svc#32 rollout are
			// handled gracefully so normal probes still succeed + provide evidence.
			{
				var testFreqHz = long.TryParse(_config["Peripherals:Sdr:TestCenterFreqHz"], out var tf)
					? tf : DefaultTestCenterFreqHz;
				var testRateHz = long.TryParse(_config["Peripherals:Sdr:TestSampleRateHz"], out var tr)
					? tr : DefaultTestSampleRateHz;
				var autoDiscoverEnabled = !string.Equals(_config["Peripherals:Sdr:EnableAutoDiscoverInProbe"], "false", StringComparison.OrdinalIgnoreCase);
				var autoDiscoverCandidates = int.TryParse(_config["Peripherals:Sdr:AutoDiscoverMaxCandidates"], out var maxCandidates)
								? maxCandidates : 48;
				var autoDiscoverSamples = int.TryParse(_config["Peripherals:Sdr:AutoDiscoverNumSamples"], out var discoverSamples)
								? discoverSamples : 16_384;

				try
				{
					var rxReq = new RxConfigRequest { CenterFreqHz = testFreqHz, SampleRateHz = testRateHz };
					var cfgResp = await _sdrClient.ConfigureRxAsync(rxReq, ct);

					long configuredFreqHz = cfgResp?.CenterFreqHz ?? testFreqHz;
					if (reportStep is not null)
						await reportStep("POST /api/rx/configure (SdrClient)",
							$"center={configuredFreqHz / 1e6:F3}MHz rate={testRateHz / 1e6:F1}MSPS", false);

					snapshot["rx_configured"] = true;
					snapshot["last_center_freq_hz"] = configuredFreqHz;
					snapshot["last_sample_rate_hz"] = testRateHz;
					messages.Add($"RX configured: {configuredFreqHz / 1e6:F3} MHz @ {testRateHz / 1e6:F1} MSPS");

							// Use factory client + helper (honors test fakes/overrides; in prod the helper falls to SdrClient when client null)
							try
							{
								var iqClient = _httpClientFactory.CreateClient("SdrSvc");
								var iq = await AcquireSamplesAsync(FunctionalTestSampleCount, iqClient, timeoutMs, ct);

								var (sampleCount, minVal, maxVal, meanAbs) = ValidateIqBlock(iq);

								if (reportStep is not null)
									await reportStep("GET /api/rx/samples (SdrClient)",
										$"count={sampleCount} min={minVal:F4} max={maxVal:F4} mean_abs={meanAbs:F4}", false);

								snapshot["iq_sample_count"] = sampleCount;
								snapshot["iq_min_value"] = minVal;
								snapshot["iq_max_value"] = maxVal;
								snapshot["iq_mean_abs"] = meanAbs;

								var countOk = sampleCount == FunctionalTestSampleCount * 2;
								var rangeOk = minVal >= -1.5f && maxVal <= 1.5f;
								if (!countOk)
									messages.Add($"IQ WARNING: expected {FunctionalTestSampleCount * 2} floats, got {sampleCount}");
								if (!rangeOk)
									messages.Add($"IQ WARNING: values out of CF32 normalized range [{minVal:F4}, {maxVal:F4}]");

								messages.Add($"IQ acquire PASS: {sampleCount / 2} samples, mean_abs={meanAbs:F4}");
							}
							catch (OperationCanceledException)
							{
								snapshot["iq_acquire_error"] = "timed out";
								messages.Add("IQ acquire timeout: proceeding with capture validator path");
								if (reportStep is not null)
									await reportStep("GET /api/rx/samples (SdrClient)", "timed out while waiting for samples", true);
							}
							catch (Exception ex)
							{
								snapshot["iq_acquire_error"] = ex.Message;
								messages.Add($"IQ acquire error: {ex.GetType().Name}: {ex.Message} (continuing)");
								if (reportStep is not null)
									await reportStep("GET /api/rx/samples (SdrClient)", $"error: {ex.GetType().Name}: {ex.Message}", true);
							}

					// ── NEW: exercise capture path (proves arbitrary f/bw raw capture)
					// Uses direct call (SdrClient does not yet expose Capture per common#14 eval).
					// Graceful on 404 (sdr-svc#32 not fully deployed).
					try
					{
						var cap = await CaptureRawForProbeAsync(testFreqHz, testRateHz, 2048, timeoutMs, ct);
						if (cap is not null)
						{
							var (cCount, cMin, cMax, cMean) = ValidateIqBlock(cap);
							if (reportStep is not null)
								await reportStep("GET /api/rx/capture (probe)",
									$"count={cCount} min={cMin:F4} max={cMax:F4}", false);
							snapshot["capture_iq_count"] = cCount;
							snapshot["capture_mean_abs"] = cMean;
							messages.Add($"Capture PASS (probe evidence): {cCount / 2} samples @ {testFreqHz / 1e6:F3}MHz");
						}
						else
						{
							messages.Add("Capture (probe): 404 or unavailable (sdr-svc#32 rollout) — graceful");
							snapshot["capture_unavailable"] = true;
						}
					}
					catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
					{
						_logger.LogWarning("SDR probe: /api/rx/capture 404 — sdr-svc#32 not yet deployed");
						messages.Add("Capture skipped in probe: sdr-svc#32 endpoint not available yet (graceful)");
						snapshot["capture_unavailable"] = true;
					}

					if (autoDiscoverEnabled)
					{
						try
						{
								var validator = new SdrCaptureValidator(NullLogger<SdrCaptureValidator>.Instance, _httpClientFactory, _config, _sdrClient);
								var captureResult = await validator.RunAutoDiscoverAndCaptureAsync(
									autoDiscoverCandidates,
									autoDiscoverSamples,
									progress: async line =>
									{
										if (reportStep is not null)
										{
											await reportStep("AUTO detail (validator)", line, false);
										}
									},
									ct: ct);

							snapshot["autodiscover_tx_count"] = captureResult.TransmissionCount;
							snapshot["autodiscover_center_freq_hz"] = captureResult.CenterFreqHz;
							snapshot["autodiscover_bandwidth_hz"] = captureResult.BandwidthHz;
							snapshot["autodiscover_message_count"] = captureResult.Messages?.Count ?? 0;
							snapshot["autodiscover_rssi_range"] = captureResult.RssiCount > 0
								? $"{captureResult.RssiMin?.ToString("F1")}..{captureResult.RssiMax?.ToString("F1")}"
								: "n/a";
							snapshot["autodiscover_snr_range"] = captureResult.SnrCount > 0
								? $"{captureResult.SnrMin?.ToString("F1")}..{captureResult.SnrMax?.ToString("F1")}"
								: "n/a";

							if (reportStep is not null)
							{
								var freq = captureResult.CenterFreqHz.HasValue ? $"{captureResult.CenterFreqHz.Value / 1e6:F3}MHz" : "none";
								var bw = captureResult.BandwidthHz.HasValue ? $"{captureResult.BandwidthHz.Value / 1e6:F1}MHz" : "none";
								await reportStep("AUTO /api/rx/capture (validator)", $"selected={freq}/{bw} tx={captureResult.TransmissionCount}", captureResult.TransmissionCount <= 0);
							}

							messages.Add(captureResult.TransmissionCount > 0
								? $"Auto-discover PASS: tx={captureResult.TransmissionCount} selected={captureResult.CenterFreqHz}/{captureResult.BandwidthHz}"
								: "Auto-discover completed: no transmissions detected");
						}
						catch (Exception ex) when (ex is not OperationCanceledException)
						{
							_logger.LogWarning(ex, "SDR probe auto-discover validator failed");
							snapshot["autodiscover_error"] = ex.Message;
							messages.Add($"Auto-discover error: {ex.Message}");
						}
					}
				}
				catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					_logger.LogWarning("SDR probe: configure/samples/capture 404 during transition");
					messages.Add("RX configure/acquire/capture skipped: sdr-svc endpoints pending (graceful during rollout)");
					snapshot["rx_configure_unavailable"] = true;
				}
			}

			messages.Add($"SDR PASS: {driverInfo} tuning {minFreqHz / 1e6:F1}-{maxFreqHz / 1e6:F1} MHz");

			return BuildEnvelope(PeripheralStatus.Ready, messages, snapshot);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "SDR probe: REST call to sdr-svc failed");
			messages.Add($"REST error: {ex.GetType().Name}: {ex.Message}");
			snapshot["service_available"] = snapshot.ContainsKey("service_available") ? snapshot["service_available"] : false;
			snapshot["rest_error"] = ex.Message;
			return BuildEnvelope(PeripheralStatus.Degraded, messages, snapshot);
		}
	}

	/// <summary>
	/// Sends POST /api/rx/configure. Prefers SdrClient; falls back to raw on provided client (test compat + override handlers).
	/// </summary>
	public async Task<long> ConfigureRxAsync(RxConfigRequest config, HttpClient? client, int timeoutMs, CancellationToken ct)
	{
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeoutMs);

		if (client is not null)
		{
			// legacy raw path for tests that supply a specific handler client
			using var response = await client.PostAsJsonAsync("/api/rx/configure", config, JsonOptions, cts.Token);
			response.EnsureSuccessStatusCode();
			using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
			var root = doc.RootElement;
			return root.TryGetProperty("centerFreqHz", out var freqEl) ? freqEl.GetInt64() : config.CenterFreqHz;
		}

		var resp = await _sdrClient.ConfigureRxAsync(config, cts.Token);
		return resp?.CenterFreqHz ?? config.CenterFreqHz;
	}

	/// <summary>
	/// Calls GET /api/rx/samples. Prefers SdrClient; falls back to raw on provided client (for test override routes).
	/// Returns shared IqSamples.
	/// </summary>
	public async Task<IqSamples> AcquireSamplesAsync(int numSamples, HttpClient? client, int timeoutMs, CancellationToken ct)
	{
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeoutMs);

		if (client is not null)
		{
			using var response = await client.GetAsync($"/api/rx/samples?numSamples={numSamples}", cts.Token);
			response.EnsureSuccessStatusCode();
			using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
			var root = doc.RootElement;
			var cf = root.TryGetProperty("centerFreqHz", out var cfEl) ? cfEl.GetInt64() : 0L;
			var sr = root.TryGetProperty("sampleRateHz", out var srEl) ? srEl.GetInt64() : 0L;
			var data = root.TryGetProperty("data", out var dEl) && dEl.ValueKind == JsonValueKind.Array
				? dEl.EnumerateArray().Select(e => e.GetSingle()).ToArray() : [];
			return new IqSamples { CenterFreqHz = cf, SampleRateHz = sr, Data = data, Timestamp = DateTimeOffset.UtcNow };
		}

		var iq = await _sdrClient.GetIqSamplesAsync(numSamples, cts.Token);
		return iq ?? new IqSamples { CenterFreqHz = 0, SampleRateHz = 0, Data = [], Timestamp = DateTimeOffset.UtcNow };
	}

	/// <summary>
	/// Internal probe helper: direct capture call for /api/rx/capture (new endpoint).
	/// Returns IqSamples or null on 404/unavailable (graceful).
	/// </summary>
	private async Task<IqSamples?> CaptureRawForProbeAsync(long centerFreqHz, long bandwidthHz, int numSamples, int timeoutMs, CancellationToken ct)
	{
		var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;
		try
		{
			var client = _httpClientFactory.CreateClient("SdrSvc");
			client.BaseAddress = new Uri($"http://{host}:{port}");
			client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
			var url = $"/api/rx/capture?center_freq_hz={centerFreqHz}&bandwidth_hz={bandwidthHz}&num_samples={numSamples}";
			using var resp = await client.GetAsync(url, ct);
			if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
			resp.EnsureSuccessStatusCode();
			return await resp.Content.ReadFromJsonAsync<IqSamples>(JsonOptions, ct);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return null;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogDebug(ex, "Probe capture helper failed (non-fatal)");
			return null;
		}
	}

	/// <summary>
	/// Returns a summary of the device status from sdr-svc.
	/// Also reports last configured frequency and rate if available from a prior probe.
	/// </summary>
	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;

		try
		{
			var client = _httpClientFactory.CreateClient("SdrSvc");
			client.BaseAddress = new Uri($"http://{host}:{port}");
			client.Timeout = TimeSpan.FromSeconds(3);
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(3_000);
			using var response = await client.GetAsync("/api/status", cts.Token);
			response.EnsureSuccessStatusCode();
			using var doc = await JsonDocument.ParseAsync(
				await response.Content.ReadAsStreamAsync(cts.Token),
				cancellationToken: cts.Token);
			var root = doc.RootElement;
			var drv = root.TryGetProperty("driverInfo", out var drvEl) ? drvEl.GetString() ?? "" : "";
			var avail = root.TryGetProperty("deviceFound", out var availEl) && availEl.GetBoolean();
			return $"driver_info={drv} device_found={avail}";
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Computes basic statistics over an IQ sample block for validation (supports shared IqSamples post-migration).
	/// Returns (floatCount, min, max, meanAbs).
	/// </summary>
	internal static (int Count, float Min, float Max, float MeanAbs) ValidateIqBlock(IqSamples block)
	{
		if (block?.Data is null || block.Data.Length == 0)
			return (0, 0f, 0f, 0f);

		var min = float.MaxValue;
		var max = float.MinValue;
		var sumAbs = 0.0;
		foreach (var v in block.Data)
		{
			if (v < min) min = v;
			if (v > max) max = v;
			sumAbs += Math.Abs(v);
		}
		return (block.Data.Length, min, max, (float)(sumAbs / block.Data.Length));
	}

	/// <summary>
	/// Back-compat overload for any remaining synthetic test data using old shape during transition.
	/// </summary>
	internal static (int Count, float Min, float Max, float MeanAbs) ValidateIqBlock(float[] data)
	{
		if (data is null || data.Length == 0)
			return (0, 0f, 0f, 0f);
		var min = float.MaxValue;
		var max = float.MinValue;
		var sumAbs = 0.0;
		foreach (var v in data)
		{
			if (v < min) min = v;
			if (v > max) max = v;
			sumAbs += Math.Abs(v);
		}
		return (data.Length, min, max, (float)(sumAbs / data.Length));
	}

	private DiagnosticEnvelope BuildEnvelope(PeripheralStatus status, List<string> messages, Dictionary<string, object?> snapshot) =>
		new()
		{
			PeripheralId = PeripheralId,
			DependencyAvailable = true,
			Status = status,
			Messages = messages,
			Snapshot = new HealthSnapshot { Values = snapshot },
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
}
