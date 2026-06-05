namespace ZScout.HwTest.Contracts.Models;

public sealed record CommandLogEntry
{
	public required string LogEntryId { get; init; }
	public required string RunId { get; init; }
	public required PeripheralId PeripheralId { get; init; }
	public required DateTimeOffset Timestamp { get; init; }
	public required string Command { get; init; }
	public required string Output { get; init; }
	public required bool IsError { get; init; }
}
