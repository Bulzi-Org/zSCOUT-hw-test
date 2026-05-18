using Xunit;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Tests;

/// <summary>
/// Spy publisher that bypasses SignalR hub (null) so .NET event behaviour
/// can be tested without a running ASP.NET host.
/// </summary>
file sealed class SpyPublisher : LiveEventPublisher
{
	public SpyPublisher() : base(null!) { }

	public override Task PublishRunStatusAsync(string runId, RunStatus status, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishPeripheralStatusAsync(
		string runId, PeripheralId peripheralId, PeripheralStatus status, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishTelemetrySampleAsync(
		string runId, PeripheralId peripheralId, string sample, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishCommandProgressAsync(
		string runId, PeripheralId peripheralId, string command, string output, bool isError,
		CancellationToken ct = default)
	{
		// Fire the .NET event via the protected helper (can't invoke event from outside declaring class)
		RaiseCommandProgressReceived(
			new CommandProgressEventArgs(runId, peripheralId, command, output, isError, DateTimeOffset.UtcNow));
		return Task.CompletedTask;
	}
}

public sealed class LiveEventPublisherTests
{
	[Fact]
	public async Task PublishCommandProgressAsync_RaisesCommandProgressReceived_WithCorrectArgs()
	{
		var pub = new SpyPublisher();
		CommandProgressEventArgs? captured = null;
		pub.CommandProgressReceived += (_, e) => captured = e;

		await pub.PublishCommandProgressAsync(
			"run-abc", PeripheralId.Gps, "pgrep -x gpsd", "1234\n", false);

		Assert.NotNull(captured);
		Assert.Equal("run-abc", captured.RunId);
		Assert.Equal(PeripheralId.Gps, captured.PeripheralId);
		Assert.Equal("pgrep -x gpsd", captured.Command);
		Assert.Equal("1234\n", captured.Output);
		Assert.False(captured.IsError);
	}

	[Fact]
	public async Task PublishCommandProgressAsync_IsError_True_WhenCommandFails()
	{
		var pub = new SpyPublisher();
		CommandProgressEventArgs? captured = null;
		pub.CommandProgressReceived += (_, e) => captured = e;

		await pub.PublishCommandProgressAsync(
			"run-xyz", PeripheralId.Compass, "i2cdetect -y 1", "", true);

		Assert.NotNull(captured);
		Assert.True(captured.IsError);
		Assert.Equal(PeripheralId.Compass, captured.PeripheralId);
	}

	[Fact]
	public async Task PublishCommandProgressAsync_MultipleEvents_AllDeliveredInOrder()
	{
		var pub = new SpyPublisher();
		var received = new List<string>();
		pub.CommandProgressReceived += (_, e) => received.Add(e.Command);

		await pub.PublishCommandProgressAsync("r", PeripheralId.Gps, "cmd-1", "", false);
		await pub.PublishCommandProgressAsync("r", PeripheralId.Gps, "cmd-2", "", false);
		await pub.PublishCommandProgressAsync("r", PeripheralId.Gps, "cmd-3", "", false);

		Assert.Equal(["cmd-1", "cmd-2", "cmd-3"], received);
	}

	[Fact]
	public async Task NullLiveEventPublisher_PublishCommandProgressAsync_DoesNotThrow()
	{
		// Regression: NullLiveEventPublisher was missing this override,
		// causing a NullReferenceException in CLI/headless mode.
		var pub = new NullLiveEventPublisher();

		var ex = await Record.ExceptionAsync(() =>
			pub.PublishCommandProgressAsync("r", PeripheralId.Sdr, "SoapySDRUtil --find", "", false));

		Assert.Null(ex); // completed successfully, no exception
	}
}
