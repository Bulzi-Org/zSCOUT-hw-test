using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Common;

/// <summary>
/// Envelope wrapping a peripheral diagnostic result with metadata.
/// </summary>
public sealed record DiagnosticEnvelope
{
    public required PeripheralId PeripheralId { get; init; }
    public required bool DependencyAvailable { get; init; }
    public required PeripheralStatus Status { get; init; }
    public required IReadOnlyList<string> Messages { get; init; }
    public required HealthSnapshot Snapshot { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }

    public static DiagnosticEnvelope Unavailable(PeripheralId id, string reason) => new()
    {
        PeripheralId = id,
        DependencyAvailable = false,
        Status = PeripheralStatus.Unavailable,
        Messages = [reason],
        Snapshot = new HealthSnapshot(),
        CapturedAtUtc = DateTimeOffset.UtcNow
    };
}
