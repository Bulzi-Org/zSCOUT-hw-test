using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Api;

/// <summary>
/// T033: Raw telemetry stream retrieval with per-stream filtering.
/// </summary>
public static class StreamsEndpoints
{
	public static IEndpointRouteBuilder MapStreamsEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/streams").WithTags("Streams");

		// GET /api/streams/{runId} — all stream records for a run
		group.MapGet("/{runId}", async (
			string runId,
			TelemetryStreamRepository repo,
			CancellationToken ct) =>
		{
			var records = await repo.GetForRunAsync(runId, ct);
			return Results.Ok(records);
		});

		// GET /api/streams/{runId}/{peripheralId} — per-peripheral stream records
		group.MapGet("/{runId}/{peripheralId}", async (
			string runId,
			string peripheralId,
			TelemetryStreamRepository repo,
			CancellationToken ct) =>
		{
			if (!Enum.TryParse<PeripheralId>(peripheralId, ignoreCase: true, out var pid))
				return Results.BadRequest(new { Message = $"Unknown peripheralId: {peripheralId}" });

			var records = await repo.GetForRunPeripheralAsync(runId, pid, ct);
			return Results.Ok(records);
		});

		return app;
	}
}
