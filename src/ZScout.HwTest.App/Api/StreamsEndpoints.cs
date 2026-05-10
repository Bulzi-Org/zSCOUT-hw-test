using ZScout.HwTest.App.Auth;

namespace ZScout.HwTest.App.Api;

/// <summary>
/// Placeholder for telemetry stream endpoints (T033, Phase 5).
/// </summary>
public static class StreamsEndpoints
{
	public static IEndpointRouteBuilder MapStreamsEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/streams").WithTags("Streams")
			.RequireAuthorization(PolicyNames.RequireViewer);

		group.MapGet("/{runId}/{peripheralId}", () => Results.Ok(Array.Empty<object>()));

		return app;
	}
}
