using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Api;

public static class RunsEndpoints
{
	public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/runs").WithTags("Runs");

		// GET /api/runs — list all runs (viewer+)
		group.MapGet("/", async (RunRepository runs, CancellationToken ct) =>
		{
			var all = await runs.GetAllAsync(ct);
			return Results.Ok(all);
		});

		// GET /api/runs/{runId} — get single run detail (viewer+)
		group.MapGet("/{runId}", async (string runId, RunRepository runs, CancellationToken ct) =>
		{
			var run = await runs.GetByIdAsync(runId, ct);
			return run is null ? Results.NotFound() : Results.Ok(run);
		});

		// POST /api/runs — start a new run
		group.MapPost("/", async (
			StartRunRequest req,
			RunLockService lockSvc,
			RunRepository runs,
			RunOrchestrator orchestrator,
			LiveEventPublisher events,
			CancellationToken ct) =>
		{
			var (canStart, active) = await lockSvc.TryAcquireAsync(ct);
			if (!canStart)
				return Results.Conflict(new { Message = "A run is already active.", ActiveRunId = active!.RunId });

			var run = new TestRun
			{
				RunId = Guid.NewGuid().ToString("N"),
				Mode = req.Mode,
				Status = RunStatus.Queued,
				RequestedByUserId = "dashboard",
				Configuration = req.Configuration ?? new RunConfiguration(),
				StartedAtUtc = DateTimeOffset.UtcNow
			};
			await runs.SaveAsync(run, ct);
			await events.PublishRunStatusAsync(run.RunId, run.Status, ct);

			// Fire-and-forget: orchestrator runs in background, caller gets 202 immediately
			_ = Task.Run(async () =>
			{
				try { await orchestrator.ExecuteAsync(run.RunId); }
				catch { /* orchestrator logs internally */ }
			});

			return Results.Accepted($"/api/runs/{run.RunId}", run);
		});

		// DELETE /api/runs/{runId} — stop a run
		group.MapDelete("/{runId}", async (
			string runId,
			RunRepository runs,
			LiveEventPublisher events,
			CancellationToken ct) =>
		{
			var run = await runs.GetByIdAsync(runId, ct);
			if (run is null) return Results.NotFound();
			if (run.Status is not (RunStatus.Queued or RunStatus.Running))
				return Results.BadRequest(new { Message = "Run is not active." });

			var updated = run with { Status = RunStatus.Stopped, FinishedAtUtc = DateTimeOffset.UtcNow };
			await runs.SaveAsync(updated, ct);
			await events.PublishRunStatusAsync(runId, RunStatus.Stopped, ct);
			return Results.Ok(updated);
		});

		// POST /api/runs/{runId}/verdict/{peripheralId} — assign manual verdict
		group.MapPost("/{runId}/verdict/{peripheralId}", async (
			string runId,
			string peripheralId,
			VerdictRequest req,
			VerdictRepository verdicts,
			RunRepository runs,
			CancellationToken ct) =>
		{
			var run = await runs.GetByIdAsync(runId, ct);
			if (run is null) return Results.NotFound();

			if (req.Outcome == VerdictOutcome.Fail && string.IsNullOrWhiteSpace(req.FailureReason))
				return Results.BadRequest(new { Message = "FailureReason is required when outcome is Fail." });

			if (!Enum.TryParse<PeripheralId>(peripheralId, ignoreCase: true, out var pid))
				return Results.BadRequest(new { Message = $"Unknown peripheralId: {peripheralId}" });

			var verdict = new PeripheralVerdict
			{
				VerdictId = Guid.NewGuid().ToString("N"),
				RunId = runId,
				PeripheralId = pid,
				Outcome = req.Outcome,
				FailureReason = req.FailureReason,
				AssignedByUserId = "dashboard",
				AssignedAtUtc = DateTimeOffset.UtcNow
			};
			await verdicts.SaveAsync(verdict, ct);
			return Results.Created($"/api/runs/{runId}/verdict/{peripheralId}", verdict);
		});

		return app;
	}

	public sealed record StartRunRequest(RunMode Mode, RunConfiguration? Configuration);
	public sealed record VerdictRequest(VerdictOutcome Outcome, string? FailureReason);
}
