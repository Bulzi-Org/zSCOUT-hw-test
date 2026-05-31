using System.Text.Json;
using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Compass;

/// <summary>
/// Compass adapter: probes the QMC5883L magnetometer via compass-svc REST API (FR-004).
/// Continuously polls GET /api/heading (~1s interval) until cancellation, reporting
/// each reading via the reportStep callback for live dashboard display.
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
	/// Probes the compass via compass-svc REST API with continuous polling.
	/// Checks device status first, then polls GET /api/heading in a loop until
	/// the cancellation token fires (user stops the test).
	/// </summary>
	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, Func<string, string, bool, Task>? reportStep = null, CancellationToken ct = default)
	{
		var messages = new List<string>();

		var host = _config["Peripherals:Compass:Host"] ?? "localhost";
		var port = int.TryParse(_config["Peripherals:Compass:Port"], out var p) ? p : 5100;
		var timeoutMs = int.TryParse(_config["Peripherals:Compass:TimeoutMs"], out var t) ? t : 5_000;
		var baseUrl = $"http://{host}:{port}";

		var client = _httpClientFactory.CreateClient("CompassSvc");
		client.BaseAddress = new Uri(baseUrl);
		client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);

		try
		{
			// 1. Check device status via REST
			using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			statusCts.CancelAfter(timeoutMs);
			using var statusResponse = await client.GetAsync("/api/status", statusCts.Token);
			statusResponse.EnsureSuccessStatusCode();
			using var statusDoc = await JsonDocument.ParseAsync(await statusResponse.Content.ReadAsStreamAsync(statusCts.Token), cancellationToken: statusCts.Token);
			var statusRoot = statusDoc.RootElement;

			var available = statusRoot.TryGetProperty("deviceFound", out var availEl) && availEl.GetBoolean();
			var deviceAddress = statusRoot.TryGetProperty("deviceAddress", out var addrEl) ? addrEl.GetString() ?? "" : "";
			var bus = statusRoot.TryGetProperty("i2cBus", out var busEl) ? busEl.GetInt32().ToString() : "";
			var statusMessage = statusRoot.TryGetProperty("status", out var msgEl) ? msgEl.GetString() ?? "" : "";

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

			// 2. Continuous polling loop — poll GET /api/heading until cancelled
			var pollCount = 0;
			double lastHeading = 0, lastX = 0, lastY = 0, lastZ = 0, lastTemp = 0;
			bool lastOverflow = false;
			bool hasNonZeroData = false;

			while (!ct.IsCancellationRequested)
			{
				try
				{
					using var headingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
					headingCts.CancelAfter(timeoutMs);
					using var headingResponse = await client.GetAsync("/api/heading", headingCts.Token);
					headingResponse.EnsureSuccessStatusCode();
					using var headingDoc = await JsonDocument.ParseAsync(await headingResponse.Content.ReadAsStreamAsync(headingCts.Token), cancellationToken: headingCts.Token);
					var headingRoot = headingDoc.RootElement;

					lastHeading = headingRoot.TryGetProperty("headingDegrees", out var hdEl) ? hdEl.GetDouble() : 0.0;
					lastX = headingRoot.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 0.0;
					lastY = headingRoot.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : 0.0;
					lastZ = headingRoot.TryGetProperty("z", out var zEl) ? zEl.GetDouble() : 0.0;
					lastTemp = headingRoot.TryGetProperty("temperature", out var tempEl) ? tempEl.GetDouble() : 0.0;
					lastOverflow = headingRoot.TryGetProperty("overflow", out var ovEl) && ovEl.GetBoolean();

					pollCount++;
					if (lastX != 0 || lastY != 0 || lastZ != 0)
						hasNonZeroData = true;

					if (reportStep is not null)
						await reportStep("GET /api/heading",
							$"heading={lastHeading:F1}° x={lastX:F2} y={lastY:F2} z={lastZ:F2} temp={lastTemp:F1}°C overflow={lastOverflow}",
							false);
				}
				catch (OperationCanceledException) { break; }
				catch (Exception ex)
				{
					if (reportStep is not null)
						await reportStep("GET /api/heading", $"error: {ex.Message}", true);
				}

				try { await Task.Delay(1000, ct); }
				catch (OperationCanceledException) { break; }
			}

			var finalStatus = hasNonZeroData ? PeripheralStatus.Ready : PeripheralStatus.Degraded;

			messages.Add(hasNonZeroData
				? $"Compass PASS: {pollCount} readings, last heading={lastHeading:F1}° XYZ=({lastX:F2},{lastY:F2},{lastZ:F2}) temp={lastTemp:F1}°C"
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
						["heading_degrees"] = lastHeading,
						["x"] = lastX,
						["y"] = lastY,
						["z"] = lastZ,
						["temperature_c"] = lastTemp,
						["overflow"] = lastOverflow,
						["poll_count"] = pollCount
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
