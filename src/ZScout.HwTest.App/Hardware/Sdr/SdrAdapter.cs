using Grpc.Net.Client;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Protos;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Sdr;

/// <summary>
/// SDR adapter: probes the Wavelet-Lab uSDR via sdr-svc gRPC (FR-006).
/// Connects to sdr-svc on configurable host:port (default localhost:5101) instead
/// of calling SoapySDRUtil directly. Eliminates the need for USB device
/// pass-through and SoapySDR tools in the container.
/// T018: Device status, capabilities, and band sweep via gRPC.
/// </summary>
public sealed class SdrAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Sdr;

	private readonly ILogger<SdrAdapter> _logger;
	private readonly IConfiguration _config;

	public SdrAdapter(ILogger<SdrAdapter> logger, IConfiguration config)
	{
		_logger = logger;
		_config = config;
	}

	/// <summary>
	/// Probes the SDR via sdr-svc gRPC API (FR-006, FR-007).
	/// Returns Ready with device info on success, Unavailable if service is not reachable.
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, Func<string, string, bool, Task>? reportStep = null, CancellationToken ct = default)
	{
		var messages = new List<string>();

		// Resolve sdr-svc endpoint from configuration (FR-006, FR-013)
		var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;
		var timeoutMs = int.TryParse(_config["Peripherals:Sdr:TimeoutMs"], out var t) ? t : 5_000;

		// 1. Check gRPC service reachability via TCP (FR-018)
		var reachable = await TcpHealthCheck.CheckAsync(host, port, timeoutMs, ct);
		if (reportStep is not null)
			await reportStep($"TCP connect {host}:{port}", reachable ? "connected" : "unreachable", !reachable);

		if (!reachable)
		{
			_logger.LogWarning("SDR probe: sdr-svc not reachable on {Host}:{Port}", host, port);
			return DiagnosticEnvelope.Unavailable(PeripheralId, $"sdr-svc not reachable on {host}:{port}");
		}

		messages.Add($"sdr-svc reachable on {host}:{port}");
		_logger.LogInformation("SDR probe: sdr-svc reachable on {Host}:{Port}", host, port);

		// 2. Query sdr-svc via gRPC
		using var channel = GrpcChannelFactory.Create(host, port);
		var client = new SdrService.SdrServiceClient(channel);

		try
		{
			// Check device status
			using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			statusCts.CancelAfter(timeoutMs);
			var status = await client.GetStatusAsync(new SdrStatusRequest(), cancellationToken: statusCts.Token);
			if (reportStep is not null)
				await reportStep("SdrService.GetStatus", $"available={status.Available} driver={status.Driver} label={status.DeviceLabel}", !status.Available);

			if (!status.Available)
			{
				messages.Add($"sdr-svc reports device unavailable: {status.StatusMessage}");
				return new DiagnosticEnvelope
				{
					PeripheralId = PeripheralId,
					DependencyAvailable = true,
					Status = PeripheralStatus.Unavailable,
					Messages = messages,
					Snapshot = new HealthSnapshot
					{
						Values = new Dictionary<string, object?>
						{
							["service_available"] = true,
							["device_found"] = false,
							["status_message"] = status.StatusMessage
						}
					},
					CapturedAtUtc = DateTimeOffset.UtcNow
				};
			}

			messages.Add($"SDR device found: {status.Driver} — {status.DeviceLabel}");

			// Get capabilities (FR-007)
			using var capsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			capsCts.CancelAfter(timeoutMs);
			var caps = await client.GetCapabilitiesAsync(new SdrCapabilitiesRequest(), cancellationToken: capsCts.Token);
			if (reportStep is not null)
				await reportStep("SdrService.GetCapabilities", $"freq={caps.MinFrequencyHz / 1e6:F1}-{caps.MaxFrequencyHz / 1e6:F1}MHz gain={caps.MinGainDb:F0}-{caps.MaxGainDb:F0}dB", false);

			messages.Add($"SDR PASS: {status.Driver} tuning {caps.MinFrequencyHz / 1e6:F1}-{caps.MaxFrequencyHz / 1e6:F1} MHz, gain {caps.MinGainDb:F0}-{caps.MaxGainDb:F0} dB");

			return new DiagnosticEnvelope
			{
				PeripheralId = PeripheralId,
				DependencyAvailable = true,
				Status = PeripheralStatus.Ready,
				Messages = messages,
				Snapshot = new HealthSnapshot
				{
					Values = new Dictionary<string, object?>
					{
						["service_available"] = true,
						["device_found"] = true,
						["driver"] = status.Driver,
						["device_label"] = status.DeviceLabel,
						["serial"] = status.Serial,
						["min_frequency_hz"] = caps.MinFrequencyHz,
						["max_frequency_hz"] = caps.MaxFrequencyHz,
						["min_gain_db"] = caps.MinGainDb,
						["max_gain_db"] = caps.MaxGainDb
					}
				},
				CapturedAtUtc = DateTimeOffset.UtcNow
			};
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "SDR probe: gRPC call to sdr-svc failed");
			messages.Add($"gRPC error: {ex.GetType().Name}: {ex.Message}");
			return new DiagnosticEnvelope
			{
				PeripheralId = PeripheralId,
				DependencyAvailable = true,
				Status = PeripheralStatus.Degraded,
				Messages = messages,
				Snapshot = new HealthSnapshot
				{
					Values = new Dictionary<string, object?>
					{
						["service_available"] = true,
						["grpc_error"] = ex.Message
					}
				},
				CapturedAtUtc = DateTimeOffset.UtcNow
			};
		}
	}

	/// <summary>
	/// Returns a summary of the device status from sdr-svc.
	/// </summary>
	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;

		try
		{
			using var channel = GrpcChannelFactory.Create(host, port);
			var client = new SdrService.SdrServiceClient(channel);
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(3_000);
			var status = await client.GetStatusAsync(new SdrStatusRequest(), cancellationToken: cts.Token);
			return $"driver={status.Driver} label={status.DeviceLabel} available={status.Available}";
		}
		catch
		{
			return null;
		}
	}
}
