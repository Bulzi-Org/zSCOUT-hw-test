using System.Net.Http.Json;
using System.Text.Json;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Gps;

/// <summary>
/// GPS adapter: probes the MicoAir MG-A01 via gps-svc REST API.
/// Step 1 — Availability: GET /api/fix returns the current fix snapshot.
/// Step 2 — Streaming: GET /api/stream/fixes (SSE) + parallel /api/stream/nmea
/// for NMEA sentence counting to detect data stream health.
/// </summary>
public sealed class GpsAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Gps;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly ILogger<GpsAdapter> _logger;
	private readonly IConfiguration _config;
	private readonly IHttpClientFactory _httpClientFactory;

	public GpsAdapter(ILogger<GpsAdapter> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
	{
		_logger = logger;
		_config = config;
		_httpClientFactory = httpClientFactory;
	}

	/// <summary>
	/// Probes GPS availability via GET /api/fix, then streams live fix data
	/// via GET /api/stream/fixes (SSE) with parallel NMEA sentence counting
	/// via /api/stream/nmea until cancelled.
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(
		RunMode mode,
		Func<string, string, bool, Task>? reportStep = null,
		CancellationToken ct = default)
	{
		var messages = new List<string>();

		var host = _config["Peripherals:Gps:Host"] ?? "localhost";
		var restPort = int.TryParse(_config["Peripherals:Gps:RestPort"], out var rp) ? rp : 5200;
		var timeoutMs = int.TryParse(_config["Peripherals:Gps:TimeoutMs"], out var t) ? t : 5_000;

		// Step 1: Check gps-svc availability via GET /api/fix
		var reachable = false;
		try
		{
			var client = _httpClientFactory.CreateClient("GpsSvc");
			client.BaseAddress = new Uri($"http://{host}:{restPort}");
			client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
			using var fixCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			fixCts.CancelAfter(timeoutMs);
			using var fixResponse = await client.GetAsync("/api/fix", fixCts.Token);
			reachable = fixResponse.IsSuccessStatusCode;
			if (reportStep is not null)
				await reportStep($"GET http://{host}:{restPort}/api/fix", reachable ? "ok" : $"HTTP {(int)fixResponse.StatusCode}", !reachable);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (reportStep is not null)
				await reportStep($"GET http://{host}:{restPort}/api/fix", $"unreachable: {ex.GetType().Name}", true);
		}

		if (!reachable)
		{
			_logger.LogWarning("GPS probe: gps-svc not reachable on {Host}:{Port}", host, restPort);
			return DiagnosticEnvelope.Unavailable(PeripheralId, $"gps-svc not reachable on {host}:{restPort}");
		}

		messages.Add($"gps-svc reachable on {host}:{restPort}; starting live fix stream");
		_logger.LogInformation("GPS probe: gps-svc reachable on {Host}:{Port}; starting SSE fix stream + NMEA monitor", host, restPort);

		// Step 2: Stream live fixes + parallel NMEA counting
		var accumulator = new GpsFixAccumulator();
		var nmeaCount = 0;

		// Start parallel NMEA sentence counter
		var nmeaTask = Task.Run(async () =>
		{
			try
			{
				var nmeaClient = _httpClientFactory.CreateClient("GpsSvc");
				nmeaClient.BaseAddress = new Uri($"http://{host}:{restPort}");
				nmeaClient.Timeout = Timeout.InfiniteTimeSpan;
				using var nmeaStream = await nmeaClient.GetStreamAsync("/api/stream/nmea", ct);
				using var nmeaReader = new StreamReader(nmeaStream);
				while (!ct.IsCancellationRequested)
				{
					var line = await nmeaReader.ReadLineAsync(ct);
					if (line is null) break;
					if (line.StartsWith("data:", StringComparison.Ordinal))
						Interlocked.Increment(ref nmeaCount);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "GPS probe: NMEA stream monitor ended");
			}
		}, ct);

		try
		{
			var streamClient = _httpClientFactory.CreateClient("GpsSvc");
			streamClient.BaseAddress = new Uri($"http://{host}:{restPort}");
			streamClient.Timeout = Timeout.InfiniteTimeSpan;

			using var stream = await streamClient.GetStreamAsync("/api/stream/fixes", ct);
			using var reader = new StreamReader(stream);

			while (!ct.IsCancellationRequested)
			{
				var line = await reader.ReadLineAsync(ct);
				if (line is null) break;

				if (!line.StartsWith("data:", StringComparison.Ordinal))
					continue;

				var json = line["data:".Length..].Trim();
				if (string.IsNullOrEmpty(json))
					continue;

				GpsFix? fix;
				try
				{
					fix = JsonSerializer.Deserialize<GpsFix>(json, JsonOptions);
				}
				catch (JsonException)
				{
					continue;
				}

				if (fix is null)
					continue;

				accumulator.Update(fix);

				if (reportStep is not null)
				{
					var currentNmea = Interlocked.CompareExchange(ref nmeaCount, 0, 0);
					var summary = FormatFixSummary(fix, accumulator, currentNmea);
					await reportStep("SSE /api/stream/fixes", summary, false);
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "GPS probe: SSE fix stream ended with exception");
			messages.Add($"Stream ended with error: {ex.GetType().Name}: {ex.Message}");
		}

		// Wait briefly for NMEA task to wind down
		try { await nmeaTask.WaitAsync(TimeSpan.FromSeconds(2)); }
		catch { /* timeout or cancellation — fine */ }

		var finalNmeaCount = Interlocked.CompareExchange(ref nmeaCount, 0, 0);

		// Step 3: Determine final status
		var status = accumulator.FixObtained ? PeripheralStatus.Ready : PeripheralStatus.Degraded;

		if (accumulator.FixObtained)
		{
			messages.Add($"GPS PASS: qualifying fix obtained ({accumulator.TotalFixUpdates} fix updates, {finalNmeaCount} NMEA sentences)");
		}
		else
		{
			messages.Add(finalNmeaCount == 0
				? $"GPS test stopped: no NMEA data received — check baud rate / wiring ({accumulator.TotalFixUpdates} fix updates)"
				: $"GPS test stopped: no qualifying fix obtained ({accumulator.TotalFixUpdates} fix updates, {finalNmeaCount} NMEA sentences)");
		}

		// Step 4: Build HealthSnapshot
		var snapshotValues = accumulator.BuildSnapshot(reachable);
		snapshotValues["nmea_sentence_count"] = finalNmeaCount;

		return new DiagnosticEnvelope
		{
			PeripheralId = PeripheralId,
			DependencyAvailable = reachable,
			Status = status,
			Messages = messages,
			Snapshot = new HealthSnapshot { Values = snapshotValues },
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
	}

	/// <summary>
	/// Returns a summary of the latest GPS fix from gps-svc REST API.
	/// </summary>
	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var host = _config["Peripherals:Gps:Host"] ?? "localhost";
		var restPort = int.TryParse(_config["Peripherals:Gps:RestPort"], out var rp) ? rp : 5200;

		try
		{
			var client = _httpClientFactory.CreateClient("GpsSvc");
			client.BaseAddress = new Uri($"http://{host}:{restPort}");
			client.Timeout = TimeSpan.FromSeconds(3);
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(3_000);
			var fix = await client.GetFromJsonAsync<GpsFix>("/api/fix", JsonOptions, cts.Token);
			if (fix is null) return null;
			var modeStr = fix.Mode switch { 3 => "3D", 2 => "2D", 1 => "no-fix", _ => "unknown" };
			var lat = fix.Latitude.HasValue ? $"{fix.Latitude:F6}" : "--";
			var lon = fix.Longitude.HasValue ? $"{fix.Longitude:F6}" : "--";
			return $"[{modeStr}] lat:{lat} lon:{lon} sats:{fix.SatellitesUsed}/{fix.SatellitesVisible}";
		}
		catch
		{
			return null;
		}
	}

	private static string FormatFixSummary(GpsFix fix, GpsFixAccumulator acc, int nmeaRxCount)
	{
		var modeStr = fix.Mode switch
		{
			3 => "3D",
			2 => "2D",
			1 => "no-fix",
			_ => "unknown"
		};
		var lat = fix.Latitude.HasValue ? $"{fix.Latitude:F6}°" : "--";
		var lon = fix.Longitude.HasValue ? $"{fix.Longitude:F6}°" : "--";
		var alt = fix.AltitudeM.HasValue ? $"{fix.AltitudeM:F1}m" : "--";
		var time = fix.UtcTime ?? "--";
		var sats = $"{fix.SatellitesUsed}/{fix.SatellitesVisible} sats";
		var snr = fix.MaxSnrDb.HasValue ? $" maxSNR:{fix.MaxSnrDb}dB" : "";
		var nmeaInfo = nmeaRxCount > 0 ? $" | NMEA rx: {nmeaRxCount}" : " | NMEA rx: 0 — no raw GPS data received (check baud rate / wiring)";
		return $"[{modeStr}] lat:{lat} lon:{lon} alt:{alt} time:{time} {sats}{snr} (#{acc.TotalFixUpdates}){nmeaInfo}";
	}
}
