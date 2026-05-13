using ZScout.HwTest.App.Persistence;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Streams;

/// <summary>
/// T032: Append-only writer for per-run, per-peripheral telemetry stream records.
/// Detects and tags malformed payloads (empty or unprintable content).
/// </summary>
public sealed class TelemetryStreamWriter
{
	private readonly TelemetryStreamRepository _repo;
	private readonly ILogger<TelemetryStreamWriter> _logger;

	public TelemetryStreamWriter(TelemetryStreamRepository repo, ILogger<TelemetryStreamWriter> logger)
	{
		_repo = repo;
		_logger = logger;
	}

	/// <summary>
	/// Appends a telemetry sample to the store.
	/// Marks the record as malformed if the payload is null/empty or contains only non-printable characters.
	/// </summary>
	public async Task WriteAsync(
		string runId,
		PeripheralId peripheralId,
		StreamType streamType,
		string? rawPayload,
		CancellationToken ct = default)
	{
		var isMalformed = string.IsNullOrWhiteSpace(rawPayload)
			|| rawPayload.All(c => c < 0x20 && c != '\n' && c != '\r' && c != '\t');

		var record = new TelemetryStreamRecord
		{
			StreamRecordId = Guid.NewGuid().ToString("N"),
			RunId = runId,
			PeripheralId = peripheralId,
			StreamType = streamType,
			TimestampUtc = DateTimeOffset.UtcNow,
			Payload = rawPayload ?? string.Empty,
			IsMalformed = isMalformed
		};

		if (isMalformed)
			_logger.LogWarning("Malformed telemetry from {Peripheral} in run {RunId}", peripheralId, runId);

		await _repo.SaveAsync(record, ct);
	}
}
