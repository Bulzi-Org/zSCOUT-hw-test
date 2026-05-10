using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Guards against concurrent runs: only one run may be active (Queued, Running, or AwaitingVerdict) at a time.
/// </summary>
public sealed class RunLockService
{
    private readonly RunRepository _runs;

    public RunLockService(RunRepository runs)
    {
        _runs = runs;
    }

    /// <summary>
    /// Returns true if a new run may begin. Returns false and the active run if one is already in progress.
    /// </summary>
    public async Task<(bool CanStart, TestRun? ActiveRun)> TryAcquireAsync(CancellationToken ct = default)
    {
        var active = await _runs.GetActiveRunAsync(ct);
        return active is null ? (true, null) : (false, active);
    }
}
