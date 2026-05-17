using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Api;

/// <summary>
/// T034: Run history query endpoint with mode/date filters and details projection.
/// </summary>
public static class HistoryEndpoints
{
	public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/history").WithTags("History");

		// GET /api/history — query run history with optional filters
		// Query params: mode, from (ISO-8601), to (ISO-8601), page (1-based), pageSize
		group.MapGet("/", async (
			HttpContext ctx,
			RunRepository runs,
			RetentionPolicy retention,
			CancellationToken ct) =>
		{
			var qs = ctx.Request.Query;

			RunMode? modeFilter = null;
			if (qs.TryGetValue("mode", out var modeStr) &&
				Enum.TryParse<RunMode>(modeStr, ignoreCase: true, out var m))
				modeFilter = m;

			DateTimeOffset fromFilter = retention.CutoffUtc;
			if (qs.TryGetValue("from", out var fromStr) &&
				DateTimeOffset.TryParse(fromStr, out var fromParsed))
				fromFilter = fromParsed < retention.CutoffUtc ? retention.CutoffUtc : fromParsed;

			DateTimeOffset toFilter = DateTimeOffset.UtcNow;
			if (qs.TryGetValue("to", out var toStr) &&
				DateTimeOffset.TryParse(toStr, out var toParsed))
				toFilter = toParsed;

			int page = 1;
			if (qs.TryGetValue("page", out var pageStr) && int.TryParse(pageStr, out var p) && p > 0)
				page = p;

			int pageSize = 20;
			if (qs.TryGetValue("pageSize", out var psStr) && int.TryParse(psStr, out var ps) && ps > 0)
				pageSize = Math.Min(ps, 100);

			var all = await runs.GetByDateRangeAsync(fromFilter, toFilter, ct);
			var filtered = all
				.Where(r => modeFilter is null || r.Mode == modeFilter)
				.OrderByDescending(r => r.StartedAtUtc)
				.ToList();

			var total = filtered.Count;
			var page_items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

			return Results.Ok(new
			{
				Total = total,
				Page = page,
				PageSize = pageSize,
				Items = page_items
			});
		});

		// GET /api/history/{runId} — full details for a single historical run
		group.MapGet("/{runId}", async (
			string runId,
			RunRepository runs,
			EvidenceRepository evidence,
			VerdictRepository verdicts,
			RetentionPolicy retention,
			RunResultSerializer serializer,
			CancellationToken ct) =>
		{
			var run = await runs.GetByIdAsync(runId, ct);
			if (run is null) return Results.NotFound();

			// Enforce retention boundary
			if (!retention.IsWithinRetention(run.StartedAtUtc))
				return Results.NotFound(new { Message = "Run is outside the retention window." });

			var ev = await evidence.GetForRunAsync(runId, ct);
			var vd = await verdicts.GetForRunAsync(runId, ct);

			return Results.Ok(new
			{
				Run = run,
				Evidence = ev,
				Verdicts = vd,
				ResultJson = serializer.Serialize(run, ev, vd)
			});
		});

		return app;
	}
}
