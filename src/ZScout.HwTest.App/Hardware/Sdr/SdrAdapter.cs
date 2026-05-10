using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Sdr;

/// <summary>
/// SDR adapter: probes the Wavelet-Lab uSDR via SoapySDR.
/// Uses SoapySDRUtil --probe to enumerate device and verify USB enumeration.
/// </summary>
public sealed class SdrAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Sdr;

	private readonly ILogger<SdrAdapter> _logger;

	public SdrAdapter(ILogger<SdrAdapter> logger)
	{
		_logger = logger;
	}

	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, CancellationToken ct = default)
	{
		var messages = new List<string>();

		// 1. Enumerate SoapySDR devices
		var findResult = await ProcessHelper.RunAsync(
			"SoapySDRUtil", "--find", 10_000, ct);

		var found = findResult.ExitCode == 0 &&
					findResult.Stdout.Contains("driver", StringComparison.OrdinalIgnoreCase);

		if (!found)
		{
			messages.Add("SoapySDRUtil --find returned no devices or failed");
			if (!string.IsNullOrWhiteSpace(findResult.Stderr))
				messages.Add($"stderr: {findResult.Stderr.Trim()}");
			return DiagnosticEnvelope.Unavailable(PeripheralId, "No SoapySDR device found");
		}

		messages.Add("SoapySDR device enumerated successfully");

		// 2. Probe device details
		var probeResult = await ProcessHelper.RunAsync(
			"SoapySDRUtil", "--probe", 15_000, ct);

		var probeOk = probeResult.ExitCode == 0;
		messages.Add(probeOk
			? "SoapySDRUtil --probe succeeded"
			: $"SoapySDRUtil --probe exited {probeResult.ExitCode}");

		// Extract driver line for snapshot
		var driverLine = findResult.Stdout
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault(l => l.Contains("driver", StringComparison.OrdinalIgnoreCase))
			?.Trim();

		return new DiagnosticEnvelope
		{
			PeripheralId = PeripheralId,
			DependencyAvailable = true,
			Status = probeOk ? PeripheralStatus.Ready : PeripheralStatus.Degraded,
			Messages = messages,
			Snapshot = new HealthSnapshot
			{
				Values = new Dictionary<string, object?>
				{
					["device_found"] = found,
					["probe_exit_code"] = probeResult.ExitCode,
					["driver_info"] = driverLine
				}
			},
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
	}

	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var result = await ProcessHelper.RunAsync("SoapySDRUtil", "--find", 5_000, ct);
		return result.ExitCode == 0 ? result.Stdout.Trim() : null;
	}
}
