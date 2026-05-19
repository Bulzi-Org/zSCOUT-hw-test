using Grpc.Net.Client;

namespace ZScout.HwTest.App.Hardware.Common;

/// <summary>
/// Creates <see cref="GrpcChannel"/> instances for Tier 2 service connections.
/// Uses HTTP/2 over plaintext (no TLS) since services run on the same host network.
/// </summary>
public static class GrpcChannelFactory
{
	/// <summary>
	/// Creates a gRPC channel to the specified <paramref name="host"/> and <paramref name="port"/>.
	/// Caller is responsible for disposing the returned channel.
	/// </summary>
	public static GrpcChannel Create(string host, int port)
	{
		var address = $"http://{host}:{port}";
		return GrpcChannel.ForAddress(address, new GrpcChannelOptions
		{
			// Disable TLS — Tier 2 services run on localhost via --network host
			HttpHandler = new SocketsHttpHandler
			{
				EnableMultipleHttp2Connections = true,
				ConnectTimeout = TimeSpan.FromSeconds(5)
			}
		});
	}
}
