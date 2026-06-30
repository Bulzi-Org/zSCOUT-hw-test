using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZScout.HwTest.App.Hardware.Halow;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Tests.Hardware.Halow;

/// <summary>
/// Tests for the HalowAdapter two-tier diagnostic pipeline.
/// These tests run without MM8108 hardware — they verify the adapter
/// handles absent hardware gracefully and returns correct status/envelope shapes.
/// </summary>
public sealed class HalowAdapterTests
{
	private static HalowAdapter CreateAdapter()
	{
		var config = new ConfigurationBuilder().Build();
		return new HalowAdapter(NullLogger<HalowAdapter>.Instance, config, new StubHttpClientFactory());
	}

	// ── ProbeAsync tests ────────────────────────────────────────────────

	/// <summary>
	/// Without the MM8108 USB device, Layer 0 should fail and ProbeAsync
	/// should return Unavailable immediately without attempting Layers 1-3.
	/// </summary>
	[Fact]
	public async Task ProbeAsync_WhenUsbDeviceAbsent_ReturnsUnavailable()
	{
		var adapter = CreateAdapter();

		var result = await adapter.ProbeAsync(RunMode.Container);

		Assert.Equal(PeripheralStatus.Unavailable, result.Status);
		Assert.False(result.DependencyAvailable);
		Assert.Equal(PeripheralId.Halow, result.PeripheralId);
		Assert.Contains(result.Messages, m => m.Contains("MM8108 USB device not detected"));
	}

	/// <summary>
	/// The snapshot must contain all required keys (Tier A + Tier B) even when layers fail early.
	/// </summary>
	[Fact]
	public async Task ProbeAsync_SnapshotContainsAllRequiredKeys()
	{
		var adapter = CreateAdapter();

		var result = await adapter.ProbeAsync(RunMode.Container);

		var keys = result.Snapshot.Values.Keys;
		// Tier A keys
		Assert.Contains("usb_device_found", keys);
		Assert.Contains("vendor_id", keys);
		Assert.Contains("module_loaded", keys);
		Assert.Contains("firmware_loaded", keys);
		Assert.Contains("firmware_version", keys);
		Assert.Contains("interface_name", keys);
		Assert.Contains("phy_name", keys);
		Assert.Contains("supported_channels", keys);
		Assert.Contains("health_check_ok", keys);
		// Tier B keys
		Assert.Contains("mesh_service_available", keys);
		Assert.Contains("mesh_associated", keys);
		Assert.Contains("peer_count", keys);
		Assert.Contains("gateway_mode", keys);
		Assert.Contains("bat0_ip", keys);
		Assert.Contains("internet_reachable", keys);
	}

	/// <summary>
	/// When USB is absent, usb_device_found must be false and vendor_id null.
	/// </summary>
	[Fact]
	public async Task ProbeAsync_WhenUsbAbsent_SnapshotHasCorrectUsbValues()
	{
		var adapter = CreateAdapter();

		var result = await adapter.ProbeAsync(RunMode.Container);

		Assert.Equal(false, result.Snapshot.Values["usb_device_found"]);
		Assert.Null(result.Snapshot.Values["vendor_id"]);
	}

	/// <summary>
	/// ReportStep must be called at least once (for the USB check command)
	/// even when the probe fails at Layer 0.
	/// </summary>
	[Fact]
	public async Task ProbeAsync_WithReportStep_CallsReportStepForLayer0()
	{
		var adapter = CreateAdapter();
		var calls = new List<(string Cmd, bool IsError)>();

		Task ReportStep(string cmd, string output, bool isError)
		{
			calls.Add((cmd, isError));
			return Task.CompletedTask;
		}

		await adapter.ProbeAsync(RunMode.Container, ReportStep);

		Assert.NotEmpty(calls);
		// The first call should be for the USB sysfs grep
		Assert.Contains(calls, c => c.Cmd.Contains("idVendor"));
	}

	/// <summary>
	/// ProbeAsync must not throw when reportStep is null (backward compat).
	/// </summary>
	[Fact]
	public async Task ProbeAsync_WithNullReportStep_DoesNotThrow()
	{
		var adapter = CreateAdapter();

		var result = await adapter.ProbeAsync(RunMode.Host, reportStep: null);

		Assert.NotNull(result);
		Assert.Equal(PeripheralId.Halow, result.PeripheralId);
	}

	/// <summary>
	/// ProbeAsync must work with both RunMode.Container and RunMode.Host.
	/// </summary>
	[Fact]
	public async Task ProbeAsync_HostMode_ReturnsValidEnvelope()
	{
		var adapter = CreateAdapter();

		var result = await adapter.ProbeAsync(RunMode.Host);

		Assert.NotNull(result);
		Assert.NotNull(result.Snapshot);
		Assert.NotEmpty(result.Messages);
	}

