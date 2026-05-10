namespace ZScout.HwTest.App.Persistence;

/// <summary>
/// Background service that prunes expired runs, evidence, verdicts, and stream records
/// once per day according to the configured retention window.
/// </summary>
public sealed class RetentionPrunerService : BackgroundService
{
    private readonly RunRepository _runs;
    private readonly EvidenceRepository _evidence;
    private readonly VerdictRepository _verdicts;
    private readonly RetentionPolicy _policy;
    private readonly ILogger<RetentionPrunerService> _logger;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);

    public RetentionPrunerService(
        RunRepository runs,
        EvidenceRepository evidence,
        VerdictRepository verdicts,
        RetentionPolicy policy,
        ILogger<RetentionPrunerService> logger)
    {
        _runs = runs;
        _evidence = evidence;
        _verdicts = verdicts;
        _policy = policy;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PruneAsync(stoppingToken);
            await Task.Delay(PruneInterval, stoppingToken);
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        var cutoff = _policy.CutoffUtc;
        _logger.LogInformation("Running retention pruner. Cutoff: {Cutoff:O}", cutoff);

        var allRuns = await _runs.GetAllAsync(ct);
        var expiredRunIds = new HashSet<string>();

        foreach (var run in allRuns)
        {
            if (run.StartedAtUtc.HasValue && run.StartedAtUtc.Value < cutoff)
            {
                expiredRunIds.Add(run.RunId);
                await _runs.DeleteAsync(run.RunId, ct);
                _logger.LogDebug("Pruned expired run {RunId}", run.RunId);
            }
        }

        var allEvidence = await _evidence.GetAllAsync(ct);
        foreach (var e in allEvidence.Where(e => expiredRunIds.Contains(e.RunId)))
        {
            await _evidence.DeleteAsync(e.EvidenceId, ct);
        }

        var allVerdicts = await _verdicts.GetAllAsync(ct);
        foreach (var v in allVerdicts.Where(v => expiredRunIds.Contains(v.RunId)))
        {
            await _verdicts.DeleteAsync(v.VerdictId, ct);
        }

        _logger.LogInformation("Retention pruner complete. Removed {Count} expired run(s).", expiredRunIds.Count);
    }
}
