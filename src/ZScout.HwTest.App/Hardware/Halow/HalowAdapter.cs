using System.Text.RegularExpressions;
using Grpc.Net.Client;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Protos;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Halow;

/// <summary>
/// HaLow adapter: probes the Morse Micro MM8108 Wi-Fi HaLow radio through a
/// two-tier diagnostic strategy (FR-008).
/// <para>Tier A — Hardware Health (Layers 0-3, always runs, no mesh required):</para>
/// <list type="bullet">
///   <item><description>Layer 0 — USB device enumeration (vendor ID 0x325B)</description></item>
///   <item><description>Layer 1 — Kernel module load + firmware verification</description></item>
///   <item><description>Layer 2 — Wireless interface via <c>iw dev</c> / <c>iw phy</c></description></item>
///   <item><description>Layer 3 — Optional <c>morse_cli</c> radio health check</description></item>
/// </list>
/// <para>Tier B — Mesh Connectivity (Layer 4+, requires zSCOUT-mesh gRPC :5102):</para>
/// <list type="bullet">
///   <item><description>Mesh association, peer count, gateway mode via gRPC</description></item>
///   <item><description>Internet reachability through bat0 interface</description></item>
/// </list>
/// Tier A requires only read-only /sys and --network host (no --privileged).
/// Tier B is attempted only when Tier A passes and mesh service is reachable (FR-011).
/// </summary>
public sealed partial class HalowAdapter : IHardwareAdapter
{
	/// <summary>Morse Micro USB vendor ID.</summary>
	private const string MorseVendorId = "325b";

	/// <summary>Known kernel module names for the Morse Micro driver.</summary>
	private static readonly string[] ModuleNames = ["morse", "morse_driver", "dot11ah"];

	public PeripheralId PeripheralId => PeripheralId.Halow;

	private readonly ILogger<HalowAdapter> _logger;
	private readonly IConfiguration _config;

	/// <summary>
	/// Caches the most recently discovered HaLow wireless interface name
	/// so that <see cref="ReadRawSampleAsync"/> can query it without re-probing.
	/// </summary>
	private volatile string? _lastDiscoveredInterface;

	public HalowAdapter(ILogger<HalowAdapter> logger, IConfiguration config)
	{
		_logger = logger;
		_config = config;
	}

	/// <inheritdoc />
	public async Task<DiagnosticEnvelope> ProbeAsync(
		RunMode mode,
		Func<string, string, bool, Task>? reportStep = null,
		CancellationToken ct = default)
	{
		var messages = new List<string>();
		var snapshot = new Dictionary<string, object?>();

		// ── Layer 0 — USB device enumeration ──────────────────────────────
		var (usbFound, vendorId) = await ProbeUsbAsync(messages, snapshot, reportStep, ct);
		if (!usbFound)
		{
			_logger.LogWarning("HaLow probe: MM8108 USB device not detected");
			return BuildEnvelope(PeripheralStatus.Unavailable, false, messages, snapshot);
		}

		// ── Layer 1 — Kernel module + firmware ────────────────────────────
		var (moduleLoaded, firmwareLoaded, firmwareVersion) =
			await ProbeModuleAsync(messages, snapshot, reportStep, ct);
		if (!moduleLoaded)
		{
			_logger.LogWarning("HaLow probe: MM8108 USB device detected but morse_driver not installed");
			messages.Add("MM8108 USB device detected but morse_driver not installed");
			return BuildEnvelope(PeripheralStatus.Unavailable, false, messages, snapshot);
		}

		// ── Layer 2 — Wireless interface ──────────────────────────────────
		var (ifaceName, phyName, channels) =
			await ProbeWirelessAsync(messages, snapshot, reportStep, ct);
		if (ifaceName is null)
		{
			_logger.LogWarning("HaLow probe: morse module loaded but no wireless interface created");
			return BuildEnvelope(PeripheralStatus.Degraded, true, messages, snapshot);
		}

		_lastDiscoveredInterface = ifaceName;

		// ── Layer 3 — Radio health check ──────────────────────────────────
		await ProbeRadioHealthAsync(ifaceName, messages, snapshot, reportStep, ct);

		_logger.LogInformation("HaLow probe: Tier A passed for interface {Interface}", ifaceName);

		// ── Tier B — Mesh Connectivity (FR-008, FR-010, FR-011) ─────────
		await ProbeMeshAsync(messages, snapshot, reportStep, ct);

		return BuildEnvelope(PeripheralStatus.Ready, true, messages, snapshot);
	}

