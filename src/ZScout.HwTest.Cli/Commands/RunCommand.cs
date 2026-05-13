using System.Text.Json;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.Cli.Commands;

/// <summary>
/// CLI command to start a hardware communication test run.
/// Supports --mode host|container and --format human|json output.
/// T022: Host/container mode with human and JSON outputs.
/// </summary>
public sealed class RunCommand
{
	private readonly RunRepository _runs;
	private readonly EvidenceRepository _evidence;
	private readonly VerdictRepository _verdicts;
	private readonly RunLockService _lockService;
	private readonly RunOrchestrator _orchestrator;
	private readonly RunResultSerializer _serializer;

	public RunCommand(
		RunRepository runs,
		EvidenceRepository evidence,
		VerdictRepository verdicts,
		RunLockService lockService,
		RunOrchestrator orchestrator,
		RunResultSerializer serializer)
	{
		_runs = runs;
		_evidence = evidence;
		_verdicts = verdicts;
		_lockService = lockService;
		_orchestrator = orchestrator;
		_serializer = serializer;
	}

	public enum OutputFormat { Human, Json }

	public async Task<int> ExecuteAsync(
		RunMode mode,
		OutputFormat format = OutputFormat.Human,
		CancellationToken ct = default)
	{
		// Check active run lock
		var (canStart, activeRun) = await _lockService.TryAcquireAsync(ct);
		if (!canStart)
		{
			WriteError(format, $"A run is already active (runId: {activeRun!.RunId}).");
			return 1;
		}

		// Create run record
		var run = new TestRun
		{
			RunId = Guid.NewGuid().ToString("N"),
			Mode = mode,
			Status = RunStatus.Queued,
			RequestedByUserId = "cli",
			Configuration = new RunConfiguration(),
			StartedAtUtc = DateTimeOffset.UtcNow
		};
		await _runs.SaveAsync(run, ct);

		if (format == OutputFormat.Human)
			Console.WriteLine($"[zSCOUT] Starting hardware communication test — mode: {mode}");

		// Execute full suite
		await _orchestrator.ExecuteAsync(run.RunId, ct);

		// Reload run state after orchestration
		run = await _runs.GetByIdAsync(run.RunId, ct) ?? run;
		var evidence = await _evidence.GetForRunAsync(run.RunId, ct);
		var verdicts = await _verdicts.GetForRunAsync(run.RunId, ct);

		if (format == OutputFormat.Json)
		{
			Console.WriteLine(_serializer.Serialize(run, evidence, verdicts));
		}
		else
		{
			Console.WriteLine();
			Console.WriteLine($"{"Peripheral",-12} {"Status",-14} {"Samples",7}  Diagnostics");
			Console.WriteLine(new string('-', 70));

			foreach (var ev in evidence)
			{
				var statusStr = ev.HealthSnapshot.Values.TryGetValue("status", out var s)
					? s?.ToString() ?? "unknown"
					: ev.DependencyAvailable ? "probed" : "unavailable";

				Console.WriteLine($"{ev.PeripheralId,-12} {statusStr,-14} {ev.SampleCount,7}  " +
								  $"{string.Join("; ", ev.DiagnosticMessages.Take(2))}");
			}

			Console.WriteLine();
			Console.WriteLine($"Run status : {run.Status}");
			Console.WriteLine($"Run ID     : {run.RunId}");
			Console.WriteLine("Use the dashboard or API to assign operator verdicts.");
		}

		return 0;
	}

	private static void WriteError(OutputFormat format, string message)
	{
		if (format == OutputFormat.Json)
			Console.WriteLine(JsonSerializer.Serialize(new { error = message }));
		else
			Console.Error.WriteLine($"[ERROR] {message}");
	}
}
