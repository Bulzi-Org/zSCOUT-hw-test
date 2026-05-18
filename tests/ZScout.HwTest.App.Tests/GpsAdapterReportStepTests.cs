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
	/// Regression test for the duplicate reportStep bug.
	/// Before the fix, when gpsd was not running, reportStep was called twice
	/// for "pgrep -x gpsd" — once with the actual process output, and once
	/// with a hardcoded message inside the early-return block.
	/// After the fix the early-return block has no reportStep call, so
	/// "pgrep -x gpsd" appears exactly once in the log.
	/// </summary>
	[Fact]
	public async Task ProbeAsync_WhenGpsdNotRunning_PgrepReportedExactlyOnce()
	{
		ILogger<GpsAdapter> logger = NullLogger<GpsAdapter>.Instance;
		var config = new ConfigurationBuilder().Build();
		var adapter = new GpsAdapter(logger, config);

		var calls = new List<(string Cmd, bool IsError)>();
		Task ReportStep(string cmd, string output, bool isError)
		{
			calls.Add((cmd, isError));
			return Task.CompletedTask;
		}

		var result = await adapter.ProbeAsync(RunMode.Container, ReportStep);

		// On any machine without gpsd, the adapter must return Unavailable
		// and reportStep must have been called exactly once for pgrep.
		Assert.Equal(PeripheralStatus.Unavailable, result.Status);
		Assert.False(result.DependencyAvailable);

		var pgrepCalls = calls.Where(c => c.Cmd == "pgrep -x gpsd").ToList();
		Assert.Single(pgrepCalls); // was 2 before the fix
		Assert.True(pgrepCalls[0].IsError);
	}

	[Fact]
	public async Task ProbeAsync_WithNullReportStep_DoesNotThrow()
	{
		ILogger<GpsAdapter> logger = NullLogger<GpsAdapter>.Instance;
		var config = new ConfigurationBuilder().Build();
		var adapter = new GpsAdapter(logger, config);

		// Must not throw when no reportStep is provided (backward-compat)
		var result = await adapter.ProbeAsync(RunMode.Host, reportStep: null);

		Assert.NotNull(result);
		Assert.Equal(PeripheralId.Gps, result.PeripheralId);
	}
}
