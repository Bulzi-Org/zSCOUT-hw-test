using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Halow;

/// <summary>
/// HaLow adapter: probes the Morse Micro MM8108 Wi-Fi HaLow via morse_driver.
/// Checks driver kernel module load status and interface presence.
/// Container mode: requires host networking + privileged to access /sys.
/// </summary>
public sealed class HalowAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Halow;

	private readonly ILogger<HalowAdapter> _logger;

	public HalowAdapter(ILogger<HalowAdapter> logger)
	{
		_logger = logger;
	}

	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, CancellationToken ct = default)
	{
		var messages = new List<string>();

		// 1. Check if morse driver kernel module is loaded
		var lsmodResult = await ProcessHelper.RunAsync("lsmod", "", 5_000, ct);
		var moduleLoaded = lsmodResult.ExitCode == 0 &&
						   lsmodResult.Stdout.Contains("morse", StringComparison.OrdinalIgnoreCase);

		messages.Add(moduleLoaded
			? "morse kernel module loaded"
			: "morse kernel module not found in lsmod output");

		if (!moduleLoaded)
		{
			// Also check modinfo as a secondary check
			var modinfoResult = await ProcessHelper.RunAsync("modinfo", "morse", 5_000, ct);
			if (modinfoResult.ExitCode == 0)
				messages.Add("morse module available but not loaded – try: modprobe morse");
			else
				messages.Add("morse module not available on this system");

			return DiagnosticEnvelope.Unavailable(PeripheralId, "morse kernel module not loaded");
		}

		// 2. Find HaLow network interface (typically wlan0 or morse0)
		var ipResult = await ProcessHelper.RunAsync("ip", "link show", 5_000, ct);
		var halowIface = ipResult.Stdout
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Where(l => l.Contains("morse", StringComparison.OrdinalIgnoreCase) ||
						l.Contains("wlan", StringComparison.OrdinalIgnoreCase))
			.Select(l => l.Trim())
			.FirstOrDefault();

		var ifaceFound = halowIface is not null;
		messages.Add(ifaceFound
			? $"HaLow interface found: {halowIface}"
			: "No HaLow network interface found via ip link");

		// 3. Check MM8108 device presence in sysfs
		var sysfsResult = await ProcessHelper.RunAsync(
			"find", "/sys/bus/sdio/devices /sys/bus/pci/devices -name '*morse*' 2>/dev/null", 5_000, ct);
		var sysfsFound = !string.IsNullOrWhiteSpace(sysfsResult.Stdout);
		messages.Add(sysfsFound
			? $"MM8108 device found in sysfs: {sysfsResult.Stdout.Trim()}"
			: "MM8108 not found in sysfs device tree (may be OK if already bound to driver)");

		var status = ifaceFound ? PeripheralStatus.Ready : PeripheralStatus.Degraded;

		return new DiagnosticEnvelope
		{
			PeripheralId = PeripheralId,
			DependencyAvailable = moduleLoaded,
			Status = status,
			Messages = messages,
			Snapshot = new HealthSnapshot
			{
				Values = new Dictionary<string, object?>
				{
					["module_loaded"] = moduleLoaded,
					["interface_found"] = ifaceFound,
					["interface_name"] = halowIface,
					["sysfs_device_found"] = sysfsFound
				}
			},
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
	}

	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var result = await ProcessHelper.RunAsync("ip", "link show", 3_000, ct);
		return result.ExitCode == 0 ? result.Stdout.Trim() : null;
	}
}
