using System.Net.Http.Json;
using System.Text.Json;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Gps;

/// <summary>
/// GPS adapter: probes the MicoAir MG-A01 via gps-svc REST API.
/// Connects to gps-svc on configurable host:restPort (default localhost:5200).
/// Step 1 — Availability: GET /api/fix returns the current fix snapshot.
/// Step 2 — Streaming: GET /api/stream/fixes (SSE) streams live GpsFix JSON
/// until the CancellationToken is cancelled (operator Stop).
/// Returns PASS (Ready) if a qualifying fix was obtained,
/// FAIL (Degraded) if gps-svc ran but no qualifying fix arrived,
/// or Unavailable if gps-svc is absent.
/// T016: Streaming probe with fix-based verdict and 14-field HealthSnapshot.
/// T024: Unavailable returned immediately when gps-svc is not reachable; other adapters not blocked.
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
	/// via GET /api/stream/fixes (SSE) until <paramref name="ct"/> is cancelled.
	/// Each parsed fix is published via <paramref name="reportStep"/> for live dashboard display.
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(
		RunMode mode,
		Func<string, string, bool, Task>? reportStep = null,
		CancellationToken ct = default)
	{
		var messages = new List<string>();

		// Resolve gps-svc endpoint from configuration (FR-001)
		var host = _config["Peripherals:Gps:Host"] ?? "localhost";
		var restPort = int.TryParse(_config["Peripherals:Gps:RestPort"], out var rp) ? rp : 5200;
		var timeoutMs = int.TryParse(_config["Peripherals:Gps:TimeoutMs"], out var t) ? t : 5_000;

		// Step 1: Check gps-svc availability via GET /api/fix (FR-001, FR-002)
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
		_logger.LogInformation("GPS probe: gps-svc reachable on {Host}:{Port}; starting SSE fix stream", host, restPort);

		// Step 2: Stream live fixes via GET /api/stream/fixes SSE (FR-003)
		var accumulator = new GpsFixAccumulator();

		try
		{
			var streamClient = _httpClientFactory.CreateClient("GpsSvc");
			streamClient.BaseAddress = new Uri($"http://{host}:{restPort}");
			streamClient.Timeout = Timeout.InfiniteTimeSpan; // SSE is long-lived

			using var stream = await streamClient.GetStreamAsync("/api/stream/fixes", ct);
			using var reader = new StreamReader(stream);

			while (!ct.IsCancellationRequested)
			{
				var line = await reader.ReadLineAsync(ct);
				if (line is null) break; // stream closed

				// SSE format: lines prefixed with "data:"
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
					// Malformed JSON line — skip silently (same resilience as old parser)
					continue;
				}

				if (fix is null)
					continue;

				accumulator.Update(fix);

				if (reportStep is not null)
				{
					var summary = FormatFixSummary(fix, accumulator);
					await reportStep("SSE /api/stream/fixes", summary, false);
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "GPS probe: SSE fix stream ended with exception");
			messages.Add($"Stream ended with error: {ex.GetType().Name}: {ex.Message}");
		}

		// Step 3: Determine final status based on whether a qualifying fix was captured
		var status = accumulator.FixObtained ? PeripheralStatus.Ready : PeripheralStatus.Degraded;

		if (accumulator.FixObtained)
		{
			messages.Add($"GPS PASS: qualifying fix obtained ({accumulator.TotalFixUpdates} fix updates received)");
			_logger.LogInformation("GPS probe: qualifying fix obtained after {Count} fix updates", accumulator.TotalFixUpdates);
		}
		else
		{
			// T015: actionable FAIL message
			messages.Add($"GPS test stopped: no qualifying fix obtained during session ({accumulator.TotalFixUpdates} fix updates received)");
			_logger.LogWarning("GPS probe: no qualifying fix after {Count} fix updates", accumulator.TotalFixUpdates);
		}

		// Step 4: Build 14-field HealthSnapshot (T016)
		var snapshotValues = accumulator.BuildSnapshot(reachable);

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
	/// Used for periodic health polling. Returns null if gps-svc is unreachable.
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

	// ── Private helpers ───────────────────────────────────────────────────────────────────────────────────────

	private static string FormatFixSummary(GpsFix fix, GpsFixAccumulator acc)
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
		return $"[{modeStr}] lat:{lat} lon:{lon} alt:{alt} time:{time} {sats}{snr} (#{acc.TotalFixUpdates})";
	}
}
