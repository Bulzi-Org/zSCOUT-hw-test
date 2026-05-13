using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Orchestrates a full hardware communication test run.
/// T020: Executes all peripheral adapters and collects per-peripheral evidence.
/// T024: Dependency-failure isolation — adapter exceptions never stop other peripherals.
/// </summary>
public sealed class RunOrchestrator
{
	private readonly IEnumerable<IHardwareAdapter> _adapters;
	private readonly RunRepository _runs;
	private readonly EvidenceRepository _evidence;
	private readonly LiveEventPublisher _events;
	private readonly ILogger<RunOrchestrator> _logger;

	public RunOrchestrator(
		IEnumerable<IHardwareAdapter> adapters,
		RunRepository runs,
		EvidenceRepository evidence,
		LiveEventPublisher events,
		ILogger<RunOrchestrator> logger)
	{
		_adapters = adapters;
		_runs = runs;
		_evidence = evidence;
		_events = events;
		_logger = logger;
	}

	/// <summary>
	/// Execute the full suite for the given run.
	/// Each adapter runs independently — a failure in one never affects others (T024).
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
		await _runs.SaveAsync(run, ct);
		await _events.PublishRunStatusAsync(runId, RunStatus.Running, ct);

		_logger.LogInformation("Run {RunId} started in {Mode} mode", runId, run.Mode);

		using var scope = _logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId, ["RunMode"] = run.Mode.ToString() });

		// Execute all adapters concurrently (T024: isolated — each catches its own exceptions)
		var adapterTasks = _adapters.Select(adapter => ProbeAdapterSafeAsync(adapter, run, ct));
		var evidenceList = await Task.WhenAll(adapterTasks);

		// Persist all evidence
		foreach (var ev in evidenceList)
			await _evidence.SaveAsync(ev, ct);

		// Transition: Running → AwaitingVerdict
		run = run with { Status = RunStatus.AwaitingVerdict };
		await _runs.SaveAsync(run, ct);
		await _events.PublishRunStatusAsync(runId, RunStatus.AwaitingVerdict, ct);

		_logger.LogInformation("Run {RunId} evidence collection complete — awaiting operator verdicts", runId);
	}

	/// <summary>
	/// Wraps a single adapter probe in a try/catch so exceptions are captured as
	/// diagnostic evidence rather than bubbling up to stop the orchestrator (T024).
	/// </summary>
	private async Task<PeripheralEvidence> ProbeAdapterSafeAsync(
		IHardwareAdapter adapter, TestRun run, CancellationToken ct)
	{
		_logger.LogDebug("Probing {Peripheral}...", adapter.PeripheralId);

		DiagnosticEnvelope envelope;
		try
		{
			envelope = await adapter.ProbeAsync(run.Mode, ct);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unhandled exception in {Peripheral} adapter", adapter.PeripheralId);
			envelope = DiagnosticEnvelope.Unavailable(
				adapter.PeripheralId,
				$"Adapter threw unhandled exception: {ex.GetType().Name}: {ex.Message}");
		}

		await _events.PublishPeripheralStatusAsync(run.RunId, adapter.PeripheralId, envelope.Status, ct);

		return new PeripheralEvidence
		{
			EvidenceId = Guid.NewGuid().ToString("N"),
			RunId = run.RunId,
			PeripheralId = adapter.PeripheralId,
			SampleCount = envelope.Snapshot.Values.TryGetValue("nmea_sentence_count", out var c)
				? Convert.ToInt32(c) : (envelope.Status == PeripheralStatus.Ready ? 1 : 0),
			LastSampleAtUtc = envelope.Status == PeripheralStatus.Ready ? envelope.CapturedAtUtc : null,
			HealthSnapshot = envelope.Snapshot,
			DiagnosticMessages = envelope.Messages,
			DependencyAvailable = envelope.DependencyAvailable,
			RawStreamPointer = null
		};
	}
}
