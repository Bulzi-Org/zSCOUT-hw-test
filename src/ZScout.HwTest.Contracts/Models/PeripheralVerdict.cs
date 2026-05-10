namespace ZScout.HwTest.Contracts.Models;

public sealed record PeripheralVerdict
{
	public required string VerdictId { get; init; }
	public required string RunId { get; init; }
	public required PeripheralId PeripheralId { get; init; }
	public required VerdictOutcome Outcome { get; init; }
	public string? FailureReason { get; init; }
	public required string AssignedByUserId { get; init; }
	public required DateTimeOffset AssignedAtUtc { get; init; }
}
