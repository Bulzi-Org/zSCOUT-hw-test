using Microsoft.AspNetCore.SignalR;
using ZScout.HwTest.App.Dashboard.Hubs;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Streams;

public sealed record RunStatusEventArgs(string RunId, RunStatus Status);
public sealed record PeripheralStatusEventArgs(string RunId, PeripheralId PeripheralId, PeripheralStatus Status);
public sealed record CommandProgressEventArgs(string RunId, PeripheralId PeripheralId, string Command, string Output, bool IsError, DateTimeOffset TimestampUtc);

/// <summary>
/// Publishes live events to all connected SignalR clients AND raises .NET events
/// so Blazor Server components can subscribe directly without a JS SignalR connection.
/// </summary>
public class LiveEventPublisher
{
	private readonly IHubContext<HardwareStatusHub> _hub;

	// .NET events for Blazor Server components to subscribe to
	public event EventHandler<RunStatusEventArgs>? RunStatusChanged;
	public event EventHandler<PeripheralStatusEventArgs>? PeripheralStatusChanged;
	public event EventHandler<CommandProgressEventArgs>? CommandProgressReceived;

	/// <summary>Raises <see cref="CommandProgressReceived"/>; callable from derived classes.</summary>
	protected void RaiseCommandProgressReceived(CommandProgressEventArgs args)
		=> CommandProgressReceived?.Invoke(this, args);

	public LiveEventPublisher(IHubContext<HardwareStatusHub> hub)
	{
		_hub = hub;
	}

	public virtual async Task PublishRunStatusAsync(string runId, RunStatus status, CancellationToken ct = default)
	{
		RunStatusChanged?.Invoke(this, new RunStatusEventArgs(runId, status));
		await _hub.Clients.All.SendAsync(
			HubEvents.RunStatusChanged,
			new { runId, status = status.ToString() },
			ct);
	}

	public virtual async Task PublishPeripheralStatusAsync(
		string runId, PeripheralId peripheralId, PeripheralStatus status, CancellationToken ct = default)
	{
		PeripheralStatusChanged?.Invoke(this, new PeripheralStatusEventArgs(runId, peripheralId, status));
		await _hub.Clients.All.SendAsync(
			HubEvents.PeripheralStatusChanged,
			new { runId, peripheralId = peripheralId.ToString(), status = status.ToString() },
			ct);
	}

	/// <summary>
	/// Publishes a single command progress event for live dashboard display.
	/// </summary>
	public virtual async Task PublishCommandProgressAsync(
		string runId, PeripheralId peripheralId, string command, string output, bool isError, CancellationToken ct = default)
	{
		CommandProgressReceived?.Invoke(this, new CommandProgressEventArgs(runId, peripheralId, command, output, isError, DateTimeOffset.UtcNow));
		await _hub.Clients.All.SendAsync(
			HubEvents.CommandProgress,
			new { runId, peripheralId = peripheralId.ToString(), command, output, isError },
			ct);
	}

	public virtual async Task PublishTelemetrySampleAsync(
		string runId, PeripheralId peripheralId, string sample, CancellationToken ct = default)
		=> await _hub.Clients.All.SendAsync(
			HubEvents.TelemetrySample,
			new { runId, peripheralId = peripheralId.ToString(), sample },
			ct);
}
