using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ZScout.HwTest.App.Hardware.Gps;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Tests;

public sealed class GpsAdapterReportStepTests
{
	/// <summary>
	/// When gps-svc is not reachable, the adapter returns Unavailable
	/// and reports via reportStep.
	/// </summary>
	[Fact]
	public async Task ProbeAsync_WhenGpsSvcNotReachable_ReportsUnreachable()
	{
		ILogger<GpsAdapter> logger = NullLogger<GpsAdapter>.Instance;
		var config = new ConfigurationBuilder().Build();
		var adapter = new GpsAdapter(logger, config, new StubHttpClientFactory());

		var calls = new List<(string Cmd, bool IsError)>();
		Task ReportStep(string cmd, string output, bool isError)
		{
			calls.Add((cmd, isError));
			return Task.CompletedTask;
		}

		var result = await adapter.ProbeAsync(RunMode.Container, ReportStep);

		// Without gps-svc running, the adapter must return Unavailable
		Assert.Equal(PeripheralStatus.Unavailable, result.Status);
		Assert.False(result.DependencyAvailable);

		// At least one report step should indicate unreachability
		Assert.NotEmpty(calls);
		Assert.Contains(calls, c => c.IsError);
	}

	[Fact]
	public async Task ProbeAsync_WithNullReportStep_DoesNotThrow()
	{
		ILogger<GpsAdapter> logger = NullLogger<GpsAdapter>.Instance;
		var config = new ConfigurationBuilder().Build();
		var adapter = new GpsAdapter(logger, config, new StubHttpClientFactory());

		// Must not throw when no reportStep is provided (backward-compat)
		var result = await adapter.ProbeAsync(RunMode.Host, reportStep: null);

		Assert.NotNull(result);
		Assert.Equal(PeripheralId.Gps, result.PeripheralId);
	}
}