	// ── ReadRawSampleAsync tests ────────────────────────────────────────

	/// <summary>
	/// Without a cached interface (no prior probe), ReadRawSampleAsync
	/// should attempt discovery and return null if no interface found.
	/// </summary>
	[Fact]
	public async Task ReadRawSampleAsync_WhenNoInterfaceCached_ReturnsNull()
	{
		var adapter = CreateAdapter();

		var result = await adapter.ReadRawSampleAsync();

		// Without real hardware, iw won't find any interface
		Assert.Null(result);
	}

	// ── Parser unit tests ───────────────────────────────────────────────

	[Fact]
	public void ParseInterfaceName_WithValidIwDevOutput_ReturnsInterfaceName()
	{
		const string iwDevOutput = """
			phy#0
				Interface wlan0
					ifindex 3
					wdev 0x1
					addr 00:11:22:33:44:55
					type managed
			""";

		var result = HalowAdapter.ParseInterfaceName(iwDevOutput);
		Assert.Equal("wlan0", result);
	}

	[Fact]
	public void ParseInterfaceName_WithEmptyOutput_ReturnsNull()
	{
		Assert.Null(HalowAdapter.ParseInterfaceName(""));
	}

	[Fact]
	public void ParseInterfaceName_WithNoInterfaceLine_ReturnsNull()
	{
		const string output = "phy#0\n\tifindex 3\n";
		Assert.Null(HalowAdapter.ParseInterfaceName(output));
	}

	[Fact]
	public void ParsePhyName_WithValidIwPhyOutput_ReturnsPhyName()
	{
		const string iwPhyOutput = """
			Wiphy phy0
				max # scan SSIDs: 4
				max scan IEs length: 2257 bytes
			""";

		var result = HalowAdapter.ParsePhyName(iwPhyOutput);
		Assert.Equal("phy0", result);
	}

	[Fact]
	public void ParsePhyName_WithEmptyOutput_ReturnsNull()
	{
		Assert.Null(HalowAdapter.ParsePhyName(""));
	}

	[Fact]
	public void ParseSupportedChannels_WithFrequencies_ReturnsCommaSeparatedList()
	{
		const string iwPhyOutput = """
			Frequencies:
				* 902 MHz [1] (30.0 dBm)
				* 904 MHz [2] (30.0 dBm)
				* 906 MHz [3] (30.0 dBm)
			""";

		var result = HalowAdapter.ParseSupportedChannels(iwPhyOutput);
		Assert.Equal("902, 904, 906", result);
	}

	[Fact]
	public void ParseSupportedChannels_WithNoFrequencies_ReturnsNull()
	{
		Assert.Null(HalowAdapter.ParseSupportedChannels("some other output"));
	}

	[Fact]
	public void ParseSupportedChannels_WithEmptyOutput_ReturnsNull()
	{
		Assert.Null(HalowAdapter.ParseSupportedChannels(""));
	}

	// ── Morse interface selection (#97) ─────────────────────────────────

	[Fact]
	public void SelectMorseInterface_PrefersMorseUsbDriver()
	{
		var drivers = new Dictionary<string, string?>
		{
			["lo"] = null,
			["wlan0"] = "brcmfmac",
			["wlan1"] = "morse_usb",
		};

		var (iface, driver) = HalowAdapter.SelectMorseInterface(
			drivers.Keys, name => drivers[name]);

		Assert.Equal("wlan1", iface);
		Assert.Equal("morse_usb", driver);
	}

	[Fact]
	public void SelectMorseInterface_IgnoresOnboardWifi()
	{
		var drivers = new Dictionary<string, string?>
		{
			["wlan0"] = "brcmfmac",
		};

		var (iface, driver) = HalowAdapter.SelectMorseInterface(
			drivers.Keys, name => drivers[name]);

		Assert.Null(iface);
		Assert.Null(driver);
	}

	[Fact]
	public void SelectMorseInterface_FallsBackToDriverContainingMorse()
	{
		var drivers = new Dictionary<string, string?>
		{
			["wlan0"] = "brcmfmac",
			["wlan2"] = "morse",
		};

		var (iface, driver) = HalowAdapter.SelectMorseInterface(
			drivers.Keys, name => drivers[name]);

		Assert.Equal("wlan2", iface);
		Assert.Equal("morse", driver);
	}

	// ── iw scan parsing (#97) ───────────────────────────────────────────

