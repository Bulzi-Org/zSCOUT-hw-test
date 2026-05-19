using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Orchestrates a full hardware communication test run.
/// T020: Executes all peripheral adapters concurrently via Task.WhenAll and collects per-peripheral evidence.
/// T024: Dependency-failure isolation — adapter exceptions never stop other peripherals.
/// Auto-assigns verdicts after all adapters complete (Ready→Pass, else→Fail).
/// </summary>
public sealed class RunOrchestrator
{
	private readonly IEnumerable<IHardwareAdapter> _adapters;
	private readonly RunRepository _runs;
	private readonly EvidenceRepository _evidence;
	private readonly VerdictRepository _verdicts;
	private readonly LiveEventPublisher _events;
	private readonly RunCancellationService _cancellation;
	private readonly ILogger<RunOrchestrator> _logger;

	public RunOrchestrator(
		IEnumerable<IHardwareAdapter> adapters,
		RunRepository runs,
		EvidenceRepository evidence,
		VerdictRepository verdicts,
		LiveEventPublisher events,
		RunCancellationService cancellation,
		ILogger<RunOrchestrator> logger)
	{
		_adapters = adapters;
		_runs = runs;
		_evidence = evidence;
		_verdicts = verdicts;
		_events = events;
		_cancellation = cancellation;
		_logger = logger;
	}

