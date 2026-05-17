using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Common;

/// <summary>
/// Contract all peripheral adapters must implement.
/// Each adapter probes its hardware and returns a DiagnosticEnvelope.
/// </summary>
public interface IHardwareAdapter
{
	PeripheralId PeripheralId { get; }

	/// <summary>
	/// Run a communication probe and return a diagnostic result.
	/// Must not throw — exceptions are caught by the orchestrator and treated as adapter failures.
	/// The optional <paramref name="reportStep"/> callback receives (command, output, isError)
	/// after each shell command to enable live progress reporting.
	/// </summary>
	Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, Func<string, string, bool, Task>? reportStep = null, CancellationToken ct = default);

	/// <summary>
	/// Stream a single raw sample for live telemetry display.
	/// Returns null if no sample is available at this time.
	/// </summary>
	Task<string?> ReadRawSampleAsync(CancellationToken ct = default);
}