	[Fact]
	public void ParseScanResults_WithSingleBss_ReturnsNode()
	{
		const string scanOutput = """
			BSS 0c:bf:74:11:22:33(on wlan1)
				freq: 9025
				signal: -31.00 dBm
				SSID: GL-MT3000-970
			""";

		var nodes = HalowAdapter.ParseScanResults(scanOutput);

		var node = Assert.Single(nodes);
		Assert.Equal("0c:bf:74:11:22:33", node.Bssid);
		Assert.Equal("GL-MT3000-970", node.Ssid);
		Assert.Equal("9025", node.Frequency);
		Assert.Equal("-31.00 dBm", node.Signal);
	}

	[Fact]
	public void ParseScanResults_WithMultipleBss_ReturnsAll()
	{
		const string scanOutput = """
			BSS aa:bb:cc:dd:ee:01(on wlan1)
				freq: 9025
				signal: -40.00 dBm
				SSID: NodeOne
			BSS aa:bb:cc:dd:ee:02(on wlan1)
				freq: 9035
				signal: -55.00 dBm
				SSID: NodeTwo
			""";

		var nodes = HalowAdapter.ParseScanResults(scanOutput);

		Assert.Equal(2, nodes.Count);
		Assert.Equal("NodeOne", nodes[0].Ssid);
		Assert.Equal("NodeTwo", nodes[1].Ssid);
	}

	[Fact]
	public void ParseScanResults_WithEmptyOutput_ReturnsEmpty()
	{
		Assert.Empty(HalowAdapter.ParseScanResults(""));
	}

	[Fact]
	public void ParseMeshStatusFields_ReadsSnakeCaseAndCamelCaseAliases()
	{
		const string json = """
			{
			  "associated": true,
			  "peer_count": 2,
			  "gateway_mode": "client",
			  "bat0_ip": "10.41.0.2",
			  "internet_reachable": true,
			  "status_message": "mesh active"
			}
			""";

		using var doc = JsonDocument.Parse(json);
		var fields = HalowAdapter.ParseMeshStatusFields(doc.RootElement);

		Assert.True(fields.Associated);
		Assert.Equal(2, fields.PeerCount);
		Assert.Equal("client", fields.GatewayMode);
		Assert.Equal("10.41.0.2", fields.Bat0Ip);
		Assert.True(fields.InternetReachable);
	}

	[Fact]
	public void ParseMeshStatusFields_FallsBackToCamelCaseKeys()
	{
		const string json = """
			{
			  "peerCount": 1,
			  "gatewayMode": "client",
			  "bat0Ip": "10.41.0.3",
			  "internetReachable": false,
			  "bat0Up": true
			}
			""";

		using var doc = JsonDocument.Parse(json);
		var fields = HalowAdapter.ParseMeshStatusFields(doc.RootElement);

		Assert.True(fields.Associated);
		Assert.Equal(1, fields.PeerCount);
		Assert.False(fields.InternetReachable);
	}

	[Fact]
	public void ParseScanResults_WithHiddenSsid_LeavesSsidNull()
	{
		const string scanOutput = """
			BSS aa:bb:cc:dd:ee:03(on wlan1)
				freq: 9025
				signal: -60.00 dBm
				SSID: 
			""";

		var node = Assert.Single(HalowAdapter.ParseScanResults(scanOutput));
		Assert.Null(node.Ssid);
		Assert.Equal("aa:bb:cc:dd:ee:03", node.Bssid);
	}

	[Fact]
	public void ParseScanResults_WithMeshId_UsesMeshIdAsSsid()
	{
		const string scanOutput = """
			BSS 0c:bf:74:00:22:20(on wlan1)
				freq: 5560.0
				signal: -20.00 dBm
				MESH ID: zSCOUT-Mesh
			""";

		var node = Assert.Single(HalowAdapter.ParseScanResults(scanOutput));
		Assert.Equal("zSCOUT-Mesh", node.Ssid);
	}

	[Fact]
	public void ParseStationDumpPeerCount_CountsMeshPeers()
	{
		const string dump = """
			Station 0c:bf:74:00:22:20 (on wlan1)
				inactive time:	0 ms
				rx bytes:	1234
			Station 0c:bf:74:00:33:44 (on wlan1)
				inactive time:	10 ms
			""";

		Assert.Equal(2, HalowAdapter.ParseStationDumpPeerCount(dump));
	}

	[Fact]
	public void ParseStationDumpPeerCount_WithEmptyOutput_ReturnsZero()
	{
		Assert.Equal(0, HalowAdapter.ParseStationDumpPeerCount(""));
	}
}
