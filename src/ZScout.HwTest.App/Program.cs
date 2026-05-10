using Microsoft.AspNetCore.Authentication.Cookies;
using ZScout.HwTest.App.Api;
using ZScout.HwTest.App.Auth;
using ZScout.HwTest.App.Dashboard.Hubs;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Hardware.Compass;
using ZScout.HwTest.App.Hardware.Gps;
using ZScout.HwTest.App.Hardware.Halow;
using ZScout.HwTest.App.Hardware.Sdr;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.App.Streams;

var builder = WebApplication.CreateBuilder(args);

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

// ── Persistence ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<RunRepository>();
builder.Services.AddSingleton<EvidenceRepository>();
builder.Services.AddSingleton<VerdictRepository>();
builder.Services.AddSingleton<ExportJobRepository>();
builder.Services.AddSingleton<RetentionPolicy>();
builder.Services.AddHostedService<RetentionPrunerService>();

// ── Auth Services ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IUserStore, UserStore>();
builder.Services.AddScoped<LocalAuthService>();

// ── Run Services ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<RunLockService>();
builder.Services.AddSingleton<RunResultSerializer>();

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

// ── Misc ─────────────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Middleware pipeline ──────────────────────────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();

// ── Routes ────────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapHub<HardwareStatusHub>("/hubs/hardware");

app.MapAuthEndpoints();
app.MapRunsEndpoints();
app.MapPeripheralsEndpoints();
app.MapStreamsEndpoints();
app.MapExportsEndpoints();

// Dashboard UI will be wired in Phase 4 (Blazor Server components)

app.Run();
