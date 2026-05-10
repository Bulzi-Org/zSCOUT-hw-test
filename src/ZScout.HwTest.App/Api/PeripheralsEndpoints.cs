using ZScout.HwTest.App.Auth;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Api;

public static class PeripheralsEndpoints
{
    public static IEndpointRouteBuilder MapPeripheralsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/peripherals").WithTags("Peripherals")
            .RequireAuthorization(PolicyNames.RequireViewer);

        // GET /api/peripherals/{runId} — evidence for a run
        group.MapGet("/{runId}", async (string runId, EvidenceRepository evidence, CancellationToken ct) =>
        {
            var items = await evidence.GetForRunAsync(runId, ct);
            return Results.Ok(items);
        });

        return app;
    }
}
