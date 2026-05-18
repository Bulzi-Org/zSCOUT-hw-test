using System.Collections.Concurrent;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Singleton service that maintains a <see cref="CancellationTokenSource"/> per active run.
/// Allows <c>StopRunAsync</c> to propagate cancellation to all running hardware adapters
/// via the <see cref="CancellationToken"/> passed to <c>RunOrchestrator.ExecuteAsync</c>.
/// </summary>
public sealed class RunCancellationService
{
	private readonly ConcurrentDictionary<string, CancellationTokenSource> _registry = new();

	/// <summary>
	/// Registers a new <see cref="CancellationTokenSource"/> for the given run.
	/// Returns the associated <see cref="CancellationToken"/> to pass to the orchestrator.
	/// </summary>
	public CancellationToken Register(string runId)
	{
		var cts = new CancellationTokenSource();
		_registry[runId] = cts;
		return cts.Token;
	}

	/// <summary>
	/// Cancels and removes the <see cref="CancellationTokenSource"/> for the given run.
	/// Called by <c>StopRunAsync</c> to terminate all in-flight adapter probes.
	/// </summary>
	public void Cancel(string runId)
	{
		if (_registry.TryRemove(runId, out var cts))
		{
			cts.Cancel();
			cts.Dispose();
		}
	}

	/// <summary>
	/// Removes the <see cref="CancellationTokenSource"/> for the given run without cancelling it.
	/// Called by the orchestrator after all probes complete normally.
	/// </summary>
	public void Unregister(string runId)
	{
		if (_registry.TryRemove(runId, out var cts))
			cts.Dispose();
	}
}
