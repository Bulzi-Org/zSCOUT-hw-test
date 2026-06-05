using ZScout.HwTest.App;
using ZScout.HwTest.App.Api;
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

// ── Persistence ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<RunRepository>();
builder.Services.AddSingleton<EvidenceRepository>();
builder.Services.AddSingleton<VerdictRepository>();
builder.Services.AddSingleton<TelemetryStreamRepository>();
builder.Services.AddSingleton<ExportJobRepository>();
builder.Services.AddSingleton<CommandLogRepository>();
builder.Services.AddSingleton<RetentionPolicy>();
builder.Services.AddHostedService<RetentionPrunerService>();
builder.Services.AddSingleton<ExportService>();
builder.Services.AddSingleton<TelemetryStreamWriter>();

// ── Run Services ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<RunLockService>();
builder.Services.AddSingleton<RunCancellationService>();
builder.Services.AddSingleton<RunResultSerializer>();
builder.Services.AddSingleton<RunConfigurationService>();

// ── HTTP Clients (Tier 2 service communication) ────────────────────────────────────
builder.Services.AddHttpClient();

// ── Hardware Adapters ─────────────────────────────────────────────────────────────────
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
builder.Services.AddSingleton<NmeaStreamService>();
builder.Services.AddHostedService<GpsFixStreamService>();

// ── Dashboard (Blazor Server) ─────────────────────────────────────────────────
builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();
builder.Services.AddScoped<RunCommandService>();

// ── Misc ─────────────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Middleware pipeline ──────────────────────────────────────────────────────
app.UseStaticFiles();
app.UseMiddleware<CorrelationMiddleware>(); // T040: correlation IDs
app.UseAntiforgery();

// ── Routes ────────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapHub<HardwareStatusHub>("/hubs/hardware");

app.MapRunsEndpoints();
app.MapPeripheralsEndpoints();
app.MapStreamsEndpoints();
app.MapHistoryEndpoints();
app.MapExportsEndpoints();

// ── Blazor Server components ──────────────────────────────────────────────────
app.MapRazorComponents<ZScout.HwTest.App.App>()
	.AddInteractiveServerRenderMode();

app.Run();
