namespace ZScout.HwTest.Contracts.Models;

public sealed record RunConfiguration
{
    public int TimeoutSeconds { get; init; } = 30;
    public int PollingIntervalMs { get; init; } = 500;
    public Dictionary<string, string> Paths { get; init; } = [];
}

public sealed record TestRun
{
    public required string RunId { get; init; }
    public required RunMode Mode { get; init; }
    public required RunStatus Status { get; init; }
    public required string RequestedByUserId { get; init; }
    public required RunConfiguration Configuration { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public string? RejectionReason { get; init; }
    public OverallOutcome? OverallOutcome { get; init; }
}
