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
	/// and reports the TCP connect attempt exactly once via reportStep.
	/// </summary>
	[Fact]
	public async Task ProbeAsync_WhenGpsSvcNotReachable_TcpCheckReportedExactlyOnce()
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

		// Without gps-svc running, the adapter must return Unavailable
		// and reportStep must have been called exactly once for the TCP check.
		Assert.Equal(PeripheralStatus.Unavailable, result.Status);
		Assert.False(result.DependencyAvailable);

		var tcpCalls = calls.Where(c => c.Cmd.StartsWith("TCP connect")).ToList();
		Assert.Single(tcpCalls);
		Assert.True(tcpCalls[0].IsError);
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
