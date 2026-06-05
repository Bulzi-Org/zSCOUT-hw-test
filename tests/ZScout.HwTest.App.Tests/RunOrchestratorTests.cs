using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.App.Runs;
using ZScout.HwTest.App.Streams;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Tests;

public sealed class RunOrchestratorTests : IDisposable
{
	private readonly string _dataDir;
	private readonly IConfiguration _config;
	private readonly RunRepository _runs;
	private readonly EvidenceRepository _evidence;
	private readonly CommandLogRepository _commandLog;
	private readonly RunCancellationService _cancellation;
	private readonly NullLiveEventPublisher _events;
	private readonly TelemetryStreamRepository _streamRepo;
	private readonly TelemetryStreamWriter _telemetry;

	public RunOrchestratorTests()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), $"zscout-test-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dataDir);
		_config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["DataDirectory"] = _dataDir })
			.Build();
		_runs = new RunRepository(_config);
		_evidence = new EvidenceRepository(_config);
		_commandLog = new CommandLogRepository(_config);
		_cancellation = new RunCancellationService();
		_events = new NullLiveEventPublisher();
		_streamRepo = new TelemetryStreamRepository(_config);
		_telemetry = new TelemetryStreamWriter(_streamRepo, NullLogger<TelemetryStreamWriter>.Instance);
	}

	public void Dispose()
	{
		try { Directory.Delete(_dataDir, recursive: true); } catch { }
	}

	private RunOrchestrator CreateOrchestrator(IEnumerable<IHardwareAdapter> adapters) =>
		new(adapters, _runs, _evidence, _commandLog, _events, _telemetry,
			_cancellation, NullLogger<RunOrchestrator>.Instance);

	private async Task<TestRun> SeedRunAsync(IReadOnlyList<PeripheralId> selectedTests)
	{
		var run = new TestRun
		{
			RunId = Guid.NewGuid().ToString("N"),
			Mode = RunMode.Host,
			Status = RunStatus.Queued,
			RequestedByUserId = "test",
			Configuration = new RunConfiguration(),
			SelectedTests = selectedTests,
			StartedAtUtc = DateTimeOffset.UtcNow
		};
		await _runs.SaveAsync(run);
		_cancellation.Register(run.RunId);
		return run;
	}

	/// <summary>
	/// Helper: runs orchestrator and auto-notifies verdict after each adapter completes.
	/// The orchestrator transitions to AwaitingVerdict after each probe; we poll and signal.
	/// </summary>
	private async Task ExecuteWithAutoVerdictAsync(RunOrchestrator orch, string runId)
	{
		var executeTask = Task.Run(() => orch.ExecuteAsync(runId));

		// Poll for AwaitingVerdict and auto-notify
		while (!executeTask.IsCompleted)
		{
			var run = await _runs.GetByIdAsync(runId);
			if (run?.Status == RunStatus.AwaitingVerdict)
				orch.NotifyVerdictAssigned(runId);
			await Task.Delay(10);
		}

		await executeTask;
	}

	[Fact]
	public async Task ExecuteAsync_OnlyProbesSelectedTests()
	{
		var adapters = new IHardwareAdapter[]
		{
			new ImmediateAdapter(PeripheralId.Compass),
			new ImmediateAdapter(PeripheralId.Gps),
			new ImmediateAdapter(PeripheralId.Sdr),
			new ImmediateAdapter(PeripheralId.Halow)
		};
		var orch = CreateOrchestrator(adapters);
		var run = await SeedRunAsync([PeripheralId.Gps]);

		await ExecuteWithAutoVerdictAsync(orch, run.RunId);

		var evidence = await _evidence.GetForRunAsync(run.RunId);
		Assert.Single(evidence);
		Assert.Equal(PeripheralId.Gps, evidence[0].PeripheralId);
	}

	[Fact]
	public async Task ExecuteAsync_EmptySelectedTests_RunsAll()
	{
		var adapters = new IHardwareAdapter[]
		{
			new ImmediateAdapter(PeripheralId.Compass),
			new ImmediateAdapter(PeripheralId.Gps),
		};
		var orch = CreateOrchestrator(adapters);
		var run = await SeedRunAsync([]);

		await ExecuteWithAutoVerdictAsync(orch, run.RunId);

		var evidence = await _evidence.GetForRunAsync(run.RunId);
		Assert.Equal(2, evidence.Count);
	}

	[Fact]
	public async Task ExecuteAsync_PersistsCommandLogEntries()
	{
		var adapter = new ImmediateAdapter(PeripheralId.Gps);
		var orch = CreateOrchestrator([adapter]);
		var run = await SeedRunAsync([PeripheralId.Gps]);

		await ExecuteWithAutoVerdictAsync(orch, run.RunId);

		var logs = await _commandLog.GetForRunPeripheralAsync(run.RunId, PeripheralId.Gps);
		Assert.Single(logs);
		Assert.Equal("health-check", logs[0].Command);
	}

	/// <summary>Adapter that calls reportStep once then returns immediately.</summary>
	private sealed class ImmediateAdapter : IHardwareAdapter
	{
		public PeripheralId PeripheralId { get; }
		public ImmediateAdapter(PeripheralId id) => PeripheralId = id;

		public async Task<DiagnosticEnvelope> ProbeAsync(
			RunMode mode, Func<string, string, bool, Task>? reportStep = null, CancellationToken ct = default)
		{
			if (reportStep is not null)
				await reportStep("health-check", "ok", false);

			return new DiagnosticEnvelope
			{
				PeripheralId = PeripheralId,
				Status = PeripheralStatus.Ready,
				Snapshot = new HealthSnapshot { Values = { ["poll_count"] = 1 } },
				Messages = [],
				DependencyAvailable = true,
				CapturedAtUtc = DateTimeOffset.UtcNow
			};
		}

		public Task<string?> ReadRawSampleAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
	}
}
