using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Persistence;

/// <summary>
/// T037: Produces ZIP-JSON export artifacts for a user-specified retention window.
/// T038: Enforces 30-day boundaries via RetentionPolicy.
/// Files are written to {dataDir}/exports/ and the job record tracks the artifact path.
/// </summary>
public sealed class ExportService
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
	};

	private readonly RunRepository _runs;
	private readonly EvidenceRepository _evidence;
	private readonly VerdictRepository _verdicts;
	private readonly TelemetryStreamRepository _streams;
	private readonly ExportJobRepository _jobs;
	private readonly RetentionPolicy _retention;
	private readonly string _exportDir;
	private readonly ILogger<ExportService> _logger;

	public ExportService(
		RunRepository runs,
		EvidenceRepository evidence,
		VerdictRepository verdicts,
		TelemetryStreamRepository streams,
		ExportJobRepository jobs,
		RetentionPolicy retention,
		IConfiguration config,
		ILogger<ExportService> logger)
	{
		_runs = runs;
		_evidence = evidence;
		_verdicts = verdicts;
		_streams = streams;
		_jobs = jobs;
		_retention = retention;
		_logger = logger;

		var dataDir = config["DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data");
		_exportDir = Path.Combine(dataDir, "exports");
		Directory.CreateDirectory(_exportDir);
	}

	public sealed record CreateExportRequest(
		DateTimeOffset From,
		DateTimeOffset To,
		string RequestedByUserId,
		ExportFormat Format = ExportFormat.ZipJson);

	/// <summary>
	/// Validates the range, creates a job record, and runs the export synchronously.
	/// Returns the completed ExportJob.
	/// </summary>
	public async Task<ExportJob> CreateAsync(CreateExportRequest request, CancellationToken ct = default)
	{
		// T038: Enforce retention boundaries
		_retention.ValidateExportRange(request.From, request.To);

		var job = new ExportJob
		{
			ExportJobId = Guid.NewGuid().ToString("N"),
			RequestedByUserId = request.RequestedByUserId,
			FromUtc = request.From,
			ToUtc = request.To,
			Format = request.Format,
			Status = ExportStatus.Queued,
			CreatedAtUtc = DateTimeOffset.UtcNow,
			ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
		};

		await _jobs.SaveAsync(job, ct);

		try
		{
			var artifactPath = await BuildZipAsync(job, ct);
			job = job with { Status = ExportStatus.Ready, ArtifactPath = artifactPath };
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Export job {JobId} failed", job.ExportJobId);
			job = job with { Status = ExportStatus.Failed };
		}

		await _jobs.SaveAsync(job, ct);
		return job;
	}

	private async Task<string> BuildZipAsync(ExportJob job, CancellationToken ct)
	{
		var runs = await _runs.GetByDateRangeAsync(job.FromUtc, job.ToUtc, ct);
		var runIds = runs.Select(r => r.RunId).ToHashSet();

		var allEvidence = await _evidence.GetAllAsync(ct);
		var allVerdicts = await _verdicts.GetAllAsync(ct);
		var allStreams = await _streams.GetAllAsync(ct);

		var evidence = allEvidence.Where(e => runIds.Contains(e.RunId)).ToList();
		var verdicts = allVerdicts.Where(v => runIds.Contains(v.RunId)).ToList();
		var streams = allStreams.Where(s => runIds.Contains(s.RunId)).ToList();

		var zipPath = Path.Combine(_exportDir, $"export-{job.ExportJobId}.zip");
		using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
		using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

		await WriteEntryAsync(archive, "runs.json", runs, ct);
		await WriteEntryAsync(archive, "evidence.json", evidence, ct);
		await WriteEntryAsync(archive, "verdicts.json", verdicts, ct);
		await WriteEntryAsync(archive, "telemetry.json", streams, ct);

		_logger.LogInformation(
			"Export {JobId}: {RunCount} runs, {EvidenceCount} evidence, {StreamCount} telemetry records zipped to {Path}",
			job.ExportJobId, runs.Count, evidence.Count, streams.Count, zipPath);

		return zipPath;
	}

	private static async Task WriteEntryAsync<T>(ZipArchive archive, string entryName, T data, CancellationToken ct)
	{
		var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
		using var stream = entry.Open();
		await JsonSerializer.SerializeAsync(stream, data, JsonOpts, ct);
	}
}
