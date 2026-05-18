using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Hardware.Compass;
using ZScout.HwTest.App.Hardware.Gps;
using ZScout.HwTest.App.Hardware.Halow;
using ZScout.HwTest.App.Hardware.Sdr;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Cli.Commands;
using ZScout.HwTest.Contracts.Models;

// ── Parse args ─────────────────────────────────────────────────────────────
var modeArg = args.FirstOrDefault(a => a.StartsWith("--mode="))?.Split('=')[1]
		   ?? args.SkipWhile(a => a != "--mode").Skip(1).FirstOrDefault()
		   ?? "host";

var formatArg = args.FirstOrDefault(a => a.StartsWith("--format="))?.Split('=')[1]
			 ?? args.SkipWhile(a => a != "--format").Skip(1).FirstOrDefault()
			 ?? "human";

if (!Enum.TryParse<RunMode>(modeArg, ignoreCase: true, out var runMode))
{
	Console.Error.WriteLine($"[ERROR] Unknown --mode '{modeArg}'. Use: host | container");
	return 1;
}

var outputFormat = formatArg.Equals("json", StringComparison.OrdinalIgnoreCase)
	? RunCommand.OutputFormat.Json
	: RunCommand.OutputFormat.Human;

// ── Build host & services ──────────────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
	.ConfigureAppConfiguration(cfg => cfg
		.AddJsonFile("appsettings.json", optional: true)
		.AddEnvironmentVariables())
	.ConfigureLogging(logging =>
	{
		logging.ClearProviders();
		// Suppress verbose logs in human mode; keep them in JSON mode for diagnostics
		if (outputFormat == RunCommand.OutputFormat.Human)
			logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
		else
			logging.AddConsole().SetMinimumLevel(LogLevel.Information);
	})
	.ConfigureServices((ctx, services) =>
	{
		var config = ctx.Configuration;

		// Persistence
		services.AddSingleton<RunRepository>();
		services.AddSingleton<EvidenceRepository>();
		services.AddSingleton<VerdictRepository>();
		services.AddSingleton<ExportJobRepository>();
		services.AddSingleton<RetentionPolicy>();

		// Hardware adapters
		services.AddSingleton<IHardwareAdapter, GpsAdapter>();
		services.AddSingleton<IHardwareAdapter, SdrAdapter>();
		services.AddSingleton<IHardwareAdapter, HalowAdapter>();
		services.AddSingleton<IHardwareAdapter, CompassAdapter>();

		// Run services
		services.AddSingleton<RunLockService>();
		services.AddSingleton<RunCancellationService>();
		services.AddSingleton<RunResultSerializer>();

		// SignalR live events: no-op for CLI (no connected clients)
		services.AddSingleton<LiveEventPublisher, NullLiveEventPublisher>();

		services.AddSingleton<RunOrchestrator>();
		services.AddSingleton<RunCommand>();
	})
	.Build();

var command = host.Services.GetRequiredService<RunCommand>();
return await command.ExecuteAsync(runMode, outputFormat);

