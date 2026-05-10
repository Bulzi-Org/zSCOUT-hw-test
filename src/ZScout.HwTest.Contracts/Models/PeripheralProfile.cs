namespace ZScout.HwTest.Contracts.Models;

public sealed record PeripheralProfile
{
    public required PeripheralId PeripheralId { get; init; }
    public required string DisplayName { get; init; }
    public required PeripheralTransport Transport { get; init; }
    public string? ExpectedPath { get; init; }
    public string? DependencyService { get; init; }
    public string? DriverName { get; init; }
    public PeripheralStatus LastObservedStatus { get; init; } = PeripheralStatus.Unknown;
    public DateTimeOffset? LastObservedAtUtc { get; init; }
    public string? LastDiagnostic { get; init; }
}
