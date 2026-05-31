using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Orchestrates a full hardware communication test run.
/// Executes peripheral adapters sequentially in the order defined by PeripheralId enum
/// (Compass → GPS → SDR → HaLow). Each adapter runs until stopped by the user or
/// until an error is detected. Supports manual per-test triggering via RunTestAsync.
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

	/// <summary>Per-test CTS keyed by "runId:peripheralId". Set by the UI stop buttons.</summary>
	private readonly Dictionary<string, CancellationTokenSource> _testCts = new();

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
	/// Execute the full suite for the given run sequentially.
	/// Adapters run one at a time in PeripheralId enum order (Compass, Gps, Sdr, Halow).
	/// Each adapter is isolated — a failure in one never affects others.
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

		_logger.LogInformation("Run {RunId} started in {Mode} mode (sequential)", runId, run.Mode);

		using var scope = _logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId, ["RunMode"] = run.Mode.ToString() });

		// Build ordered adapter list following PeripheralId enum order
		var orderedAdapters = _adapters
			.OrderBy(a => a.PeripheralId)
			.ToList();

		var statusByPeripheral = new Dictionary<PeripheralId, PeripheralStatus>();

		// Execute adapters sequentially
		foreach (var adapter in orderedAdapters)
		{
			if (ct.IsCancellationRequested) break;

			var (ev, status) = await ProbeAdapterSafeAsync(adapter, run, ct);
			statusByPeripheral[ev.PeripheralId] = status;

			await _evidence.SaveAsync(ev, CancellationToken.None);
			await AssignVerdictAsync(run, ev, status, CancellationToken.None);

			_logger.LogInformation(
				"Adapter {Peripheral} completed with status {Status} in run {RunId}",
				ev.PeripheralId, status, runId);
		}

		// Unregister CTS now that all probes are done
		_cancellation.Unregister(runId);

		// Complete the run with overall outcome
		await CompleteRunAsync(run, statusByPeripheral, CancellationToken.None);

		_logger.LogInformation("Run {RunId} completed with auto-assigned verdicts", runId);
	}

	/// <summary>
	/// Run a single test by peripheral ID. Used by the Run Flow UI "RUN" button.
	/// Creates a per-test CTS that can be cancelled independently.
	/// </summary>
	public async Task<(PeripheralEvidence Evidence, PeripheralStatus Status)> RunTestAsync(
		string runId, PeripheralId peripheralId, CancellationToken ct = default)
	{
		var run = await _runs.GetByIdAsync(runId, ct);
		if (run is null) throw new InvalidOperationException($"Run {runId} not found");

		var adapter = _adapters.FirstOrDefault(a => a.PeripheralId == peripheralId)
			?? throw new InvalidOperationException($"No adapter for {peripheralId}");

		var testKey = $"{runId}:{peripheralId}";
		var testCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_testCts[testKey] = testCts;

		try
		{
			var (ev, status) = await ProbeAdapterSafeAsync(adapter, run, testCts.Token);
			await _evidence.SaveAsync(ev, CancellationToken.None);
			await AssignVerdictAsync(run, ev, status, CancellationToken.None);
			return (ev, status);
		}
		finally
		{
			_testCts.Remove(testKey);
			testCts.Dispose();
		}
	}

	/// <summary>
	/// Stop a specific test in progress. Called by STOP-FAILED / STOP-SUCCESS buttons.
	/// </summary>
	public void StopTest(string runId, PeripheralId peripheralId)
	{
		var testKey = $"{runId}:{peripheralId}";
		if (_testCts.TryGetValue(testKey, out var cts))
			cts.Cancel();
	}

	/// <summary>
	/// Wraps a single adapter probe in a try/catch so exceptions are captured as
	/// diagnostic evidence rather than bubbling up to stop the orchestrator.
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
		catch (OperationCanceledException)
		{
			// User stopped this test — treat as controlled stop, not error
			envelope = DiagnosticEnvelope.Unavailable(
				adapter.PeripheralId,
				"Test stopped by user");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unhandled exception in {Peripheral} adapter", adapter.PeripheralId);
			envelope = DiagnosticEnvelope.Unavailable(
				adapter.PeripheralId,
				$"Adapter threw unhandled exception: {ex.GetType().Name}: {ex.Message}");
		}

		await _events.PublishPeripheralStatusAsync(run.RunId, adapter.PeripheralId, envelope.Status, ct);

		var sampleCount = envelope.Snapshot.Values.TryGetValue("total_fix_updates", out var tfu)
			? Convert.ToInt32(tfu)
			: envelope.Snapshot.Values.TryGetValue("nmea_sentence_count", out var nsc)
				? Convert.ToInt32(nsc)
				: envelope.Snapshot.Values.TryGetValue("poll_count", out var pc)
					? Convert.ToInt32(pc)
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
