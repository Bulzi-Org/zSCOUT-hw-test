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
	/// </summary>
	Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, CancellationToken ct = default);

	/// <summary>
	/// Stream a single raw sample for live telemetry display.
	/// Returns null if no sample is available at this time.
	/// </summary>
	Task<string?> ReadRawSampleAsync(CancellationToken ct = default);
}
