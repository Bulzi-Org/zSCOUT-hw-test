using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Dashboard.Services;

/// <summary>
/// Scoped service that exposes run-lifecycle operations to Blazor components.
/// Calls repositories and services directly — no HTTP roundtrip needed from server-side Blazor.
/// </summary>
public sealed class RunCommandService
{
	private readonly RunRepository _runs;
	private readonly EvidenceRepository _evidence;
	private readonly VerdictRepository _verdicts;
	private readonly RunLockService _lockService;
	private readonly RunOrchestrator _orchestrator;
	private readonly VerdictService _verdictService;
	private readonly RunConfigurationService _config;
	private readonly LiveEventPublisher _events;
	private readonly RunCancellationService _cancellation;

	public RunCommandService(
		RunRepository runs,
		EvidenceRepository evidence,
		VerdictRepository verdicts,
		RunLockService lockService,
		RunOrchestrator orchestrator,
		VerdictService verdictService,
		RunConfigurationService config,
		LiveEventPublisher events,
		RunCancellationService cancellation)
	{
		_runs = runs;
		_evidence = evidence;
		_verdicts = verdicts;
		_lockService = lockService;
		_orchestrator = orchestrator;
		_verdictService = verdictService;
		_config = config;
		_events = events;
		_cancellation = cancellation;
	}

	/// <summary>Start a new run; returns (run, null) on success or (null, errorMsg) on conflict.</summary>
	public async Task<(TestRun? Run, string? Error)> StartRunAsync(
		RunMode mode, string userId, IReadOnlySet<PeripheralId> selectedTests,
		CancellationToken ct = default)
	{
		var (canStart, active) = await _lockService.TryAcquireAsync(ct);
		if (!canStart)
			return (null, $"A run is already active (ID: {active!.RunId}).");

		var cfg = await _config.GetAsync(ct);
		var run = new TestRun
		{
			RunId = Guid.NewGuid().ToString("N"),
			Mode = mode,
			Status = RunStatus.Queued,
			RequestedByUserId = userId,
			Configuration = cfg,
			SelectedTests = selectedTests.OrderBy(p => p).ToList(),
			StartedAtUtc = DateTimeOffset.UtcNow
		};

		await _runs.SaveAsync(run, ct);
		await _events.PublishRunStatusAsync(run.RunId, RunStatus.Queued, ct);

		// Register a CancellationTokenSource for this run so StopRunAsync can cancel it
		var runCt = _cancellation.Register(run.RunId);

		// Fire orchestrator as background task; UI gets 202-style immediate response
		_ = Task.Run(async () =>
		{
			try { await _orchestrator.ExecuteAsync(run.RunId, runCt); }
			catch { /* orchestrator logs internally */ }
		});

		return (run, null);
	}

	/// <summary>Stop an active run; returns error string if not stoppable.</summary>
	public async Task<string?> StopRunAsync(string runId, CancellationToken ct = default)
	{
		var run = await _runs.GetByIdAsync(runId, ct);
		if (run is null) return "Run not found.";
		if (run.Status is not (RunStatus.Queued or RunStatus.Running or RunStatus.AwaitingVerdict))
			return $"Run is not active (status: {run.Status}).";

		// Cancel all in-flight adapter probes (e.g. GPS streaming) before persisting Stopped
		_cancellation.Cancel(runId);

		var stopped = run with { Status = RunStatus.Stopped, FinishedAtUtc = DateTimeOffset.UtcNow };
		await _runs.SaveAsync(stopped, ct);
		await _events.PublishRunStatusAsync(runId, RunStatus.Stopped, ct);
		return null;
	}

	/// <summary>
	/// Stop the current peripheral test and assign an operator verdict.
	/// Cancels the adapter, then records the pass/fail verdict which triggers
	/// the orchestrator to advance to the next peripheral.
	/// </summary>
	public async Task<(bool Success, string? Error)> StopTestWithVerdictAsync(
		string runId, PeripheralId peripheralId, VerdictOutcome outcome,
		string? failureReason, string userId, CancellationToken ct = default)
	{
		// Cancel the adapter probe first so the orchestrator transitions to AwaitingVerdict
		_orchestrator.StopTest(runId, peripheralId);

		// Small yield to let the orchestrator's ProbeAdapterSafeAsync catch the cancellation
		// and transition to AwaitingVerdict before we assign the verdict.
		await Task.Delay(50, ct);

		// Assign the verdict — VerdictService will signal the orchestrator to advance
		var result = await _verdictService.AssignAsync(
			new VerdictService.AssignVerdictRequest(runId, peripheralId, outcome, failureReason, userId), ct);
		return (result.Success, result.Error);
	}

	public async Task<TestRun?> GetActiveRunAsync(CancellationToken ct = default)
		=> await _runs.GetActiveRunAsync(ct);

	public async Task<IReadOnlyList<TestRun>> GetRecentRunsAsync(int count = 10, CancellationToken ct = default)
	{
		var all = await _runs.GetAllAsync(ct);
		return all.OrderByDescending(r => r.StartedAtUtc).Take(count).ToList();
	}

	public async Task<TestRun?> GetRunAsync(string runId, CancellationToken ct = default)
		=> await _runs.GetByIdAsync(runId, ct);

	public async Task<IReadOnlyList<PeripheralEvidence>> GetEvidenceAsync(
		string runId, CancellationToken ct = default)
		=> await _evidence.GetForRunAsync(runId, ct);

	public async Task<IReadOnlyList<PeripheralVerdict>> GetVerdictsAsync(
		string runId, CancellationToken ct = default)
		=> await _verdicts.GetForRunAsync(runId, ct);

	public async Task<(bool Success, string? Error)> AssignVerdictAsync(
		string runId, PeripheralId peripheralId, VerdictOutcome outcome,
		string? failureReason, string userId, CancellationToken ct = default)
	{
		var result = await _verdictService.AssignAsync(
			new VerdictService.AssignVerdictRequest(runId, peripheralId, outcome, failureReason, userId), ct);
		return (result.Success, result.Error);
	}
}
