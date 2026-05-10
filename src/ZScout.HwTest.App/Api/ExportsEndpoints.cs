using ZScout.HwTest.App.Auth;
using ZScout.HwTest.App.Persistence;

namespace ZScout.HwTest.App.Api;

/// <summary>
/// Placeholder for export job endpoints (T037, Phase 5).
/// </summary>
public static class ExportsEndpoints
{
    public static IEndpointRouteBuilder MapExportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/exports").WithTags("Exports")
            .RequireAuthorization(PolicyNames.RequireOperator);

        group.MapGet("/", async (ExportJobRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAllAsync(ct)));

        return app;
    }
}
