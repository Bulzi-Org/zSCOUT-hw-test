using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Gps;

/// <summary>
/// GPS adapter: probes the MicoAir MG-A01 via gpsd.
/// Uses gpspipe to capture one NMEA sentence as proof of communication.
/// </summary>
public sealed class GpsAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Gps;

	private readonly ILogger<GpsAdapter> _logger;
	private readonly IConfiguration _config;

	public GpsAdapter(ILogger<GpsAdapter> logger, IConfiguration config)
	{
		_logger = logger;
		_config = config;
	}

	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, CancellationToken ct = default)
	{
		var messages = new List<string>();

		// 1. Check gpsd is running (works in both host and container with host network)
		var psResult = await ProcessHelper.RunAsync("pgrep", "-x gpsd", 5_000, ct);
		var gpsdRunning = psResult.ExitCode == 0;
		messages.Add(gpsdRunning
			? "gpsd process found"
			: "gpsd not running – is gpsd installed and started?");

		if (!gpsdRunning)
			return DiagnosticEnvelope.Unavailable(PeripheralId, "gpsd service not running");

		// 2. Capture one NMEA sentence via gpspipe
		var pipeResult = await ProcessHelper.RunAsync(
			"gpspipe", "-r -n 5 -w", 10_000, ct);

		var nmeaLines = pipeResult.Stdout
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Where(l => l.StartsWith('$'))
			.ToList();

		var sampleCount = nmeaLines.Count;
		var status = sampleCount > 0 ? PeripheralStatus.Ready : PeripheralStatus.Degraded;

		if (sampleCount > 0)
			messages.Add($"Captured {sampleCount} NMEA sentence(s). First: {nmeaLines[0][..Math.Min(60, nmeaLines[0].Length)]}");
		else
		{
			messages.Add("gpspipe returned no NMEA sentences within timeout");
			if (!string.IsNullOrWhiteSpace(pipeResult.Stderr))
				messages.Add($"stderr: {pipeResult.Stderr.Trim()}");
		}

		return new DiagnosticEnvelope
		{
			PeripheralId = PeripheralId,
			DependencyAvailable = gpsdRunning,
			Status = status,
			Messages = messages,
			Snapshot = new HealthSnapshot
			{
				Values = new Dictionary<string, object?>
				{
					["gpsd_running"] = gpsdRunning,
					["nmea_sentence_count"] = sampleCount,
					["exit_code"] = pipeResult.ExitCode
				}
			},
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
	}

	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var result = await ProcessHelper.RunAsync("gpspipe", "-r -n 1 -w", 5_000, ct);
		var line = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault(l => l.StartsWith('$'));
		return line;
	}
}
