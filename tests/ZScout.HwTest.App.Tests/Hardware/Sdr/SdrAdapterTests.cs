using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZScout.Common.Sdr;
using ZScout.HwTest.App.Hardware.Sdr;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Tests.Hardware.Sdr;

/// <summary>
/// Unit tests for <see cref="SdrAdapter"/>.
/// All tests use a fake <see cref="HttpMessageHandler"/> to simulate sdr-svc
/// responses without requiring a live service.
///
/// NOTE: ConfigureRxAsync and AcquireSamplesAsync tests exercise the adapter's
/// HTTP parsing logic against expected sdr-svc response shapes.  The actual
/// endpoints (POST /api/rx/configure, GET /api/rx/samples) are not yet in
/// sdr-svc — they will be added by sdr-svc#26/#27.
/// </summary>
public sealed class SdrAdapterTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SdrAdapter CreateAdapter(
        string? statusJson = null,
        string? capsJson = null,
        string? configureJson = null,
        string? samplesJson = null,
        IConfiguration? config = null)
    {
        var handler = new RoutingFakeHandler(new Dictionary<string, (HttpMethod, string)>
        {
            ["/api/status"]       = (HttpMethod.Get,  statusJson    ?? DefaultStatusJson(deviceFound: true)),
            ["/api/capabilities"] = (HttpMethod.Get,  capsJson      ?? DefaultCapsJson()),
            ["/api/rx/configure"] = (HttpMethod.Post, configureJson ?? DefaultConfigureJson()),
            ["/api/rx/samples"]   = (HttpMethod.Get,  samplesJson   ?? DefaultSamplesJson(4096)),
            // capture route for probe validator evidence + new tests (reuses samples shape for synthetic)
            ["/api/rx/capture"]   = (HttpMethod.Get,  samplesJson   ?? DefaultSamplesJson(2048)),
        });

        var factory = new FakeHttpClientFactory(handler);
        // Construct SdrClient with the fake http (post common integration)
        var httpForClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5101") };
        var sdrClient = new SdrClient(httpForClient, NullLogger<SdrClient>.Instance);
        return new SdrAdapter(NullLogger<SdrAdapter>.Instance, config ?? EmptyConfig(), factory, sdrClient);
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    // ── Default JSON payloads ──────────────────────────────────────────────────

    private static string DefaultStatusJson(bool deviceFound = true, bool probeOk = true) =>
        $$"""{"status":"ok","deviceFound":{{deviceFound.ToString().ToLower()}},"driverInfo":"uSDR v1","probeOk":{{probeOk.ToString().ToLower()}},"timestamp":"2024-01-01T00:00:00Z"}""";

    private static string DefaultCapsJson() =>
        """{"rxChannels":1,"txChannels":0,"rxFreqRangeHz":{"min":300000000,"max":3800000000},"txFreqRangeHz":{"min":0,"max":0},"sampleRates":[1000000,2000000],"rxGains":{"RFVGA":{"min":0,"max":30},"IFVGA":{"min":0,"max":59}},"timestamp":"2024-01-01T00:00:00Z"}""";

    private static string DefaultConfigureJson(long centerFreqHz = 800_000_000L, long sampleRateHz = 1_000_000L) =>
        $$"""{"ok":true,"centerFreqHz":{{centerFreqHz}},"sampleRateHz":{{sampleRateHz}}}""";

    private static string DefaultSamplesJson(int numSamples)
    {
        // Produce interleaved I+Q floats all in [-0.1, 0.1] range (quiescent noise)
        var rng = new Random(42);
        var floats = Enumerable.Range(0, numSamples * 2)
            .Select(_ => (float)(rng.NextDouble() * 0.2 - 0.1))
            .ToArray();
        var dataJson = "[" + string.Join(",", floats.Select(f => f.ToString("G7"))) + "]";
        // Include timestamp to satisfy required field in ZScout.Common.Sdr.IqSamples
        return $$"""{"centerFreqHz":800000000,"sampleRateHz":1000000,"data":{{dataJson}},"timestamp":"2024-01-01T00:00:00Z"}""";
    }

    // ── ProbeAsync — Container mode (full flow) ───────────────────────────────

    [Fact]
    public async Task ProbeAsync_ContainerMode_ServiceReachableDeviceFound_ReturnsReady()
    {
        var adapter = CreateAdapter();

        var result = await adapter.ProbeAsync(RunMode.Container);

        Assert.Equal(PeripheralStatus.Ready, result.Status);
        Assert.Equal(PeripheralId.Sdr, result.PeripheralId);
        Assert.True(result.DependencyAvailable);
    }

    [Fact]
    public async Task ProbeAsync_ContainerMode_SnapshotContainsRequiredKeys()
    {
        var adapter = CreateAdapter();

        var result = await adapter.ProbeAsync(RunMode.Container);

        var keys = result.Snapshot.Values.Keys;
        Assert.Contains("service_available", keys);
        Assert.Contains("device_found", keys);
        Assert.Contains("driver_info", keys);
        Assert.Contains("probe_ok", keys);
        Assert.Contains("min_frequency_hz", keys);
        Assert.Contains("max_frequency_hz", keys);
        Assert.Contains("rx_configured", keys);
        Assert.Contains("iq_sample_count", keys);
    }

    [Fact]
    public async Task ProbeAsync_ContainerMode_DeviceNotFound_ReturnsUnavailable()
    {
        var adapter = CreateAdapter(statusJson: DefaultStatusJson(deviceFound: false));

        var result = await adapter.ProbeAsync(RunMode.Container);

        Assert.Equal(PeripheralStatus.Unavailable, result.Status);
        Assert.Equal(false, result.Snapshot.Values["device_found"]);
    }

    [Fact]
    public async Task ProbeAsync_ContainerMode_ServiceUnreachable_ReturnsDegraded()
    {
        // Use a real new HttpClient with no base — will fail to connect
        var factory = new StubHttpClientFactory();
        // SdrClient with unreachable http
        var http = factory.CreateClient("SdrSvc");
        var sdrClient = new SdrClient(http, NullLogger<SdrClient>.Instance);
        var adapter = new SdrAdapter(NullLogger<SdrAdapter>.Instance, EmptyConfig(), factory, sdrClient);

        var result = await adapter.ProbeAsync(RunMode.Container);

        Assert.Equal(PeripheralStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_ContainerMode_WithReportStep_CallsStatusAndCaps()
    {
        var adapter = CreateAdapter();
        var calls = new List<string>();

        Task Report(string cmd, string output, bool isError)
        {
            calls.Add(cmd);
            return Task.CompletedTask;
        }

        await adapter.ProbeAsync(RunMode.Container, Report);

        Assert.Contains(calls, c => c.Contains("/api/status"));
        Assert.Contains(calls, c => c.Contains("/api/capabilities"));
    }

    [Fact]
    public async Task ProbeAsync_ContainerMode_CallsConfigureAndSamples()
    {
        var adapter = CreateAdapter();
        var calls = new List<string>();

        Task Report(string cmd, string output, bool isError)
        {
            calls.Add(cmd);
            return Task.CompletedTask;
        }

        await adapter.ProbeAsync(RunMode.Container, Report);

        Assert.Contains(calls, c => c.Contains("/api/rx/configure"));
        Assert.Contains(calls, c => c.Contains("/api/rx/samples"));
    }

    // ── ProbeAsync — Host mode (full: status + caps + configure + acquire) ─────

    [Fact]
    public async Task ProbeAsync_HostMode_AllEndpointsOk_ReturnsReady()
    {
        var adapter = CreateAdapter();

        var result = await adapter.ProbeAsync(RunMode.Host);

        Assert.Equal(PeripheralStatus.Ready, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_HostMode_SetsRxConfiguredTrue()
    {
        var adapter = CreateAdapter();

        var result = await adapter.ProbeAsync(RunMode.Host);

        Assert.Equal(true, result.Snapshot.Values["rx_configured"]);
        Assert.Equal(800_000_000L, result.Snapshot.Values["last_center_freq_hz"]);
    }

    [Fact]
    public async Task ProbeAsync_HostMode_SetsIqSampleCount()
    {
        var adapter = CreateAdapter();

        var result = await adapter.ProbeAsync(RunMode.Host);

        // Data array has numSamples*2 floats (I+Q interleaved)
        Assert.Equal(4096 * 2, result.Snapshot.Values["iq_sample_count"]);
    }

    [Fact]
    public async Task ProbeAsync_HostMode_WithReportStep_CallsConfigureAndSamples()
    {
        var adapter = CreateAdapter();
        var calls = new List<string>();

        Task Report(string cmd, string output, bool isError)
        {
            calls.Add(cmd);
            return Task.CompletedTask;
        }

        await adapter.ProbeAsync(RunMode.Host, Report);

        Assert.Contains(calls, c => c.Contains("/api/rx/configure"));
        Assert.Contains(calls, c => c.Contains("/api/rx/samples"));
    }

    [Fact]
    public async Task ProbeAsync_HostMode_WithReportStep_EmitsAutoDiscoverDetails()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Peripherals:Sdr:AutoDiscoverMaxCandidates"] = "2",
                ["Peripherals:Sdr:AutoDiscoverNumSamples"] = "4096",
                ["Peripherals:Sdr:AutoDiscoverRepeatsPerCandidate"] = "1",
            })
            .Build();

        var adapter = CreateAdapter(config: cfg);
        var calls = new List<string>();

        Task Report(string cmd, string output, bool isError)
        {
            calls.Add(cmd);
            return Task.CompletedTask;
        }

        await adapter.ProbeAsync(RunMode.Host, Report);

        Assert.Contains(calls, c => c.Contains("AUTO detail (validator)"));
        Assert.Contains(calls, c => c.Contains("AUTO /api/rx/capture (validator)"));
    }

    [Fact]
    public async Task ProbeAsync_HostMode_ConfigureReturns404_GraceFullyDegrades()
    {
        var handler = new RoutingFakeHandler(new Dictionary<string, (HttpMethod, string)>
        {
            ["/api/status"]       = (HttpMethod.Get, DefaultStatusJson()),
            ["/api/capabilities"] = (HttpMethod.Get, DefaultCapsJson()),
        }, defaultStatus: HttpStatusCode.NotFound);

        var factory = new FakeHttpClientFactory(handler);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5101") };
        var sdrClient = new SdrClient(http, NullLogger<SdrClient>.Instance);
        var adapter = new SdrAdapter(NullLogger<SdrAdapter>.Instance, EmptyConfig(), factory, sdrClient);

        var result = await adapter.ProbeAsync(RunMode.Host);

        // Should still be Ready (caps+status passed) with rx_configure_unavailable flag
        Assert.Equal(PeripheralStatus.Ready, result.Status);
        Assert.Contains(result.Messages, m => m.Contains("not yet available") || m.Contains("sdr-svc") || m.Contains("skipped"));
    }

    // ── ConfigureRxAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigureRxAsync_ValidResponse_ReturnsCenterFreqHz()
    {
        var adapter = CreateAdapter(configureJson: DefaultConfigureJson(900_000_000L, 2_000_000L));
        // Pass null client: uses the SdrClient wired at CreateAdapter (which has the provided configureJson route)
        var freqHz = await adapter.ConfigureRxAsync(new RxConfigRequest { CenterFreqHz = 900_000_000L, SampleRateHz = 2_000_000L }, null, 5_000, CancellationToken.None);

        Assert.Equal(900_000_000L, freqHz);
    }

    [Fact]
    public async Task ConfigureRxAsync_MissingCenterFreqInResponse_FallsBackToRequestValue()
    {
        var adapter = CreateAdapter();
        var freqHz = await adapter.ConfigureRxAsync(new RxConfigRequest { CenterFreqHz = 700_000_000L, SampleRateHz = 1_000_000L }, null, 5_000, CancellationToken.None);

        Assert.Equal(700_000_000L, freqHz);
    }

    // ── AcquireSamplesAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AcquireSamplesAsync_ValidResponse_ReturnsParsedBlock()
    {
        var adapter = CreateAdapter(samplesJson: DefaultSamplesJson(512));
        // Pass null: delegate uses SdrClient from CreateAdapter (handler has the samplesJson)
        var block = await adapter.AcquireSamplesAsync(512, null, 5_000, CancellationToken.None);

        Assert.Equal(800_000_000L, block.CenterFreqHz);
        Assert.Equal(1_000_000L, block.SampleRateHz);
        Assert.Equal(512 * 2, block.Data.Length);
    }

    [Fact]
    public async Task AcquireSamplesAsync_EmptyDataArray_ReturnsEmptyBlock()
    {
        var adapter = CreateAdapter();
        // Provide override client+handler with empty data json (incl ts for IqSamples deserial) so Acquire uses raw path returning empty
        var emptyJson = """{"centerFreqHz":800000000,"sampleRateHz":1000000,"data":[],"timestamp":"2024-01-01T00:00:00Z"}""";
        var h = new RoutingFakeHandler(new Dictionary<string, (HttpMethod, string)> { ["/api/rx/samples"] = (HttpMethod.Get, emptyJson) });
        var client = new HttpClient(h) { BaseAddress = new Uri("http://localhost:5101") };
        var block = await adapter.AcquireSamplesAsync(0, client, 5_000, CancellationToken.None);

        Assert.Empty(block.Data);
    }

    // ── ValidateIqBlock ────────────────────────────────────────────────────────

    [Fact]
    public void ValidateIqBlock_WithKnownValues_ReturnsCorrectStats()
    {
        var block = new IqSamples { CenterFreqHz = 800_000_000L, SampleRateHz = 1_000_000L, Data = [0.5f, -0.3f, 0.1f, -0.8f], Timestamp = DateTimeOffset.UtcNow };

        var (count, min, max, meanAbs) = SdrAdapter.ValidateIqBlock(block);

        Assert.Equal(4, count);
        Assert.Equal(-0.8f, min, 5);
        Assert.Equal(0.5f, max, 5);
        Assert.InRange(meanAbs, 0.42f, 0.43f); // (0.5+0.3+0.1+0.8)/4 = 0.425
    }

    [Fact]
    public void ValidateIqBlock_EmptyBlock_ReturnsZeros()
    {
        var block = new IqSamples { CenterFreqHz = 0, SampleRateHz = 0, Data = [], Timestamp = DateTimeOffset.UtcNow };

        var (count, min, max, meanAbs) = SdrAdapter.ValidateIqBlock(block);

        Assert.Equal(0, count);
        Assert.Equal(0f, min);
        Assert.Equal(0f, max);
        Assert.Equal(0f, meanAbs);
    }

    [Fact]
    public void ValidateIqBlock_NormalizedNoiseFloor_PassesRangeCheck()
    {
        var rng = new Random(99);
        var data = Enumerable.Range(0, 8192).Select(_ => (float)(rng.NextDouble() * 0.2 - 0.1)).ToArray();
        var block = new IqSamples { CenterFreqHz = 800_000_000L, SampleRateHz = 1_000_000L, Data = data, Timestamp = DateTimeOffset.UtcNow };

        var (count, min, max, meanAbs) = SdrAdapter.ValidateIqBlock(block);

        Assert.True(min >= -1.5f, $"min {min} out of CF32 range");
        Assert.True(max <= 1.5f, $"max {max} out of CF32 range");
        Assert.Equal(8192, count);
    }

    // ── ReadRawSampleAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReadRawSampleAsync_ServiceReachable_ReturnsStatusString()
    {
        var adapter = CreateAdapter(statusJson: DefaultStatusJson(deviceFound: true));

        // ReadRawSampleAsync creates its own client via the factory; use the fake factory
        var result = await adapter.ReadRawSampleAsync();

        Assert.NotNull(result);
        Assert.Contains("device_found=True", result);
        Assert.Contains("driver_info=uSDR v1", result);
    }

    [Fact]
    public async Task ReadRawSampleAsync_ServiceUnreachable_ReturnsNull()
    {
        var factory = new StubHttpClientFactory();
        var http = factory.CreateClient("SdrSvc");
        var sdrClient = new SdrClient(http, NullLogger<SdrClient>.Instance);
        var adapter = new SdrAdapter(NullLogger<SdrAdapter>.Instance, EmptyConfig(), factory, sdrClient);

        var result = await adapter.ReadRawSampleAsync();

        Assert.Null(result);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static HttpClient CreateClientWithHandler(SdrAdapter _, RoutingFakeHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5101")
        };
        return client;
    }

    // ── New tests for #74: SdrCaptureValidator burst/stats/hex + synthetic (pure logic, no net) ──

    [Fact]
    public void DetectBursts_SyntheticBursts_FindsTransmissionsAndComputesRssiSnr()
    {
        // Build synthetic IQ: noise + one burst of higher amplitude
        var data = new float[256]; // 128 complex
        var rng = new Random(123);
        for (int i = 0; i < data.Length; i++) data[i] = (float)(rng.NextDouble() * 0.1 - 0.05); // ~noise

        // Insert a "transmission" burst around sample 40-80 (float indices 80-160)
        for (int i = 80; i < 160; i += 2)
        {
            data[i] = 0.6f;     // I
            data[i + 1] = 0.3f; // Q  => power ~0.45
        }

        var bursts = SdrCaptureValidator.DetectBursts(data, threshDb: -20, minLenSamples: 4);

        Assert.NotEmpty(bursts);
        var b = bursts[0];
        Assert.True(b.Rssi > -10, $"expected positive-ish RSSI, got {b.Rssi}");
        Assert.True(b.Snr > 5, $"expected decent SNR, got {b.Snr}");
        Assert.True(b.LengthSamples >= 8);
    }

    [Fact]
    public void ComputeHexPrefix_ProducesLowerHexOfCorrectLength()
    {
        var data = new float[] { 0.0f, 1.0f, -0.5f, 0.25f };
        var hex = SdrCaptureValidator.ComputeHexPrefix(data, maxBytes: 16);
        Assert.NotEmpty(hex);
        Assert.True(hex.Length <= 16 * 2);
        Assert.True(hex.All(c => "0123456789abcdef".Contains(c)));
    }

    [Fact]
    public void Validator_DetectAndStats_AggregateCorrectlyOnSynthetic()
    {
        // Use the helpers directly (they are the core of validator)
        var data = new float[128];
        // two short bursts
        for (int i = 20; i < 40; i += 2) { data[i] = 0.8f; data[i+1] = 0.1f; }
        for (int i = 70; i < 90; i += 2) { data[i] = 0.4f; data[i+1] = 0.2f; }

        var bursts = SdrCaptureValidator.DetectBursts(data, -18, 3);
        Assert.Equal(2, bursts.Count);

        var rssiList = bursts.Select(b => b.Rssi).ToList();
        var snrList = bursts.Select(b => b.Snr).ToList();
        Assert.Equal(2, rssiList.Count);
        Assert.True(rssiList.Max() >= rssiList.Min());
        // HEX non empty
        var h = SdrCaptureValidator.ComputeHexPrefix(data, 8);
        Assert.NotEmpty(h);
    }
}

// ── Fake HTTP infrastructure ───────────────────────────────────────────────────

/// <summary>
/// Routes HTTP requests to pre-configured JSON responses by path prefix.
/// Unregistered paths return 404 by default (configurable).
/// </summary>
internal sealed class RoutingFakeHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpMethod Method, string Body)> _routes;
    private readonly HttpStatusCode _defaultStatus;

    public RoutingFakeHandler(
        Dictionary<string, (HttpMethod Method, string Body)> routes,
        HttpStatusCode defaultStatus = HttpStatusCode.NotFound)
    {
        _routes = routes;
        _defaultStatus = defaultStatus;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";

        // Match by path prefix so query-strings (/api/rx/samples?numSamples=...) still hit the route
        foreach (var (routePath, (_, body)) in _routes)
        {
            if (path.StartsWith(routePath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
        }

        return Task.FromResult(new HttpResponseMessage(_defaultStatus));
    }
}

/// <summary>
/// <see cref="IHttpClientFactory"/> that always returns a client backed by the given handler,
/// with a localhost base address matching the default sdr-svc port.
/// </summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly RoutingFakeHandler _handler;

    public FakeHttpClientFactory(RoutingFakeHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) =>
        new(_handler) { BaseAddress = new Uri("http://localhost:5101") };
}
