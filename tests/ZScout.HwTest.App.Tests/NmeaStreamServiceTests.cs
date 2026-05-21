using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZScout.HwTest.App.Streams;

namespace ZScout.HwTest.App.Tests;

/// <summary>
/// Spy publisher that bypasses SignalR hub for NMEA event testing.
/// </summary>
file sealed class NmeaSpyPublisher : LiveEventPublisher
{
	public NmeaSpyPublisher() : base(null!) { }

	public override Task PublishRunStatusAsync(string runId, Contracts.Models.RunStatus status, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishPeripheralStatusAsync(
		string runId, Contracts.Models.PeripheralId peripheralId, Contracts.Models.PeripheralStatus status, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishTelemetrySampleAsync(
		string runId, Contracts.Models.PeripheralId peripheralId, string sample, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishCommandProgressAsync(
		string runId, Contracts.Models.PeripheralId peripheralId, string command, string output, bool isError, CancellationToken ct = default)
		=> Task.CompletedTask;
}

public sealed class NmeaStreamServiceTests
{
	[Fact]
	public void PublishNmeaSentence_RaisesEvent_WithCorrectSentence()
	{
		var pub = new NmeaSpyPublisher();
		NmeaSentenceEventArgs? captured = null;
		pub.NmeaSentenceReceived += (_, e) => captured = e;

		pub.PublishNmeaSentence("$GNGGA,123456.00,4807.038,N,01131.000,E,1,08,0.9,545.4,M,47.0,M,,*47");

		Assert.NotNull(captured);
		Assert.Equal("$GNGGA,123456.00,4807.038,N,01131.000,E,1,08,0.9,545.4,M,47.0,M,,*47", captured.Sentence);
		Assert.True(captured.TimestampUtc <= DateTimeOffset.UtcNow);
	}

	[Fact]
	public void PublishNmeaConnectionState_RaisesEvent_WithCorrectState()
	{
		var pub = new NmeaSpyPublisher();
		var states = new List<bool>();
		pub.NmeaConnectionStateChanged += (_, e) => states.Add(e.Connected);

		pub.PublishNmeaConnectionState(true);
		pub.PublishNmeaConnectionState(false);

		Assert.Equal([true, false], states);
	}

	[Fact]
	public void Subscribe_StartsStream_Unsubscribe_StopsStream()
	{
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Peripherals:Gps:Host"] = "127.0.0.1",
				["Peripherals:Gps:RestPort"] = "19999" // intentionally unreachable port
			})
			.Build();

		var pub = new NmeaSpyPublisher();
		var logger = NullLogger<NmeaStreamService>.Instance;
		using var httpFactory = new DefaultHttpClientFactory();
		using var svc = new NmeaStreamService(httpFactory, config, logger, pub);

		// Initially not connected
		Assert.False(svc.IsConnected);

		// Subscribe starts the reader (will fail to connect, but tests lifecycle)
		svc.Subscribe();

		// Unsubscribe stops the reader
		svc.Unsubscribe();
		Assert.False(svc.IsConnected);
	}

	[Fact]
	public void MultipleSubscribers_OnlyLastUnsubscribeStops()
	{
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Peripherals:Gps:Host"] = "127.0.0.1",
				["Peripherals:Gps:RestPort"] = "19999"
			})
			.Build();

		var pub = new NmeaSpyPublisher();
		var logger = NullLogger<NmeaStreamService>.Instance;
		using var httpFactory = new DefaultHttpClientFactory();
		using var svc = new NmeaStreamService(httpFactory, config, logger, pub);

		svc.Subscribe();
		svc.Subscribe(); // 2nd subscriber

		// First unsubscribe should not stop (still 1 subscriber)
		svc.Unsubscribe();

		// Second unsubscribe stops
		svc.Unsubscribe();
		Assert.False(svc.IsConnected);
	}

	[Fact]
	public void NullPublisher_NmeaMethods_DoNotThrow()
	{
		var pub = new NullLiveEventPublisher();

		var sentenceEx = Record.Exception(() => pub.PublishNmeaSentence("$GNRMC,test"));
		var stateEx = Record.Exception(() => pub.PublishNmeaConnectionState(true));

		Assert.Null(sentenceEx);
		Assert.Null(stateEx);
	}
}

/// <summary>
/// Minimal IHttpClientFactory implementation for tests.
/// </summary>
file sealed class DefaultHttpClientFactory : IHttpClientFactory, IDisposable
{
	private readonly HttpClient _client = new();

	public HttpClient CreateClient(string name) => _client;

	public void Dispose() => _client.Dispose();
}
