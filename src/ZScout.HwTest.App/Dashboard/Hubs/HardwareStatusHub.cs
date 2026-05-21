using Microsoft.AspNetCore.SignalR;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Dashboard.Hubs;

/// <summary>
/// SignalR hub for real-time hardware status and run lifecycle events.
/// Clients subscribe to receive push notifications without polling.
/// </summary>
public sealed class HardwareStatusHub : Hub
{
	public override Task OnConnectedAsync()
		=> base.OnConnectedAsync();

	public override Task OnDisconnectedAsync(Exception? exception)
		=> base.OnDisconnectedAsync(exception);
}

/// <summary>
/// Client-side method names pushed from server to connected Blazor/browser clients.
/// </summary>
public static class HubEvents
{
	public const string RunStatusChanged = "RunStatusChanged";
	public const string PeripheralStatusChanged = "PeripheralStatusChanged";
	public const string CommandProgress = "CommandProgress";
	public const string TelemetrySample = "TelemetrySample";
	public const string GpsFixReceived = "GpsFixReceived";
}
