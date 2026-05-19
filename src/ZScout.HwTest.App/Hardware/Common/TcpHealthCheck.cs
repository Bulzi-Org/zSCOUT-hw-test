using System.Net.Sockets;

namespace ZScout.HwTest.App.Hardware.Common;

/// <summary>
/// Shared helper for testing TCP connectivity to Tier 2 service endpoints.
/// Used by adapters to verify service availability before attempting full probes.
/// </summary>
public static class TcpHealthCheck
{
	/// <summary>
	/// Attempts a TCP connection to <paramref name="host"/>:<paramref name="port"/>
	/// with the specified <paramref name="timeoutMs"/>.
	/// Returns <c>true</c> if the connection succeeds, <c>false</c> otherwise.
	/// Never throws — connection failures are caught and returned as <c>false</c>.
	/// </summary>
	public static async Task<bool> CheckAsync(
		string host,
		int port,
		int timeoutMs = 5_000,
		CancellationToken ct = default)
	{
		try
		{
			using var client = new TcpClient();
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(timeoutMs);
			await client.ConnectAsync(host, port, cts.Token);
			return true;
		}
		catch (Exception) when (!ct.IsCancellationRequested)
		{
			return false;
		}
	}
}
