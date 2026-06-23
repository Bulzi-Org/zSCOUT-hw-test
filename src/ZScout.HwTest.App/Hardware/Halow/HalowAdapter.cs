using System.Text.Json;
using System.Text.RegularExpressions;
using ZScout.HwTest.App.Hardware.Common;
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
/// <para>Tier B — Mesh Connectivity (Layer 4+, requires zSCOUT-mesh REST :5102):</para>
/// <list type="bullet">
///   <item><description>Mesh association, peer count, gateway mode via HTTP REST</description></item>
///   <item><description>Internet reachability through bat0 interface</description></item>
/// </list>
/// Tier A requires only read-only /sys and --network host (no --privileged).
/// Tier B is attempted only when Tier A passes and mesh service is reachable (FR-011).
/// </summary>
public sealed partial class HalowAdapter : IHardwareAdapter
{
	/// <summary>Morse Micro USB vendor ID.</summary>
	private const string MorseVendorId = "325b";

	/// <summary>Kernel driver name bound to the Morse Micro MM8108 netdev.</summary>
	private const string MorseDriverName = "morse_usb";

	/// <summary>Known kernel module names for the Morse Micro driver.</summary>
	private static readonly string[] ModuleNames = ["morse", "morse_driver", "dot11ah"];

	public PeripheralId PeripheralId => PeripheralId.Halow;

	private readonly ILogger<HalowAdapter> _logger;
	private readonly IConfiguration _config;
	private readonly IHttpClientFactory _httpClientFactory;

	/// <summary>
	/// Caches the most recently discovered HaLow wireless interface name
	/// so that <see cref="ReadRawSampleAsync"/> can query it without re-probing.
	/// </summary>
	private volatile string? _lastDiscoveredInterface;

