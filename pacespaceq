using System.Collections.Concurrent;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Orchestrates a full hardware communication test run.
/// Executes peripheral adapters sequentially in the order defined by PeripheralId enum
/// (Compass → GPS → SDR → HaLow). Each adapter runs until the operator stops it and
/// assigns a verdict (Pass / Fail) before the next peripheral begins.
/// </summary>
public sealed class RunOrchestrator
{
	private readonly IEnumerable<IHardwareAdapter> _adapters;
	private readonly RunRepository _runs;
	private readonly EvidenceRepository _evidence;
	private readonly LiveEventPublisher _events;
	private readonly RunCancellationService _cancellation;
	private readonly ILogger<RunOrchestrator> _logger;

	/// <summary>Per-test CTS keyed by "runId:peripheralId". Set by the UI stop buttons.</summary>
	private readonly ConcurrentDictionary<string, CancellationTokenSource> _testCts = new();

	/// <summary>
	/// Signals the orchestrator that a verdict has been assigned for the current peripheral.
	/// Keyed by runId. Created before each adapter starts; completed by <see cref="NotifyVerdictAssigned"/>.
	/// </summary>
	private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _verdictSignals = new();

	public RunOrchestrator(
		IEnumerable<IHardwareAdapter> adapters,
		RunRepository runs,
		EvidenceRepository evidence,
		LiveEventPublisher events,
		RunCancellationService cancellation,
		ILogger<RunOrchestrator> logger)
	{
		_adapters = adapters;
		_runs = runs;
		_evidence = evidence;
		_events = events;
		_cancellation = cancellation;
		_logger = logger;
	}

	/// <summary>
	/// Execute the full suite for the given run sequentially.
	/// For each peripheral: start its adapter, wait for the operator to stop it and
	/// assign a verdict, then advance to the next peripheral.
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

		// Execute adapters sequentially — one at a time, operator-driven advancement
		foreach (var adapter in orderedAdapters)
		{
			if (ct.IsCancellationRequested) break;

			// Prepare a verdict signal the operator will complete via VerdictService
			var verdictTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_verdictSignals[runId] = verdictTcs;

			// Create a per-test CTS so Stop-Success / Stop-Fail can cancel only this adapter
			var testKey = $"{runId}:{adapter.PeripheralId}";
			var testCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			_testCts[testKey] = testCts;

			try
			{
				// Run the adapter until the operator cancels it
				var (ev, status) = await ProbeAdapterSafeAsync(adapter, run, testCts.Token);
				await _evidence.SaveAsync(ev, CancellationToken.None);

				_logger.LogInformation(
					"Adapter {Peripheral} stopped in run {RunId} — awaiting operator verdict",
					adapter.PeripheralId, runId);

				// Transition to AwaitingVerdict so VerdictService accepts the verdict
				run = run with { Status = RunStatus.AwaitingVerdict };
				await _runs.SaveAsync(run, CancellationToken.None);
				await _events.PublishRunStatusAsync(runId, RunStatus.AwaitingVerdict, CancellationToken.None);

				// Wait for the operator to assign a verdict before advancing
				using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				await using (waitCts.Token.Register(() => verdictTcs.TrySetCanceled()))
				{
					await verdictTcs.Task;
				}

				// Transition back to Running for the next peripheral
				run = run with { Status = RunStatus.Running };
				await _runs.SaveAsync(run, CancellationToken.None);
				await _events.PublishRunStatusAsync(runId, RunStatus.Running, CancellationToken.None);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				_logger.LogInformation("Run {RunId} cancelled during {Peripheral}", runId, adapter.PeripheralId);
				break;
			}
			finally
			{
				_testCts.TryRemove(testKey, out _);
				testCts.Dispose();
				_verdictSignals.TryRemove(runId, out _);
			}
		}

		// Unregister run-level CTS now that all probes are done
		_cancellation.Unregister(runId);

		// If the run wasn't stopped externally, mark it as completed
		if (!ct.IsCancellationRequested)
		{
			var completed = run with
			{
				Status = RunStatus.Completed,
				FinishedAtUtc = DateTimeOffset.UtcNow
			};
			await _runs.SaveAsync(completed, CancellationToken.None);
			await _events.PublishRunStatusAsync(runId, RunStatus.Completed, CancellationToken.None);
			_logger.LogInformation("Run {RunId} completed", runId);
		}
	}

	/// <summary>
	/// Signals the orchestrator that a verdict has been assigned for the current peripheral,
	/// allowing advancement to the next one.
	/// </summary>
	public void NotifyVerdictAssigned(string runId)
	{
		if (_verdictSignals.TryGetValue(runId, out var tcs))
			tcs.TrySetResult(true);
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
			return (ev, status);
		}
		finally
		{
			_testCts.TryRemove(testKey, out _);
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
}
