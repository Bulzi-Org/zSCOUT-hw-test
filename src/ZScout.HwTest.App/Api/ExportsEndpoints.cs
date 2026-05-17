using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Api;

/// <summary>
/// T037: Export job creation and artifact download endpoints.
/// </summary>
public static class ExportsEndpoints
{
	public static IEndpointRouteBuilder MapExportsEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/exports").WithTags("Exports");

		// GET /api/exports — list all export jobs
		group.MapGet("/", async (ExportJobRepository repo, CancellationToken ct) =>
			Results.Ok(await repo.GetAllAsync(ct)));

		// POST /api/exports — create a new export (T037)
		group.MapPost("/", async (
			CreateExportRequest req,
			ExportService exportSvc,
			CancellationToken ct) =>
		{
			ExportJob job;
			try
			{
				job = await exportSvc.CreateAsync(
					new ExportService.CreateExportRequest(req.From, req.To, "dashboard", req.Format), ct);
			}
			catch (ArgumentException ex)
			{
				return Results.BadRequest(new { Message = ex.Message });
			}

			return Results.Created($"/api/exports/{job.ExportJobId}", job);
		});

		// GET /api/exports/{jobId} — get a single export job status
		group.MapGet("/{jobId}", async (string jobId, ExportJobRepository repo, CancellationToken ct) =>
		{
			var job = await repo.GetByIdAsync(jobId, ct);
			return job is null ? Results.NotFound() : Results.Ok(job);
		});

		// GET /api/exports/{jobId}/download — stream the zip artifact
		group.MapGet("/{jobId}/download", async (
			string jobId,
			ExportJobRepository repo,
			CancellationToken ct) =>
		{
			var job = await repo.GetByIdAsync(jobId, ct);
			if (job is null) return Results.NotFound();
			if (job.Status != ExportStatus.Ready || job.ArtifactPath is null)
				return Results.BadRequest(new { Message = $"Export is not ready (status: {job.Status})." });
			if (!File.Exists(job.ArtifactPath))
				return Results.NotFound(new { Message = "Artifact file not found." });

			var fileName = Path.GetFileName(job.ArtifactPath);
			return Results.File(job.ArtifactPath, "application/zip", fileName);
		});

		return app;
	}

	public sealed record CreateExportRequest(
		DateTimeOffset From,
		DateTimeOffset To,
		ExportFormat Format = ExportFormat.ZipJson);
}
