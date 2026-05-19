using Grpc.Net.Client;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.App.Protos;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Compass;

/// <summary>
/// Compass adapter: probes the QMC5883L magnetometer via compass-svc gRPC (FR-004).
/// Connects to compass-svc on configurable host:port (default localhost:5100) instead
/// of directly accessing the I2C bus. Eliminates the need for /dev/i2c-* device
/// pass-through and --privileged mode.
/// T017: Heading, XYZ, temperature, overflow data via gRPC.
/// </summary>
public sealed class CompassAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Compass;

	private readonly ILogger<CompassAdapter> _logger;
	private readonly IConfiguration _config;

	public CompassAdapter(ILogger<CompassAdapter> logger, IConfiguration config)
	{
		_logger = logger;
		_config = config;
	}

	/// <summary>
	/// Probes the compass via compass-svc gRPC API (FR-004, FR-005).
	/// Returns Ready with heading data on success, Unavailable if service is not reachable.
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, Func<string, string, bool, Task>? reportStep = null, CancellationToken ct = default)
	{
		var messages = new List<string>();

		// Resolve compass-svc endpoint from configuration (FR-004, FR-013)
		var host = _config["Peripherals:Compass:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Compass:Port"], out var p) ? p : 5100;
		var timeoutMs = int.TryParse(_config["Peripherals:Compass:TimeoutMs"], out var t) ? t : 5_000;

		// 1. Check gRPC service reachability via TCP (FR-018)
		var reachable = await TcpHealthCheck.CheckAsync(host, port, timeoutMs, ct);
		if (reportStep is not null)
			await reportStep($"TCP connect {host}:{port}", reachable ? "connected" : "unreachable", !reachable);

		if (!reachable)
		{
			_logger.LogWarning("Compass probe: compass-svc not reachable on {Host}:{Port}", host, port);
			return DiagnosticEnvelope.Unavailable(PeripheralId, $"compass-svc not reachable on {host}:{port}");
		}

		messages.Add($"compass-svc reachable on {host}:{port}");
		_logger.LogInformation("Compass probe: compass-svc reachable on {Host}:{Port}", host, port);

		// 2. Query compass-svc via gRPC
		using var channel = GrpcChannelFactory.Create(host, port);
		var client = new CompassService.CompassServiceClient(channel);

		try
		{
			// Check device status
			using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			statusCts.CancelAfter(timeoutMs);
			var status = await client.GetStatusAsync(new CompassStatusRequest(), cancellationToken: statusCts.Token);
			if (reportStep is not null)
				await reportStep("CompassService.GetStatus", $"available={status.Available} addr={status.DeviceAddress} bus={status.Bus}", !status.Available);

			if (!status.Available)
			{
				messages.Add($"compass-svc reports device unavailable: {status.StatusMessage}");
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
							["device_available"] = false,
							["status_message"] = status.StatusMessage
						}
					},
					CapturedAtUtc = DateTimeOffset.UtcNow
				};
			}

			messages.Add($"Device available at {status.DeviceAddress} on bus {status.Bus}");

			// Get heading data (FR-005)
			using var headingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			headingCts.CancelAfter(timeoutMs);
			var heading = await client.GetHeadingAsync(new CompassHeadingRequest(), cancellationToken: headingCts.Token);
			if (reportStep is not null)
				await reportStep("CompassService.GetHeading", $"heading={heading.HeadingDegrees:F1}° x={heading.X:F2} y={heading.Y:F2} z={heading.Z:F2}", false);

			var hasNonZeroData = heading.X != 0 || heading.Y != 0 || heading.Z != 0;
			var finalStatus = hasNonZeroData ? PeripheralStatus.Ready : PeripheralStatus.Degraded;

			messages.Add(hasNonZeroData
				? $"Compass PASS: heading={heading.HeadingDegrees:F1}° XYZ=({heading.X:F2},{heading.Y:F2},{heading.Z:F2}) temp={heading.TemperatureC:F1}°C"
				: "Compass FAIL: all magnetometer axes report zero — sensor may be malfunctioning");

			return new DiagnosticEnvelope
			{
				PeripheralId = PeripheralId,
				DependencyAvailable = true,
				Status = finalStatus,
				Messages = messages,
				Snapshot = new HealthSnapshot
				{
					Values = new Dictionary<string, object?>
					{
						["service_available"] = true,
						["device_available"] = true,
						["device_address"] = status.DeviceAddress,
						["bus"] = status.Bus,
						["heading_degrees"] = heading.HeadingDegrees,
						["x"] = heading.X,
						["y"] = heading.Y,
						["z"] = heading.Z,
						["temperature_c"] = heading.TemperatureC,
						["overflow"] = heading.Overflow
					}
				},
				CapturedAtUtc = DateTimeOffset.UtcNow
			};
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Compass probe: gRPC call to compass-svc failed");
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
	/// Returns a summary of the latest heading reading from compass-svc.
	/// </summary>
	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var host = _config["Peripherals:Compass:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Compass:Port"], out var p) ? p : 5100;

		try
		{
			using var channel = GrpcChannelFactory.Create(host, port);
			var client = new CompassService.CompassServiceClient(channel);
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(3_000);
			var heading = await client.GetHeadingAsync(new CompassHeadingRequest(), cancellationToken: cts.Token);
			return $"heading={heading.HeadingDegrees:F1}° x={heading.X:F2} y={heading.Y:F2} z={heading.Z:F2}";
		}
		catch
		{
			return null;
		}
	}
}
