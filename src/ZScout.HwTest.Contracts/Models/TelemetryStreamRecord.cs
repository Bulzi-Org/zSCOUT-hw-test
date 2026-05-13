namespace ZScout.HwTest.Contracts.Models;

public sealed record TelemetryStreamRecord
{
	public required string StreamRecordId { get; init; }
	public required string RunId { get; init; }
	public required PeripheralId PeripheralId { get; init; }
	public required StreamType StreamType { get; init; }
	public required DateTimeOffset TimestampUtc { get; init; }
	public required string Payload { get; init; }
	public required bool IsMalformed { get; init; }
}
