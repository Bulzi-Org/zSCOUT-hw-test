using System.Text.Json;
using System.Text.Json.Serialization;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Serializes a completed run into the machine-readable JSON format defined in
/// specs/001-hardware-comm-dashboard/contracts/run-result.schema.json
/// </summary>
public sealed class RunResultSerializer
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public sealed record RunResult(
        string RunId,
        string Mode,
        string Status,
        string? OverallOutcome,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? FinishedAtUtc,
        IReadOnlyList<PeripheralResult> Peripherals
    );

    public sealed record PeripheralResult(
        string PeripheralId,
        string Status,
        bool DependencyAvailable,
        int SampleCount,
        string? Outcome,
        string? FailureReason,
        IReadOnlyList<string> DiagnosticMessages
    );

    public RunResult Build(
        TestRun run,
        IReadOnlyList<PeripheralEvidence> evidence,
        IReadOnlyList<PeripheralVerdict> verdicts)
    {
        var peripheralResults = evidence.Select(e =>
        {
            var verdict = verdicts.FirstOrDefault(v => v.PeripheralId == e.PeripheralId);
            return new PeripheralResult(
                e.PeripheralId.ToString().ToLowerInvariant(),
                e.HealthSnapshot.Values.TryGetValue("status", out var s) ? s?.ToString() ?? "unknown" : "unknown",
                e.DependencyAvailable,
                e.SampleCount,
                verdict?.Outcome.ToString().ToLowerInvariant(),
                verdict?.FailureReason,
                e.DiagnosticMessages
            );
        }).ToList();

        return new RunResult(
            run.RunId,
            run.Mode.ToString().ToLowerInvariant(),
            run.Status.ToString().ToLowerInvariant(),
            run.OverallOutcome?.ToString().ToLowerInvariant(),
            run.StartedAtUtc,
            run.FinishedAtUtc,
            peripheralResults
        );
    }

    public string Serialize(RunResult result)
        => JsonSerializer.Serialize(result, _json);

    public string Serialize(
        TestRun run,
        IReadOnlyList<PeripheralEvidence> evidence,
        IReadOnlyList<PeripheralVerdict> verdicts)
        => Serialize(Build(run, evidence, verdicts));
}
