namespace ZScout.HwTest.Contracts.Models;

public sealed record ExportJob
{
    public required string ExportJobId { get; init; }
    public required string RequestedByUserId { get; init; }
    public required DateTimeOffset FromUtc { get; init; }
    public required DateTimeOffset ToUtc { get; init; }
    public required ExportFormat Format { get; init; }
    public required ExportStatus Status { get; init; }
    public string? ArtifactPath { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
