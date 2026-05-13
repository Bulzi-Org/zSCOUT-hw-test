using Microsoft.AspNetCore.Authentication.Cookies;
using ZScout.HwTest.App;
using ZScout.HwTest.App.Api;
using ZScout.HwTest.App.Auth;
using ZScout.HwTest.App.Dashboard.Hubs;
using ZScout.HwTest.App.Dashboard.Services;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Hardware.Compass;
using ZScout.HwTest.App.Hardware.Gps;
using ZScout.HwTest.App.Hardware.Halow;
using ZScout.HwTest.App.Hardware.Sdr;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.App.Streams;

var builder = WebApplication.CreateBuilder(args);

// ── Structured logging (T040) ─────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(opts =>
{
	opts.TimestampFormat = "O";
	opts.UseUtcTimestamp = true;
	opts.IncludeScopes = true;
});

// ── Authentication & Authorization ─────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
	.AddCookie(opts =>
	{
		opts.LoginPath = "/login";
		opts.LogoutPath = "/api/auth/logout";
		opts.Cookie.HttpOnly = true;
		opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
		opts.SlidingExpiration = true;
		opts.ExpireTimeSpan = TimeSpan.FromHours(8);
	});

builder.Services.AddAuthorization(AuthorizationPolicies.Register);
builder.Services.AddCascadingAuthenticationState();

// ── Persistence ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<RunRepository>();
builder.Services.AddSingleton<EvidenceRepository>();
builder.Services.AddSingleton<VerdictRepository>();
builder.Services.AddSingleton<TelemetryStreamRepository>();
builder.Services.AddSingleton<ExportJobRepository>();
builder.Services.AddSingleton<RetentionPolicy>();
builder.Services.AddHostedService<RetentionPrunerService>();
builder.Services.AddSingleton<ExportService>();
builder.Services.AddSingleton<TelemetryStreamWriter>();

// ── Auth Services ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IUserStore, UserStore>();
builder.Services.AddScoped<LocalAuthService>();
builder.Services.AddHostedService<DefaultAdminSeedService>();

// ── Run Services ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<RunLockService>();
builder.Services.AddSingleton<RunResultSerializer>();
builder.Services.AddSingleton<RunConfigurationService>();

// ── Hardware Adapters ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<IHardwareAdapter, GpsAdapter>();
builder.Services.AddSingleton<IHardwareAdapter, SdrAdapter>();
builder.Services.AddSingleton<IHardwareAdapter, HalowAdapter>();
builder.Services.AddSingleton<IHardwareAdapter, CompassAdapter>();

// ── Run Orchestration ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<RunOrchestrator>();
builder.Services.AddSingleton<VerdictService>();

// ── SignalR & Live Events ────────────────────────────────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSingleton<LiveEventPublisher>();

// ── Dashboard (Blazor Server) ─────────────────────────────────────────────────
builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();
builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<RunCommandService>();

// ── Misc ─────────────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Middleware pipeline ──────────────────────────────────────────────────────
app.UseStaticFiles();
app.UseMiddleware<CorrelationMiddleware>(); // T040: correlation IDs
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Form-based auth endpoints (used by Login.razor / MainLayout logout button) ──
app.MapPost("/account/login", async (HttpContext ctx, LocalAuthService auth) =>
{
	var form = ctx.Request.Form;
	var username = form["username"].ToString();
	var password = form["password"].ToString();
	var returnUrl = form["returnUrl"].FirstOrDefault() ?? "/";

	var user = await auth.ValidateCredentialsAsync(username, password);
	if (user is null) return Results.Redirect("/login?error=true");

	await LocalAuthService.SignInAsync(ctx, user);
	// Sanitize returnUrl to prevent open redirect
	return Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
		? Results.Redirect(returnUrl)
		: Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/account/logout", async (HttpContext ctx) =>
{
	await LocalAuthService.SignOutAsync(ctx);
	return Results.Redirect("/login");
}).RequireAuthorization().DisableAntiforgery();

// ── Routes ────────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapHub<HardwareStatusHub>("/hubs/hardware");

app.MapAuthEndpoints();
app.MapRunsEndpoints();
app.MapPeripheralsEndpoints();
app.MapStreamsEndpoints();
app.MapHistoryEndpoints();
app.MapExportsEndpoints();

// ── Blazor Server components ──────────────────────────────────────────────────
app.MapRazorComponents<ZScout.HwTest.App.App>()
	.AddInteractiveServerRenderMode();

app.Run();
app.Run();
