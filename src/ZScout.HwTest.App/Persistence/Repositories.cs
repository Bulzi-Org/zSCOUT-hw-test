using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Persistence;

public sealed class RunRepository : FileBackedRepository<TestRun>
{
	public RunRepository(IConfiguration config) : base(
		config["DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data"),
		"runs.json")
	{ }

	protected override string GetId(TestRun entity) => entity.RunId;

	public async Task<TestRun?> GetActiveRunAsync(CancellationToken ct = default)
	{
		var all = await GetAllAsync(ct);
		return all.FirstOrDefault(r =>
			r.Status is RunStatus.Queued or RunStatus.Running or RunStatus.AwaitingVerdict);
	}

	public async Task<IReadOnlyList<TestRun>> GetByDateRangeAsync(
		DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
	{
		var all = await GetAllAsync(ct);
		return all.Where(r => r.StartedAtUtc >= from && r.StartedAtUtc <= to).ToList();
	}
}

public sealed class EvidenceRepository : FileBackedRepository<PeripheralEvidence>
{
	public EvidenceRepository(IConfiguration config) : base(
		config["DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data"),
		"evidence.json")
	{ }

	protected override string GetId(PeripheralEvidence entity) => entity.EvidenceId;

	public async Task<IReadOnlyList<PeripheralEvidence>> GetForRunAsync(
		string runId, CancellationToken ct = default)
	{
		var all = await GetAllAsync(ct);
		return all.Where(e => e.RunId == runId).ToList();
	}
}

public sealed class VerdictRepository : FileBackedRepository<PeripheralVerdict>
{
	public VerdictRepository(IConfiguration config) : base(
		config["DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data"),
		"verdicts.json")
	{ }

	protected override string GetId(PeripheralVerdict entity) => entity.VerdictId;

	public async Task<IReadOnlyList<PeripheralVerdict>> GetForRunAsync(
		string runId, CancellationToken ct = default)
	{
		var all = await GetAllAsync(ct);
		return all.Where(v => v.RunId == runId).ToList();
	}
}

public sealed class ExportJobRepository : FileBackedRepository<ExportJob>
{
	public ExportJobRepository(IConfiguration config) : base(
		config["DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data"),
		"exports.json")
	{ }

	protected override string GetId(ExportJob entity) => entity.ExportJobId;
}