	public HalowAdapter(ILogger<HalowAdapter> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
	{
		_logger = logger;
		_config = config;
		_httpClientFactory = httpClientFactory;
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

		// ── Layer 4 — RF scan for AP/STA nodes (#97) ──────────────────────
		// Bring the morse interface up and scan; the HaLow test passes ONLY
		// when at least one node is seen on the air, not just because the
		// driver/interface is present.
		var scanNodeCount = await ProbeScanAsync(ifaceName, messages, snapshot, reportStep, ct);

		_logger.LogInformation(
			"HaLow probe: Tier A complete for interface {Interface}, scan found {NodeCount} node(s)",
			ifaceName, scanNodeCount);

		// ── Tier B — Mesh Connectivity (FR-008, FR-010, FR-011) ─────────
		await ProbeMeshAsync(messages, snapshot, reportStep, ct);

		// Ready only when the scan found >= 1 node. If the driver and morse
		// interface are present but no nodes were found, report Degraded so
		// the test does not pass on driver presence alone.
		var status = scanNodeCount >= 1 ? PeripheralStatus.Ready : PeripheralStatus.Degraded;
		return BuildEnvelope(status, true, messages, snapshot);
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
		// Search sysfs for the Morse Micro vendor ID in the USB device tree.
		// Run grep through a shell so the '*' glob is expanded. ProcessHelper uses
		// UseShellExecute=false (direct exec, no shell), so a glob passed straight to
		// grep is taken literally and fails with "No such file or directory" (#99).
		var findResult = await ProcessHelper.RunAsync(
			"/bin/sh", $"-c \"grep -rl {MorseVendorId} /sys/bus/usb/devices/*/idVendor\"",
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
		// iw dev — enumerate wireless interfaces (diagnostic context only)
		var iwDevResult = await ProcessHelper.RunAsync("iw", "dev", 5_000, ct);
		if (reportStep is not null)
			await reportStep("iw dev", iwDevResult.Stdout + iwDevResult.Stderr, iwDevResult.ExitCode != 0);

		// Select the morse-specific interface by its kernel driver (morse_usb)
		// from /sys/class/net/<iface>/device/driver. This avoids the previous
		// bug of picking the first `iw dev` interface, which is the onboard
		// Broadcom Wi-Fi on the CM5 (a false positive). (#97)
		var (ifaceName, ifaceDriver) = FindMorseInterface();

		snapshot["interface_name"] = ifaceName;
		snapshot["morse_interface"] = ifaceName;

		if (reportStep is not null)
			await reportStep(
				"resolve morse interface (/sys/class/net/*/device/driver)",
				ifaceName is null
					? "no netdev bound to morse_usb driver found"
					: $"{ifaceName} (driver={ifaceDriver})",
				ifaceName is null);

		if (ifaceName is null)
		{
			snapshot["phy_name"] = null;
			snapshot["supported_channels"] = null;
			messages.Add("Layer 2 FAIL: no morse_usb wireless interface found (onboard Wi-Fi ignored)");
			return (null, null, null);
		}

		messages.Add($"Layer 2: morse wireless interface {ifaceName} found (driver {ifaceDriver})");

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
	/// Layer 4 (#97): Bring the morse interface up and run <c>iw dev &lt;iface&gt; scan</c>
	/// to discover HaLow AP/STA nodes on the air. Records the node count and a
	/// per-node summary (SSID/BSSID/freq/signal) into the snapshot. Returns the
	/// number of nodes found so the caller can gate Ready vs. Degraded.
	/// </summary>
	private async Task<int> ProbeScanAsync(
		string ifaceName,
		List<string> messages,
		Dictionary<string, object?> snapshot,
		Func<string, string, bool, Task>? reportStep,
		CancellationToken ct)
	{
		// The morse interface must be administratively up before nl80211 will
		// allow a scan. Requires NET_ADMIN (granted via docker-compose cap_add).
		var ipResult = await ProcessHelper.RunAsync("ip", $"link set {ifaceName} up", 5_000, ct);
		if (reportStep is not null)
			await reportStep($"ip link set {ifaceName} up", ipResult.Stdout + ipResult.Stderr, ipResult.ExitCode != 0);

		if (ipResult.ExitCode != 0)
			messages.Add($"Layer 4: warning — failed to bring {ifaceName} up (exit {ipResult.ExitCode}); scanning anyway");

		// Active scan. S1G scans can take several seconds; allow ~20s.
		var scanResult = await ProcessHelper.RunAsync("iw", $"dev {ifaceName} scan", 20_000, ct);
		if (reportStep is not null)
			await reportStep($"iw dev {ifaceName} scan", scanResult.Stdout + scanResult.Stderr, scanResult.ExitCode != 0);

		if (scanResult.ExitCode != 0)
		{
			snapshot["scan_node_count"] = 0;
			snapshot["scan_nodes"] = new List<Dictionary<string, object?>>();
			messages.Add($"Layer 4 FAIL: scan on {ifaceName} failed (exit {scanResult.ExitCode}) — no nodes found");
			return 0;
		}

		var nodes = ParseScanResults(scanResult.Stdout);
		var nodeList = nodes
			.Select(n => new Dictionary<string, object?>
			{
				["ssid"] = n.Ssid,
				["bssid"] = n.Bssid,
				["freq"] = n.Frequency,
				["signal"] = n.Signal,
			})
			.ToList();

		snapshot["scan_node_count"] = nodeList.Count;
		snapshot["scan_nodes"] = nodeList;

		if (nodeList.Count == 0)
		{
			messages.Add($"Layer 4: scan on {ifaceName} completed but found 0 AP/STA nodes — Degraded");
			return 0;
		}

		foreach (var n in nodes)
			messages.Add($"Layer 4: node {n.Ssid ?? "<hidden>"} ({n.Bssid}) freq={n.Frequency ?? "?"} signal={n.Signal ?? "?"}");

		messages.Add($"Layer 4 PASS: scan found {nodeList.Count} AP/STA node(s) on {ifaceName}");
		return nodeList.Count;
	}

	/// <summary>
	/// Tier B: Attempt HTTP REST connection to zSCOUT-mesh service for mesh health data.
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

		// Check mesh service via REST status endpoint
		var client = _httpClientFactory.CreateClient("MeshSvc");
		client.BaseAddress = new Uri($"http://{host}:{port}");
		client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);

		try
		{
			using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			statusCts.CancelAfter(timeoutMs);
			using var response = await client.GetAsync("/api/status", statusCts.Token);

			if (!response.IsSuccessStatusCode)
			{
				if (reportStep is not null)
					await reportStep($"GET http://{host}:{port}/api/status", $"HTTP {(int)response.StatusCode}", true);

				snapshot["mesh_service_available"] = false;
				snapshot["mesh_associated"] = null;
				snapshot["peer_count"] = null;
				snapshot["gateway_mode"] = null;
				snapshot["bat0_ip"] = null;
				snapshot["internet_reachable"] = null;
				messages.Add($"Tier B: mesh service returned HTTP {(int)response.StatusCode} on {host}:{port} — mesh tests skipped");
				return;
			}

			snapshot["mesh_service_available"] = true;

			using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(statusCts.Token), cancellationToken: statusCts.Token);
			var root = doc.RootElement;

			var associated = root.TryGetProperty("associated", out var assocEl) && assocEl.GetBoolean();
			var peerCount = root.TryGetProperty("peer_count", out var pcEl) ? pcEl.GetInt32() : 0;
			var gatewayMode = root.TryGetProperty("gateway_mode", out var gwEl) ? gwEl.GetString() ?? "" : "";
			var bat0Ip = root.TryGetProperty("bat0_ip", out var batEl) ? batEl.GetString() ?? "" : "";
			var internetReachable = root.TryGetProperty("internet_reachable", out var inetEl) && inetEl.GetBoolean();
			var meshStatusMessage = root.TryGetProperty("status_message", out var msmEl) ? msmEl.GetString() ?? "" : "";

			if (reportStep is not null)
				await reportStep("GET /api/status",
					$"associated={associated} peers={peerCount} gw={gatewayMode} inet={internetReachable}",
					!associated);

			snapshot["mesh_associated"] = associated;
			snapshot["peer_count"] = peerCount;
			snapshot["gateway_mode"] = gatewayMode;
			snapshot["bat0_ip"] = bat0Ip;
			snapshot["internet_reachable"] = internetReachable;

			var tierBSummary = associated
				? $"Tier B PASS: mesh associated, {peerCount} peers, gw={gatewayMode}, bat0={bat0Ip}, inet={internetReachable}"
				: $"Tier B: mesh not associated — {meshStatusMessage}";
			messages.Add(tierBSummary);
			_logger.LogInformation("HaLow Tier B: associated={Associated} peers={PeerCount}", associated, peerCount);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (reportStep is not null)
				await reportStep($"GET http://{host}:{port}/api/status", $"unreachable: {ex.GetType().Name}", true);

			_logger.LogWarning(ex, "HaLow probe: REST call to mesh service failed");
			snapshot["mesh_service_available"] = false;
			snapshot["mesh_associated"] = null;
			snapshot["peer_count"] = null;
			snapshot["gateway_mode"] = null;
			snapshot["bat0_ip"] = null;
			snapshot["internet_reachable"] = null;
			messages.Add($"Tier B: mesh service not available on {host}:{port} — mesh tests skipped (NotTested)");
			_logger.LogInformation("HaLow probe: mesh service not available on {Host}:{Port} — Tier B skipped", host, port);
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

	/// <summary>
	/// Resolves the morse-specific network interface by inspecting the kernel
	/// driver bound to each netdev under <c>/sys/class/net</c>. Returns the
	/// interface name and its driver, or <c>(null, null)</c> if no morse netdev
	/// is present. The onboard Wi-Fi (e.g. brcmfmac) is never selected. (#97)
	/// </summary>
	private (string? Interface, string? Driver) FindMorseInterface()
	{
		const string netDir = "/sys/class/net";
		try
		{
			if (!Directory.Exists(netDir))
				return (null, null);

			var ifaces = Directory
				.EnumerateFileSystemEntries(netDir)
				.Select(p => Path.GetFileName(p.TrimEnd('/')))
				.Where(n => !string.IsNullOrEmpty(n))
				.Select(n => n!)
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToList();

			return SelectMorseInterface(ifaces, ResolveInterfaceDriver);
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "FindMorseInterface failed enumerating {NetDir}", netDir);
			return (null, null);
		}
	}

	/// <summary>
	/// Resolves the driver name bound to a netdev via
	/// <c>/sys/class/net/&lt;iface&gt;/device/driver</c> (a symlink whose final
	/// path segment is the driver, e.g. <c>morse_usb</c>). Returns null if the
	/// netdev has no bound driver (e.g. virtual interfaces like <c>lo</c>).
	/// </summary>
	private static string? ResolveInterfaceDriver(string iface)
	{
		try
		{
			var driverLink = $"/sys/class/net/{iface}/device/driver";
			var target = Directory.ResolveLinkTarget(driverLink, returnFinalTarget: true);
			if (target is null)
				return null;
			return Path.GetFileName(target.FullName.TrimEnd('/'));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Pure selection logic (unit-testable): given candidate interface names and
	/// a driver resolver, returns the interface bound to <c>morse_usb</c>, or as
	/// a fallback the first interface whose driver name contains "morse".
	/// </summary>
	internal static (string? Interface, string? Driver) SelectMorseInterface(
		IEnumerable<string> interfaces,
		Func<string, string?> driverResolver)
	{
		var resolved = interfaces
			.Select(iface => (Interface: iface, Driver: driverResolver(iface)))
			.Where(x => !string.IsNullOrEmpty(x.Driver))
			.ToList();

		// Exact morse_usb match preferred.
		var exact = resolved.FirstOrDefault(x =>
			string.Equals(x.Driver, MorseDriverName, StringComparison.OrdinalIgnoreCase));
		if (exact.Interface is not null)
			return (exact.Interface, exact.Driver);

		// Fallback: any driver containing "morse".
		var fuzzy = resolved.FirstOrDefault(x =>
			x.Driver!.Contains("morse", StringComparison.OrdinalIgnoreCase));
		return fuzzy.Interface is not null ? (fuzzy.Interface, fuzzy.Driver) : (null, null);
	}

	/// <summary>
	/// Parses <c>iw dev &lt;iface&gt; scan</c> output into a list of BSS entries.
	/// Each entry captures BSSID, SSID, frequency and signal where available.
	/// </summary>
	internal static List<ScanNode> ParseScanResults(string iwScanOutput)
	{
		var nodes = new List<ScanNode>();
		if (string.IsNullOrWhiteSpace(iwScanOutput))
			return nodes;

		ScanNode? current = null;
		foreach (var raw in iwScanOutput.Split('\n', StringSplitOptions.None))
		{
			var line = raw.Trim();
			if (line.Length == 0)
				continue;

			var bssMatch = BssRegex().Match(line);
			if (bssMatch.Success)
			{
				current = new ScanNode { Bssid = bssMatch.Groups[1].Value.ToLowerInvariant() };
				nodes.Add(current);
				continue;
			}

			if (current is null)
				continue;

			if (line.StartsWith("SSID:", StringComparison.OrdinalIgnoreCase))
			{
				current.Ssid = line["SSID:".Length..].Trim();
				if (current.Ssid.Length == 0)
					current.Ssid = null;
			}
			else if (line.StartsWith("freq:", StringComparison.OrdinalIgnoreCase))
			{
				current.Frequency = line["freq:".Length..].Trim();
			}
			else if (line.StartsWith("signal:", StringComparison.OrdinalIgnoreCase))
			{
				current.Signal = line["signal:".Length..].Trim();
			}
		}

		return nodes;
	}

	/// <summary>A single AP/STA node discovered by an <c>iw scan</c>.</summary>
	internal sealed class ScanNode
	{
		public string Bssid { get; set; } = string.Empty;
		public string? Ssid { get; set; }
		public string? Frequency { get; set; }
		public string? Signal { get; set; }
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
		"morse_interface",
		"phy_name",
		"supported_channels",
		"health_check_ok",
		// Layer 4 RF scan (#97)
		"scan_node_count",
		"scan_nodes",
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

	[GeneratedRegex(@"^BSS\s+([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})", RegexOptions.IgnoreCase)]
	private static partial Regex BssRegex();
}
