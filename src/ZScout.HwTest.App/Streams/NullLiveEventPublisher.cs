using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Streams;

/// <summary>
/// No-op LiveEventPublisher for CLI / headless mode where no SignalR clients are connected.
/// </summary>
public sealed class NullLiveEventPublisher : LiveEventPublisher
{
	public NullLiveEventPublisher() : base(null!) { }

	public override Task PublishRunStatusAsync(string runId, RunStatus status, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishPeripheralStatusAsync(
		string runId, PeripheralId peripheralId, PeripheralStatus status, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishTelemetrySampleAsync(
		string runId, PeripheralId peripheralId, string sample, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override Task PublishCommandProgressAsync(
		string runId, PeripheralId peripheralId, string command, string output, bool isError, CancellationToken ct = default)
		=> Task.CompletedTask;

	public override void PublishNmeaSentence(string sentence) { }

	public override void PublishNmeaConnectionState(bool connected) { }
	public override Task PublishGpsFixAsync(Hardware.Gps.GpsFix fix, CancellationToken ct = default)
		=> Task.CompletedTask;
}