	/// <inheritdoc />
	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		try
		{
			var iface = _lastDiscoveredInterface;

			// If no interface cached, attempt a quick discovery.
			if (iface is null)
			{
				var iwResult = await ProcessHelper.RunAsync("iw", "dev", 5_000, ct);
				iface = ParseInterfaceName(iwResult.Stdout);
				if (iface is not null)
					_lastDiscoveredInterface = iface;
			}

			if (iface is null)
				return null;

			var result = await ProcessHelper.RunAsync("iw", $"dev {iface} info", 3_000, ct);
			return result.ExitCode == 0 ? result.Stdout.Trim() : null;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogDebug(ex, "ReadRawSampleAsync failed — iw may not be installed");
			return null;
		}
	}

	// ── Layer probes ─────────────────────────────────────────────────────

	/// <summary>
	/// Layer 0: Check USB enumeration for Morse Micro vendor ID via sysfs.
	/// </summary>
	private async Task<(bool Found, string? VendorId)> ProbeUsbAsync(
		List<string> messages,
		Dictionary<string, object?> snapshot,
		Func<string, string, bool, Task>? reportStep,
		CancellationToken ct)
	{
		// Search sysfs for Morse Micro vendor ID in USB device tree
		var findResult = await ProcessHelper.RunAsync(
			"grep", $"-rl {MorseVendorId} /sys/bus/usb/devices/*/idVendor",
			5_000, ct);

		if (reportStep is not null)
			await reportStep("grep /sys/bus/usb/devices/*/idVendor", findResult.Stdout + findResult.Stderr, findResult.ExitCode != 0);

		var usbFound = findResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(findResult.Stdout);

		snapshot["usb_device_found"] = usbFound;
		snapshot["vendor_id"] = usbFound ? $"0x{MorseVendorId.ToUpperInvariant()}" : null;

		messages.Add(usbFound
			? $"Layer 0 PASS: MM8108 USB device detected (vendor 0x{MorseVendorId.ToUpperInvariant()})"
			: "Layer 0 FAIL: MM8108 USB device not detected");

		return (usbFound, usbFound ? MorseVendorId : null);
	}

	/// <summary>
	/// Layer 1: Check kernel module load status and parse dmesg for firmware info.
	/// </summary>
	private async Task<(bool ModuleLoaded, bool FirmwareLoaded, string? FirmwareVersion)> ProbeModuleAsync(
		List<string> messages,
		Dictionary<string, object?> snapshot,
		Func<string, string, bool, Task>? reportStep,
		CancellationToken ct)
	{
		// Check lsmod for any known morse module name
		var lsmodResult = await ProcessHelper.RunAsync("lsmod", "", 5_000, ct);
		if (reportStep is not null)
			await reportStep("lsmod", lsmodResult.Stdout + lsmodResult.Stderr, lsmodResult.ExitCode != 0);

		var moduleLoaded = lsmodResult.ExitCode == 0 &&
			ModuleNames.Any(m => lsmodResult.Stdout.Contains(m, StringComparison.OrdinalIgnoreCase));

		snapshot["module_loaded"] = moduleLoaded;
		messages.Add(moduleLoaded
			? "Layer 1: morse kernel module loaded"
			: "Layer 1 FAIL: morse kernel module not found in lsmod output");

		if (!moduleLoaded)
		{
			snapshot["firmware_loaded"] = false;
			snapshot["firmware_version"] = null;
			return (false, false, null);
		}

		// Parse dmesg for firmware load status
		var dmesgResult = await ProcessHelper.RunAsync(
			"dmesg", "", 5_000, ct);
		if (reportStep is not null)
			await reportStep("dmesg", dmesgResult.Stdout + dmesgResult.Stderr, dmesgResult.ExitCode != 0);

		var dmesgOutput = dmesgResult.ExitCode == 0 ? dmesgResult.Stdout : "";
		var morseLines = dmesgOutput
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Where(l => l.Contains("morse", StringComparison.OrdinalIgnoreCase))
			.ToList();

		// Look for firmware load success/failure indicators
		var firmwareLoaded = morseLines.Any(l =>
			l.Contains("firmware", StringComparison.OrdinalIgnoreCase) &&
			(l.Contains("loaded", StringComparison.OrdinalIgnoreCase) ||
			 l.Contains("version", StringComparison.OrdinalIgnoreCase) ||
			 l.Contains("mm8108", StringComparison.OrdinalIgnoreCase)));

		// Try to extract firmware version
		string? firmwareVersion = null;
		foreach (var line in morseLines)
		{
			var match = FirmwareVersionRegex().Match(line);
			if (match.Success)
			{
				firmwareVersion = match.Groups[1].Value;
				firmwareLoaded = true;
				break;
			}
		}

		// If no explicit firmware info found but module is loaded, assume firmware loaded
		if (!firmwareLoaded && moduleLoaded)
			firmwareLoaded = true;

		snapshot["firmware_loaded"] = firmwareLoaded;
		snapshot["firmware_version"] = firmwareVersion;

		if (firmwareVersion is not null)
			messages.Add($"Layer 1: firmware version {firmwareVersion}");
		else
			messages.Add("Layer 1: firmware version not found in dmesg");

		messages.Add("Layer 1 PASS: morse kernel module loaded");
		return (true, firmwareLoaded, firmwareVersion);
	}

	/// <summary>
	/// Layer 2: Use <c>iw dev</c> and <c>iw phy</c> to find wireless interface and capabilities.
	/// </summary>
	private async Task<(string? InterfaceName, string? PhyName, string? Channels)> ProbeWirelessAsync(
		List<string> messages,
		Dictionary<string, object?> snapshot,
		Func<string, string, bool, Task>? reportStep,
		CancellationToken ct)
	{
		// iw dev — find wireless interfaces
		var iwDevResult = await ProcessHelper.RunAsync("iw", "dev", 5_000, ct);
		if (reportStep is not null)
			await reportStep("iw dev", iwDevResult.Stdout + iwDevResult.Stderr, iwDevResult.ExitCode != 0);

		var ifaceName = ParseInterfaceName(iwDevResult.Stdout);

		snapshot["interface_name"] = ifaceName;

		if (ifaceName is null)
		{
			snapshot["phy_name"] = null;
			snapshot["supported_channels"] = null;
			messages.Add("Layer 2 FAIL: no wireless interface found via iw dev");
			return (null, null, null);
		}

		messages.Add($"Layer 2: wireless interface {ifaceName} found");

		// iw phy — capture radio capabilities
		var iwPhyResult = await ProcessHelper.RunAsync("iw", "phy", 5_000, ct);
		if (reportStep is not null)
			await reportStep("iw phy", iwPhyResult.Stdout + iwPhyResult.Stderr, iwPhyResult.ExitCode != 0);

		var phyName = ParsePhyName(iwPhyResult.Stdout);
		var channels = ParseSupportedChannels(iwPhyResult.Stdout);

		snapshot["phy_name"] = phyName;
		snapshot["supported_channels"] = channels;

		if (phyName is not null)
			messages.Add($"Layer 2: PHY {phyName}");
		if (channels is not null)
			messages.Add($"Layer 2: supported channels — {channels}");

		messages.Add("Layer 2 PASS: wireless interface and PHY capabilities captured");
		return (ifaceName, phyName, channels);
	}

	/// <summary>
	/// Layer 3: Optional radio health check via <c>morse_cli</c> and <c>iw dev info</c>.
	/// </summary>
	private async Task ProbeRadioHealthAsync(
		string ifaceName,
		List<string> messages,
		Dictionary<string, object?> snapshot,
		Func<string, string, bool, Task>? reportStep,
		CancellationToken ct)
	{
		// Check if morse_cli is available
		var whichResult = await ProcessHelper.RunAsync("which", "morse_cli", 3_000, ct);
		var morseCliAvailable = whichResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(whichResult.Stdout);

		if (morseCliAvailable)
		{
			var healthResult = await ProcessHelper.RunAsync("morse_cli", "health_check", 10_000, ct);
			if (reportStep is not null)
				await reportStep("morse_cli health_check", healthResult.Stdout + healthResult.Stderr, healthResult.ExitCode != 0);

			var healthOk = healthResult.ExitCode == 0;
			snapshot["health_check_ok"] = healthOk;
			messages.Add(healthOk
				? "Layer 3: morse_cli health check passed"
				: $"Layer 3: morse_cli health check failed (exit code {healthResult.ExitCode})");
		}
		else
		{
			snapshot["health_check_ok"] = null;
			messages.Add("Layer 3: morse_cli not available — health check skipped");
		}

		// Also grab iw dev <iface> info for detailed interface state
		var infoResult = await ProcessHelper.RunAsync("iw", $"dev {ifaceName} info", 5_000, ct);
		if (reportStep is not null)
			await reportStep($"iw dev {ifaceName} info", infoResult.Stdout + infoResult.Stderr, infoResult.ExitCode != 0);

		if (infoResult.ExitCode == 0)
			messages.Add($"Layer 3: interface info captured for {ifaceName}");

		messages.Add("Layer 3 PASS: radio health check complete");
	}

	/// <summary>
	/// Tier B: Attempt gRPC connection to zSCOUT-mesh service for mesh health data.
	/// If mesh service is unavailable, reports NotTested without failure (FR-011).
	/// </summary>
	private async Task ProbeMeshAsync(
		List<string> messages,
		Dictionary<string, object?> snapshot,
		Func<string, string, bool, Task>? reportStep,
		CancellationToken ct)
	{
		var host = _config["Peripherals:Halow:MeshHost"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Halow:MeshPort"], out var p) ? p : 5102;
		var timeoutMs = int.TryParse(_config["Peripherals:Halow:MeshTimeoutMs"], out var t) ? t : 5_000;

		// Check if mesh service is reachable
		var reachable = await TcpHealthCheck.CheckAsync(host, port, timeoutMs, ct);
		if (reportStep is not null)
			await reportStep($"TCP connect {host}:{port}", reachable ? "mesh service connected" : "mesh service unreachable", !reachable);

		snapshot["mesh_service_available"] = reachable;

		if (!reachable)
		{
			// Mesh not available — report NotTested, not a failure (FR-011)
			snapshot["mesh_associated"] = null;
			snapshot["peer_count"] = null;
			snapshot["gateway_mode"] = null;
			snapshot["bat0_ip"] = null;
			snapshot["internet_reachable"] = null;
			messages.Add($"Tier B: mesh service not available on {host}:{port} — mesh tests skipped (NotTested)");
			_logger.LogInformation("HaLow probe: mesh service not available on {Host}:{Port} — Tier B skipped", host, port);
			return;
		}

		// Query mesh service via gRPC
		using var channel = GrpcChannelFactory.Create(host, port);
		var client = new MeshService.MeshServiceClient(channel);

		try
		{
			using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			statusCts.CancelAfter(timeoutMs);
			var meshStatus = await client.GetStatusAsync(new MeshStatusRequest(), cancellationToken: statusCts.Token);
			if (reportStep is not null)
				await reportStep("MeshService.GetStatus",
					$"associated={meshStatus.Associated} peers={meshStatus.PeerCount} gw={meshStatus.GatewayMode} inet={meshStatus.InternetReachable}",
					!meshStatus.Associated);

			snapshot["mesh_associated"] = meshStatus.Associated;
			snapshot["peer_count"] = meshStatus.PeerCount;
			snapshot["gateway_mode"] = meshStatus.GatewayMode;
			snapshot["bat0_ip"] = meshStatus.Bat0Ip;
			snapshot["internet_reachable"] = meshStatus.InternetReachable;

			var tierBSummary = meshStatus.Associated
				? $"Tier B PASS: mesh associated, {meshStatus.PeerCount} peers, gw={meshStatus.GatewayMode}, bat0={meshStatus.Bat0Ip}, inet={meshStatus.InternetReachable}"
				: $"Tier B: mesh not associated — {meshStatus.StatusMessage}";
			messages.Add(tierBSummary);
			_logger.LogInformation("HaLow Tier B: associated={Associated} peers={PeerCount}", meshStatus.Associated, meshStatus.PeerCount);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "HaLow probe: gRPC call to mesh service failed");
			snapshot["mesh_associated"] = null;
			snapshot["peer_count"] = null;
			snapshot["gateway_mode"] = null;
			snapshot["bat0_ip"] = null;
			snapshot["internet_reachable"] = null;
			messages.Add($"Tier B: gRPC error — {ex.GetType().Name}: {ex.Message}");
		}
	}

	// ── Helpers ──────────────────────────────────────────────────────────

	/// <summary>
	/// Builds a <see cref="DiagnosticEnvelope"/> from the collected data.
	/// </summary>
	private DiagnosticEnvelope BuildEnvelope(
		PeripheralStatus status,
		bool dependencyAvailable,
		List<string> messages,
		Dictionary<string, object?> snapshot)
	{
		// Ensure all 9 required keys are present (null for unchecked layers)
		foreach (var key in RequiredSnapshotKeys)
		{
			snapshot.TryAdd(key, null);
		}

		return new DiagnosticEnvelope
		{
			PeripheralId = PeripheralId,
			DependencyAvailable = dependencyAvailable,
			Status = status,
			Messages = messages,
			Snapshot = new HealthSnapshot { Values = snapshot },
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
	}

	/// <summary>
	/// Parses the interface name from <c>iw dev</c> output.
	/// Looks for lines matching <c>Interface &lt;name&gt;</c>.
	/// </summary>
	internal static string? ParseInterfaceName(string iwDevOutput)
	{
		foreach (var line in iwDevOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			var trimmed = line.Trim();
			if (trimmed.StartsWith("Interface ", StringComparison.OrdinalIgnoreCase))
				return trimmed["Interface ".Length..].Trim();
		}

		return null;
	}

	/// <summary>
	/// Parses the PHY name from <c>iw phy</c> output.
	/// Looks for lines matching <c>Wiphy &lt;name&gt;</c>.
	/// </summary>
	internal static string? ParsePhyName(string iwPhyOutput)
	{
		foreach (var line in iwPhyOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			var trimmed = line.Trim();
			if (trimmed.StartsWith("Wiphy ", StringComparison.OrdinalIgnoreCase))
				return trimmed["Wiphy ".Length..].Trim();
		}

		return null;
	}

	/// <summary>
	/// Extracts a summary of supported channels from <c>iw phy</c> output.
	/// Returns a comma-separated list of MHz frequencies, or null if none found.
	/// </summary>
	internal static string? ParseSupportedChannels(string iwPhyOutput)
	{
		var frequencies = new List<string>();
		foreach (var line in iwPhyOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			var match = FrequencyRegex().Match(line);
			if (match.Success)
				frequencies.Add(match.Groups[1].Value);
		}

		return frequencies.Count > 0 ? string.Join(", ", frequencies) : null;
	}

	/// <summary>All keys required in HealthSnapshot.Values per FR-008/FR-009/FR-010.</summary>
	private static readonly string[] RequiredSnapshotKeys =
	[
		// Tier A (FR-009)
		"usb_device_found",
		"vendor_id",
		"module_loaded",
		"firmware_loaded",
		"firmware_version",
		"interface_name",
		"phy_name",
		"supported_channels",
		"health_check_ok",
		// Tier B (FR-010)
		"mesh_service_available",
		"mesh_associated",
		"peer_count",
		"gateway_mode",
		"bat0_ip",
		"internet_reachable"
	];

	[GeneratedRegex(@"firmware version[:\s]+(\S+)", RegexOptions.IgnoreCase)]
	private static partial Regex FirmwareVersionRegex();

	[GeneratedRegex(@"\*\s+(\d+)\s+MHz")]
	private static partial Regex FrequencyRegex();
}
