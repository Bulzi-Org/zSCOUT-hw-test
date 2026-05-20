namespace ZScout.HwTest.App.Tests;

/// <summary>
/// Minimal <see cref="IHttpClientFactory"/> stub for unit tests.
/// Returns a new <see cref="HttpClient"/> each time. The client will fail
/// to connect to any real endpoint, which is the expected behavior when
/// testing adapters without a live service.
/// </summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
	public HttpClient CreateClient(string name) => new();
}
