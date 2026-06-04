using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Manual verdict service: records operator pass/fail decisions for each peripheral.
/// After saving a verdict, signals the <see cref="RunOrchestrator"/> to advance to
/// the next peripheral in the sequential run.
/// </summary>
public sealed class VerdictService
{
	private readonly RunRepository _runs;
	private readonly VerdictRepository _verdicts;
	private readonly RunOrchestrator _orchestrator;
	private readonly ILogger<VerdictService> _logger;

	public VerdictService(
		RunRepository runs,
		VerdictRepository verdicts,
		RunOrchestrator orchestrator,
		ILogger<VerdictService> logger)
	{
		_runs = runs;
		_verdicts = verdicts;
		_orchestrator = orchestrator;
		_logger = logger;
	}

	public sealed record AssignVerdictRequest(
		string RunId,
		PeripheralId PeripheralId,
		VerdictOutcome Outcome,
		string? FailureReason,
		string AssignedByUserId);

	public sealed record AssignVerdictResult(bool Success, string? Error, PeripheralVerdict? Verdict);

	public async Task<AssignVerdictResult> AssignAsync(
		AssignVerdictRequest request, CancellationToken ct = default)
	{
		// Validate failure reason for Fail outcome
		if (request.Outcome == VerdictOutcome.Fail && string.IsNullOrWhiteSpace(request.FailureReason))
			return new AssignVerdictResult(false, "FailureReason is required when outcome is Fail.", null);

		var run = await _runs.GetByIdAsync(request.RunId, ct);
		if (run is null)
			return new AssignVerdictResult(false, $"Run {request.RunId} not found.", null);

		// Accept verdicts when the run is actively testing or awaiting a verdict
		if (run.Status is not (RunStatus.Running or RunStatus.AwaitingVerdict))
			return new AssignVerdictResult(false,
				$"Run must be in Running or AwaitingVerdict status (current: {run.Status}).", null);

		var verdict = new PeripheralVerdict
		{
			VerdictId = Guid.NewGuid().ToString("N"),
			RunId = request.RunId,
			PeripheralId = request.PeripheralId,
			Outcome = request.Outcome,
			FailureReason = request.FailureReason,
			AssignedByUserId = request.AssignedByUserId,
			AssignedAtUtc = DateTimeOffset.UtcNow
		};

		await _verdicts.SaveAsync(verdict, ct);
		_logger.LogInformation(
			"Verdict {Outcome} assigned for {Peripheral} in run {RunId} by {User}",
			request.Outcome, request.PeripheralId, request.RunId, request.AssignedByUserId);

		// Signal the orchestrator to advance to the next peripheral
		_orchestrator.NotifyVerdictAssigned(request.RunId);

		return new AssignVerdictResult(true, null, verdict);
	}
}
