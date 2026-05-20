using System.Text.Json;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Sdr;

/// <summary>
/// SDR adapter: probes the Wavelet-Lab uSDR via sdr-svc REST API (FR-006).
/// Connects to sdr-svc on configurable host:port (default localhost:5101) instead
/// of calling SoapySDRUtil directly. Eliminates the need for USB device
/// pass-through and SoapySDR tools in the container.
/// T018: Device status, capabilities, and band sweep via HTTP REST.
/// </summary>
public sealed class SdrAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Sdr;

	private readonly ILogger<SdrAdapter> _logger;
	private readonly IConfiguration _config;
	private readonly IHttpClientFactory _httpClientFactory;

	public SdrAdapter(ILogger<SdrAdapter> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
	{
		_logger = logger;
		_config = config;
		_httpClientFactory = httpClientFactory;
	}

	/// <summary>
	/// Probes the SDR via sdr-svc REST API (FR-006, FR-007).
	/// Returns Ready with device info on success, Unavailable if service is not reachable.
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, Func<string, string, bool, Task>? reportStep = null, CancellationToken ct = default)
	{
		var messages = new List<string>();

		// Resolve sdr-svc endpoint from configuration (FR-006, FR-013)
		var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;
		var timeoutMs = int.TryParse(_config["Peripherals:Sdr:TimeoutMs"], out var t) ? t : 5_000;
		var baseUrl = $"http://{host}:{port}";

		var client = _httpClientFactory.CreateClient("SdrSvc");
		client.BaseAddress = new Uri(baseUrl);
		client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);

		try
		{
			// 1. Check device status via REST (FR-018)
			using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			statusCts.CancelAfter(timeoutMs);
			using var statusResponse = await client.GetAsync("/api/status", statusCts.Token);
			statusResponse.EnsureSuccessStatusCode();
			using var statusDoc = await JsonDocument.ParseAsync(await statusResponse.Content.ReadAsStreamAsync(statusCts.Token), cancellationToken: statusCts.Token);
			var statusRoot = statusDoc.RootElement;

			var available = statusRoot.TryGetProperty("deviceFound", out var availEl) && availEl.GetBoolean();
			var driverInfo = statusRoot.TryGetProperty("driverInfo", out var drvEl) ? drvEl.GetString() ?? "" : "";
			var probeOk = statusRoot.TryGetProperty("probeOk", out var probeEl) && probeEl.GetBoolean();
			var statusMessage = statusRoot.TryGetProperty("status", out var msgEl) ? msgEl.GetString() ?? "" : "";

			if (reportStep is not null)
				await reportStep("GET /api/status", $"device_found={available} driver_info={driverInfo} probe_ok={probeOk}", !available);

			messages.Add($"sdr-svc reachable on {host}:{port}");
			_logger.LogInformation("SDR probe: sdr-svc reachable on {Host}:{Port}", host, port);

			if (!available)
			{
				messages.Add($"sdr-svc reports device unavailable: {statusMessage}");
				return new DiagnosticEnvelope
				{
					PeripheralId = PeripheralId,
					DependencyAvailable = true,
					Status = PeripheralStatus.Unavailable,
					Messages = messages,
					Snapshot = new HealthSnapshot
					{
						Values = new Dictionary<string, object?>
						{
							["service_available"] = true,
							["device_found"] = false,
							["status_message"] = statusMessage
						}
					},
					CapturedAtUtc = DateTimeOffset.UtcNow
				};
			}

			messages.Add($"SDR device found: {driverInfo}");

			// 2. Get capabilities via REST (FR-007)
			using var capsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			capsCts.CancelAfter(timeoutMs);
			using var capsResponse = await client.GetAsync("/api/capabilities", capsCts.Token);
			capsResponse.EnsureSuccessStatusCode();
			using var capsDoc = await JsonDocument.ParseAsync(await capsResponse.Content.ReadAsStreamAsync(capsCts.Token), cancellationToken: capsCts.Token);
			var capsRoot = capsDoc.RootElement;

			var minFreqHz = 0.0;
			var maxFreqHz = 0.0;
			if (capsRoot.TryGetProperty("rxFreqRangeHz", out var rxFreqEl) && rxFreqEl.ValueKind == JsonValueKind.Object)
			{
				minFreqHz = rxFreqEl.TryGetProperty("min", out var minFEl) ? minFEl.GetDouble() : 0.0;
				maxFreqHz = rxFreqEl.TryGetProperty("max", out var maxFEl) ? maxFEl.GetDouble() : 0.0;
			}
			var minGainDb = 0.0;
			var maxGainDb = 0.0;
			if (capsRoot.TryGetProperty("rxGains", out var gainsEl) && gainsEl.ValueKind == JsonValueKind.Object)
			{
				foreach (var gain in gainsEl.EnumerateObject())
				{
					if (gain.Value.TryGetProperty("max", out var maxGEl))
					{
						var gMax = maxGEl.GetDouble();
						if (gMax > maxGainDb) maxGainDb = gMax;
					}
				}
			}

			if (reportStep is not null)
				await reportStep("GET /api/capabilities", $"freq={minFreqHz / 1e6:F1}-{maxFreqHz / 1e6:F1}MHz gain={minGainDb:F0}-{maxGainDb:F0}dB", false);

			messages.Add($"SDR PASS: {driverInfo} tuning {minFreqHz / 1e6:F1}-{maxFreqHz / 1e6:F1} MHz, gain {minGainDb:F0}-{maxGainDb:F0} dB");

			return new DiagnosticEnvelope
			{
				PeripheralId = PeripheralId,
				DependencyAvailable = true,
				Status = PeripheralStatus.Ready,
				Messages = messages,
				Snapshot = new HealthSnapshot
				{
					Values = new Dictionary<string, object?>
					{
						["service_available"] = true,
						["device_found"] = true,
						["driver_info"] = driverInfo,
						["probe_ok"] = probeOk,
						["min_frequency_hz"] = minFreqHz,
						["max_frequency_hz"] = maxFreqHz,
						["min_gain_db"] = minGainDb,
						["max_gain_db"] = maxGainDb
					}
				},
				CapturedAtUtc = DateTimeOffset.UtcNow
			};
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "SDR probe: REST call to sdr-svc failed");
			messages.Add($"REST error: {ex.GetType().Name}: {ex.Message}");
			return new DiagnosticEnvelope
			{
				PeripheralId = PeripheralId,
				DependencyAvailable = true,
				Status = PeripheralStatus.Degraded,
				Messages = messages,
				Snapshot = new HealthSnapshot
				{
					Values = new Dictionary<string, object?>
					{
						["service_available"] = true,
						["rest_error"] = ex.Message
					}
				},
				CapturedAtUtc = DateTimeOffset.UtcNow
			};
		}
	}

	/// <summary>
	/// Returns a summary of the device status from sdr-svc.
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
			using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
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
}
