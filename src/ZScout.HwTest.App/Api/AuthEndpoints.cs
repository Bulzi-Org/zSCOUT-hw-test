using Microsoft.AspNetCore.Mvc;
using ZScout.HwTest.App.Auth;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Api;

public static class AuthEndpoints
{
	public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/auth").WithTags("Auth");

		group.MapPost("/login", async (
			[FromBody] LoginRequest req,
			LocalAuthService auth,
			HttpContext ctx,
			CancellationToken ct) =>
		{
			var user = await auth.ValidateCredentialsAsync(req.Username, req.Password, ct);
			if (user is null) return Results.Unauthorized();
			await LocalAuthService.SignInAsync(ctx, user);
			return Results.Ok(new { user.UserId, user.Username, Role = user.Role.ToString() });
		});

		group.MapPost("/logout", async (HttpContext ctx) =>
		{
			await LocalAuthService.SignOutAsync(ctx);
			return Results.Ok();
		}).RequireAuthorization(PolicyNames.RequireViewer);

		group.MapGet("/me", (HttpContext ctx) =>
		{
			if (ctx.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
			return Results.Ok(new
			{
				UserId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
				Username = ctx.User.Identity.Name,
				Role = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
			});
		}).RequireAuthorization(PolicyNames.RequireViewer);

		return app;
	}

	public sealed record LoginRequest(string Username, string Password);
}
