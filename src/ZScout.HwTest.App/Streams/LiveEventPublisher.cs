using Microsoft.AspNetCore.SignalR;
using ZScout.HwTest.App.Dashboard.Hubs;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Streams;

/// <summary>
/// Publishes live events to all connected SignalR clients.
/// Injected into the run orchestrator and adapters to push real-time updates.
/// </summary>
public class LiveEventPublisher
{
	private readonly IHubContext<HardwareStatusHub> _hub;

	public LiveEventPublisher(IHubContext<HardwareStatusHub> hub)
	{
		_hub = hub;
	}

	public virtual async Task PublishRunStatusAsync(string runId, RunStatus status, CancellationToken ct = default)
		=> await _hub.Clients.All.SendAsync(
			HubEvents.RunStatusChanged,
			new { runId, status = status.ToString() },
			ct);

	public virtual async Task PublishPeripheralStatusAsync(
		string runId, PeripheralId peripheralId, PeripheralStatus status, CancellationToken ct = default)
		=> await _hub.Clients.All.SendAsync(
			HubEvents.PeripheralStatusChanged,
			new { runId, peripheralId = peripheralId.ToString(), status = status.ToString() },
			ct);

	public virtual async Task PublishTelemetrySampleAsync(
		string runId, PeripheralId peripheralId, string sample, CancellationToken ct = default)
		=> await _hub.Clients.All.SendAsync(
			HubEvents.TelemetrySample,
			new { runId, peripheralId = peripheralId.ToString(), sample },
			ct);
}
