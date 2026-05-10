namespace ZScout.HwTest.Contracts.Models;

public sealed record HealthSnapshot
{
    public Dictionary<string, object?> Values { get; init; } = [];
}

public sealed record PeripheralEvidence
{
    public required string EvidenceId { get; init; }
    public required string RunId { get; init; }
    public required PeripheralId PeripheralId { get; init; }
    public required int SampleCount { get; init; }
    public DateTimeOffset? LastSampleAtUtc { get; init; }
    public required HealthSnapshot HealthSnapshot { get; init; }
    public required IReadOnlyList<string> DiagnosticMessages { get; init; }
    public required bool DependencyAvailable { get; init; }
    public string? RawStreamPointer { get; init; }
}