	/// <summary>
	/// Execute the full suite for the given run.
	/// Adapters run concurrently and results are processed incrementally as each completes
	/// (FR-012). Evidence is saved and per-adapter verdicts are assigned immediately,
	/// so fast adapters (Compass, SDR, HaLow) deliver results while GPS continues streaming.
	/// Each adapter is isolated — a failure in one never affects others (T024).
	/// </summary>
	public async Task ExecuteAsync(string runId, CancellationToken ct = default)
	{
		var run = await _runs.GetByIdAsync(runId, ct);
		if (run is null)
		{
			_logger.LogError("Run {RunId} not found", runId);
			return;
		}

		// Transition: Queued → Running
		run = run with { Status = RunStatus.Running, StartedAtUtc = DateTimeOffset.UtcNow };
		await _runs.SaveAsync(run, CancellationToken.None);
		await _events.PublishRunStatusAsync(runId, RunStatus.Running, CancellationToken.None);

		_logger.LogInformation("Run {RunId} started in {Mode} mode", runId, run.Mode);

		using var scope = _logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId, ["RunMode"] = run.Mode.ToString() });

		// Launch all adapter probes concurrently
		var probeTasks = _adapters.Select(adapter => ProbeAdapterSafeAsync(adapter, run, ct)).ToList();

		// Process results incrementally as each adapter completes (FR-012)
		var evidenceList = new List<PeripheralEvidence>();
		var statusByPeripheral = new Dictionary<PeripheralId, PeripheralStatus>();

		await foreach (var completedTask in Task.WhenEach(probeTasks))
		{
			var (ev, status) = await completedTask;
			evidenceList.Add(ev);
			statusByPeripheral[ev.PeripheralId] = status;

			// Save evidence and assign verdict immediately (FR-012)
			await _evidence.SaveAsync(ev, CancellationToken.None);
			await AssignVerdictAsync(run, ev, status, CancellationToken.None);

			_logger.LogInformation(
				"Adapter {Peripheral} completed with status {Status} in run {RunId}",
				ev.PeripheralId, status, runId);
		}

		// Unregister CTS now that all probes are done (normal completion path)
		_cancellation.Unregister(runId);

		// Complete the run with overall outcome
		await CompleteRunAsync(run, statusByPeripheral, CancellationToken.None);

		_logger.LogInformation("Run {RunId} completed with auto-assigned verdicts", runId);
	}

	/// <summary>
	/// Wraps a single adapter probe in a try/catch so exceptions are captured as
	/// diagnostic evidence rather than bubbling up to stop the orchestrator (T024).
	/// Passes a reportStep callback to publish live command progress events.
	/// </summary>
	private async Task<(PeripheralEvidence Evidence, PeripheralStatus Status)> ProbeAdapterSafeAsync(
		IHardwareAdapter adapter, TestRun run, CancellationToken ct)
	{
		_logger.LogDebug("Probing {Peripheral}...", adapter.PeripheralId);

		Func<string, string, bool, Task> reportStep = async (cmd, output, isError) =>
			await _events.PublishCommandProgressAsync(run.RunId, adapter.PeripheralId, cmd, output, isError, ct);

		DiagnosticEnvelope envelope;
		try
		{
			envelope = await adapter.ProbeAsync(run.Mode, reportStep, ct);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unhandled exception in {Peripheral} adapter", adapter.PeripheralId);
			envelope = DiagnosticEnvelope.Unavailable(
				adapter.PeripheralId,
				$"Adapter threw unhandled exception: {ex.GetType().Name}: {ex.Message}");
		}

		await _events.PublishPeripheralStatusAsync(run.RunId, adapter.PeripheralId, envelope.Status, ct);

		// Use total_fix_updates for GPS (streaming); fall back to nmea_sentence_count for legacy snapshots,
		// then to a binary 0/1 based on Ready status for other peripherals.
		var sampleCount = envelope.Snapshot.Values.TryGetValue("total_fix_updates", out var tfu)
			? Convert.ToInt32(tfu)
			: envelope.Snapshot.Values.TryGetValue("nmea_sentence_count", out var nsc)
				? Convert.ToInt32(nsc)
				: (envelope.Status == PeripheralStatus.Ready ? 1 : 0);

		var evidence = new PeripheralEvidence
		{
			EvidenceId = Guid.NewGuid().ToString("N"),
			RunId = run.RunId,
			PeripheralId = adapter.PeripheralId,
			SampleCount = sampleCount,
			LastSampleAtUtc = envelope.Status == PeripheralStatus.Ready ? envelope.CapturedAtUtc : null,
			HealthSnapshot = envelope.Snapshot,
			DiagnosticMessages = envelope.Messages,
			DependencyAvailable = envelope.DependencyAvailable,
			RawStreamPointer = null
		};

		return (evidence, envelope.Status);
	}

	/// <summary>
	/// Assigns a verdict for a single adapter result immediately upon completion.
	/// Ready → Pass, Degraded/Unavailable → Fail.
	/// </summary>
	private async Task AssignVerdictAsync(
		TestRun run,
		PeripheralEvidence ev,
		PeripheralStatus status,
		CancellationToken ct)
	{
		var outcome = status == PeripheralStatus.Ready
			? VerdictOutcome.Pass
			: VerdictOutcome.Fail;

		string? failureReason = outcome == VerdictOutcome.Fail
			? ev.DiagnosticMessages.Count > 0
				? ev.DiagnosticMessages[^1]
				: $"{status}"
			: null;

		var verdict = new PeripheralVerdict
		{
			VerdictId = Guid.NewGuid().ToString("N"),
			RunId = run.RunId,
			PeripheralId = ev.PeripheralId,
			Outcome = outcome,
			FailureReason = failureReason,
			AssignedByUserId = "system",
			AssignedAtUtc = DateTimeOffset.UtcNow
		};

		await _verdicts.SaveAsync(verdict, ct);
		_logger.LogInformation(
			"Auto-verdict {Outcome} assigned for {Peripheral} in run {RunId}",
			outcome, ev.PeripheralId, run.RunId);
	}

	/// <summary>
	/// Completes the run with an overall outcome computed from all adapter statuses.
	/// </summary>
	private async Task CompleteRunAsync(
		TestRun run,
		Dictionary<PeripheralId, PeripheralStatus> statusByPeripheral,
		CancellationToken ct)
	{
		var anyFail = statusByPeripheral.Values.Any(s => s != PeripheralStatus.Ready);
		var overallOutcome = anyFail ? OverallOutcome.Fail : OverallOutcome.Pass;
		var completed = run with
		{
			Status = RunStatus.Completed,
			OverallOutcome = overallOutcome,
			FinishedAtUtc = DateTimeOffset.UtcNow
		};

		await _runs.SaveAsync(completed, ct);
		await _events.PublishRunStatusAsync(run.RunId, RunStatus.Completed, ct);
	}
}
