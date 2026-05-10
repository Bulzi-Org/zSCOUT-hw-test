using ZScout.HwTest.App.Auth;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Api;

public static class PeripheralsEndpoints
{
	public static IEndpointRouteBuilder MapPeripheralsEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/peripherals").WithTags("Peripherals")
			.RequireAuthorization(PolicyNames.RequireViewer);

		// GET /api/peripherals — list all peripheral IDs and latest known status (T030)
		group.MapGet("/", async (EvidenceRepository evidence, CancellationToken ct) =>
		{
			// Return a summary of the most recent status per peripheral across all runs
			var all = await evidence.GetAllAsync(ct);
			var latest = Enum.GetValues<PeripheralId>()
				.Select(pid =>
				{
					var ev = all
						.Where(e => e.PeripheralId == pid)
						.OrderByDescending(e => e.LastSampleAtUtc ?? DateTimeOffset.MinValue)
						.FirstOrDefault();

					return new
					{
						PeripheralId = pid.ToString(),
						LastStatus = ev?.HealthSnapshot.Values.TryGetValue("status", out var s) == true
							? s?.ToString() ?? "Unknown" : "Unknown",
						LastSampleAtUtc = ev?.LastSampleAtUtc,
						DependencyAvailable = ev?.DependencyAvailable
					};
				});
			return Results.Ok(latest);
		});

		// GET /api/peripherals/{runId} — evidence for a specific run
		group.MapGet("/{runId}", async (string runId, EvidenceRepository evidence, CancellationToken ct) =>
		{
			var items = await evidence.GetForRunAsync(runId, ct);
			return Results.Ok(items);
		});

		return app;
	}
}
