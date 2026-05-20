using System.Text.Json;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Gps;

/// <summary>
/// GPS adapter: probes the MicoAir MG-A01 via gps-svc REST API and gpspipe streaming.
/// Connects to gps-svc on configurable host:port (default localhost:5200 for REST status,
/// localhost:2947 for gpsd TCP streaming) instead of checking for a host gpsd process
/// (which fails in container PID namespaces — #23 Bug 1).
/// Streams live GNSS fix data from <c>gpspipe -w</c> (JSON mode) until the run is stopped
/// or the CancellationToken is cancelled. Returns PASS (Ready) if a complete fix was obtained,
/// FAIL (Degraded) if gps-svc ran but no qualifying fix arrived, or Unavailable if gps-svc is absent.
/// T016: Streaming probe with fix-based verdict and 14-field HealthSnapshot.
/// T024: Unavailable returned immediately when gps-svc is not reachable; other adapters not blocked.
/// </summary>
public sealed class GpsAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Gps;

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
	/// Streams live GNSS fix data until <paramref name="ct"/> is cancelled (operator Stop)
	/// or the gpspipe process exits. Each parsed fix update is published via
	/// <paramref name="reportStep"/> for live dashboard display.
	/// Checks gps-svc availability via REST (FR-001, FR-002), then streams via gpspipe.
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(
		RunMode mode,
		Func<string, string, bool, Task>? reportStep = null,
		CancellationToken ct = default)
	{
		var messages = new List<string>();

		// Resolve gps-svc endpoint from configuration (FR-001, FR-013)
		var host = _config["Peripherals:Gps:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Gps:Port"], out var p) ? p : 2947;
		var restPort = int.TryParse(_config["Peripherals:Gps:RestPort"], out var rp) ? rp : 5200;
		var timeoutMs = int.TryParse(_config["Peripherals:Gps:TimeoutMs"], out var t) ? t : 5_000;

		// Step 1: Check gps-svc availability via REST status endpoint (FR-002)
		var reachable = false;
		try
		{
			var client = _httpClientFactory.CreateClient("GpsSvc");
			client.BaseAddress = new Uri($"http://{host}:{restPort}");
			client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
			using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			statusCts.CancelAfter(timeoutMs);
			using var statusResponse = await client.GetAsync("/api/status", statusCts.Token);
			reachable = statusResponse.IsSuccessStatusCode;
			if (reportStep is not null)
				await reportStep($"GET http://{host}:{restPort}/api/status", reachable ? "ok" : $"HTTP {(int)statusResponse.StatusCode}", !reachable);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (reportStep is not null)
				await reportStep($"GET http://{host}:{restPort}/api/status", $"unreachable: {ex.GetType().Name}", true);
		}

		if (!reachable)
		{
			// Fallback: try TCP connectivity to gpsd port (T024)
			reachable = await TcpHealthCheck.CheckAsync(host, port, timeoutMs, ct);
			if (reportStep is not null)
				await reportStep($"TCP connect {host}:{port}", reachable ? "connected" : "unreachable", !reachable);
		}

		if (!reachable)
		{
			_logger.LogWarning("GPS probe: gps-svc not reachable on {Host}:{Port}", host, restPort);
			return DiagnosticEnvelope.Unavailable(PeripheralId, $"gps-svc not reachable on {host}:{restPort}");
		}

		messages.Add($"gps-svc reachable on {host}:{restPort}; starting live GNSS fix stream");
		_logger.LogInformation("GPS probe: gps-svc reachable on {Host}:{Port}; starting gpspipe -w stream", host, restPort);

		// Step 2: Stream gpspipe -w JSON output until ct is cancelled (FR-003)
		// Connect gpspipe to gps-svc host explicitly
		var accumulator = new GpsFixAccumulator();
		string? streamStderr = null;
		var gpspipeArgs = host == "localhost" ? "-w" : $"-w -l {host}";

		try
		{
			streamStderr = await ProcessHelper.StreamLinesAsync(
				"gpspipe", gpspipeArgs,
				async (line, lineCt) =>
				{
					var fix = GnssJsonParser.Parse(line);
					if (fix is null) return;

					accumulator.Update(fix);

					if (reportStep is not null)
					{
						var summary = FormatFixSummary(fix, accumulator);
						await reportStep("gpspipe -w", summary, false);
					}
				},
				ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "GPS probe: gpspipe -w stream ended with exception");
			messages.Add($"Stream ended with error: {ex.GetType().Name}: {ex.Message}");
		}

		if (!string.IsNullOrWhiteSpace(streamStderr))
		{
			messages.Add($"gpspipe stderr: {streamStderr.Trim()}");
			_logger.LogDebug("GPS probe gpspipe stderr: {Stderr}", streamStderr.Trim());
		}

		// Step 3: Determine final status based on whether a qualifying fix was captured
		var status = accumulator.FixObtained ? PeripheralStatus.Ready : PeripheralStatus.Degraded;

		if (accumulator.FixObtained)
		{
			messages.Add($"GPS PASS: qualifying fix obtained ({accumulator.TotalFixUpdates} TPV updates received)");
			_logger.LogInformation("GPS probe: qualifying fix obtained after {Count} TPV updates", accumulator.TotalFixUpdates);
		}
		else
		{
			// T015: actionable FAIL message
			messages.Add($"GPS test stopped: no qualifying fix obtained during session ({accumulator.TotalFixUpdates} TPV updates received)");
			_logger.LogWarning("GPS probe: no qualifying fix after {Count} TPV updates", accumulator.TotalFixUpdates);
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
	/// Returns a single raw NMEA sentence via a separate one-shot gpspipe call.
	/// Independent of any active streaming session. Used for periodic health polling.
	/// </summary>
	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var result = await ProcessHelper.RunAsync("gpspipe", "-r -n 1 -w", 5_000, ct);
		var line = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault(l => l.StartsWith('$'));
		return line;
	}

	// ── Private helpers ───────────────────────────────────────────────────────

	private static string FormatFixSummary(GnssFixUpdate fix, GpsFixAccumulator acc)
	{
		if (fix.Class == "TPV")
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
			var sky = acc.LastSkyUpdate;
			var sats = sky is not null ? $"{sky.SatellitesUsed}/{sky.SatellitesVisible} sats" : "sats: --";
			var snr = sky?.MaxSnrDb.HasValue == true ? $" maxSNR:{sky.MaxSnrDb}dB" : "";
			return $"[{modeStr}] lat:{lat} lon:{lon} alt:{alt} time:{time} {sats}{snr} (#{acc.TotalFixUpdates})";
		}

		if (fix.Class == "SKY")
			return $"[SKY] {fix.SatellitesUsed}/{fix.SatellitesVisible} sats used/visible, maxSNR:{fix.MaxSnrDb?.ToString() ?? "--"}dB";

		return $"[{fix.Class}] (parsed)";
	}
}
