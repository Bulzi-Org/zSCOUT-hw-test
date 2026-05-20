using System.Text.Json;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Compass;

/// <summary>
/// Compass adapter: probes the QMC5883L magnetometer via compass-svc REST API (FR-004).
/// Connects to compass-svc on configurable host:port (default localhost:5100) instead
/// of directly accessing the I2C bus. Eliminates the need for /dev/i2c-* device
/// pass-through and --privileged mode.
/// T017: Heading, XYZ, temperature, overflow data via HTTP REST.
/// </summary>
public sealed class CompassAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Compass;

	private readonly ILogger<CompassAdapter> _logger;
	private readonly IConfiguration _config;
	private readonly IHttpClientFactory _httpClientFactory;

	public CompassAdapter(ILogger<CompassAdapter> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
	{
		_logger = logger;
		_config = config;
		_httpClientFactory = httpClientFactory;
	}

	/// <summary>
	/// Probes the compass via compass-svc REST API (FR-004, FR-005).
	/// Returns Ready with heading data on success, Unavailable if service is not reachable.
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, Func<string, string, bool, Task>? reportStep = null, CancellationToken ct = default)
	{
		var messages = new List<string>();

		// Resolve compass-svc endpoint from configuration (FR-004, FR-013)
		var host = _config["Peripherals:Compass:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Compass:Port"], out var p) ? p : 5100;
		var timeoutMs = int.TryParse(_config["Peripherals:Compass:TimeoutMs"], out var t) ? t : 5_000;
		var baseUrl = $"http://{host}:{port}";

		var client = _httpClientFactory.CreateClient("CompassSvc");
		client.BaseAddress = new Uri(baseUrl);
		client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);

		try
		{
			// 1. Check device status via REST (FR-018)
			using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			statusCts.CancelAfter(timeoutMs);
			using var statusResponse = await client.GetAsync("/api/status", statusCts.Token);
			statusResponse.EnsureSuccessStatusCode();
			using var statusDoc = await JsonDocument.ParseAsync(await statusResponse.Content.ReadAsStreamAsync(statusCts.Token), cancellationToken: statusCts.Token);
			var statusRoot = statusDoc.RootElement;

			var available = statusRoot.TryGetProperty("available", out var availEl) && availEl.GetBoolean();
			var deviceAddress = statusRoot.TryGetProperty("deviceAddress", out var addrEl) ? addrEl.GetString() ?? "" : "";
			var bus = statusRoot.TryGetProperty("bus", out var busEl) ? busEl.GetString() ?? "" : "";
			var statusMessage = statusRoot.TryGetProperty("statusMessage", out var msgEl) ? msgEl.GetString() ?? "" : "";

			if (reportStep is not null)
				await reportStep("GET /api/status", $"available={available} addr={deviceAddress} bus={bus}", !available);

			messages.Add($"compass-svc reachable on {host}:{port}");
			_logger.LogInformation("Compass probe: compass-svc reachable on {Host}:{Port}", host, port);

			if (!available)
			{
				messages.Add($"compass-svc reports device unavailable: {statusMessage}");
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
							["status_message"] = statusMessage
						}
					},
					CapturedAtUtc = DateTimeOffset.UtcNow
				};
			}

			messages.Add($"Device available at {deviceAddress} on bus {bus}");

			// 2. Get heading data via REST (FR-005)
			using var headingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			headingCts.CancelAfter(timeoutMs);
			using var headingResponse = await client.GetAsync("/api/heading", headingCts.Token);
			headingResponse.EnsureSuccessStatusCode();
			using var headingDoc = await JsonDocument.ParseAsync(await headingResponse.Content.ReadAsStreamAsync(headingCts.Token), cancellationToken: headingCts.Token);
			var headingRoot = headingDoc.RootElement;

			var headingDegrees = headingRoot.TryGetProperty("headingDegrees", out var hdEl) ? hdEl.GetDouble() : 0.0;
			var x = headingRoot.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 0.0;
			var y = headingRoot.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : 0.0;
			var z = headingRoot.TryGetProperty("z", out var zEl) ? zEl.GetDouble() : 0.0;
			var temperatureC = headingRoot.TryGetProperty("temperatureC", out var tempEl) ? tempEl.GetDouble() : 0.0;
			var overflow = headingRoot.TryGetProperty("overflow", out var ovEl) && ovEl.GetBoolean();

			if (reportStep is not null)
				await reportStep("GET /api/heading", $"heading={headingDegrees:F1}° x={x:F2} y={y:F2} z={z:F2}", false);

			var hasNonZeroData = x != 0 || y != 0 || z != 0;
			var finalStatus = hasNonZeroData ? PeripheralStatus.Ready : PeripheralStatus.Degraded;

			messages.Add(hasNonZeroData
				? $"Compass PASS: heading={headingDegrees:F1}° XYZ=({x:F2},{y:F2},{z:F2}) temp={temperatureC:F1}°C"
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
						["device_address"] = deviceAddress,
						["bus"] = bus,
						["heading_degrees"] = headingDegrees,
						["x"] = x,
						["y"] = y,
						["z"] = z,
						["temperature_c"] = temperatureC,
						["overflow"] = overflow
					}
				},
				CapturedAtUtc = DateTimeOffset.UtcNow
			};
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Compass probe: REST call to compass-svc failed");
			messages.Add($"REST error: {ex.GetType().Name}: {ex.Message}");
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
						["rest_error"] = ex.Message
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
			var client = _httpClientFactory.CreateClient("CompassSvc");
			client.BaseAddress = new Uri($"http://{host}:{port}");
			client.Timeout = TimeSpan.FromSeconds(3);
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(3_000);
			using var response = await client.GetAsync("/api/heading", cts.Token);
			response.EnsureSuccessStatusCode();
			using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
			var root = doc.RootElement;
			var hd = root.TryGetProperty("headingDegrees", out var hdEl) ? hdEl.GetDouble() : 0.0;
			var x = root.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 0.0;
			var y = root.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : 0.0;
			var z = root.TryGetProperty("z", out var zEl) ? zEl.GetDouble() : 0.0;
			return $"heading={hd:F1}° x={x:F2} y={y:F2} z={z:F2}";
		}
		catch
		{
			return null;
		}
	}
}
