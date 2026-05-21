using System.Text.Json;
using ZScout.HwTest.App.Hardware.Gps;

namespace ZScout.HwTest.App.Streams;

/// <summary>
/// Background service that connects to the gps-svc SSE endpoint (<c>GET /api/stream/fixes</c>),
/// deserializes incoming <see cref="GpsFix"/> JSON objects, and broadcasts them to Blazor clients
/// via <see cref="LiveEventPublisher"/>. Reconnects with exponential backoff on failure.
/// </summary>
public sealed class GpsFixStreamService : BackgroundService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private const int InitialBackoffMs = 2_000;
	private const int MaxBackoffMs = 30_000;

	private readonly IConfiguration _config;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly LiveEventPublisher _publisher;
	private readonly ILogger<GpsFixStreamService> _logger;

	public GpsFixStreamService(
		IConfiguration config,
		IHttpClientFactory httpClientFactory,
		LiveEventPublisher publisher,
		ILogger<GpsFixStreamService> logger)
	{
		_config = config;
		_httpClientFactory = httpClientFactory;
		_publisher = publisher;
		_logger = logger;
	}

	/// <summary>
	/// Main execution loop: connects to the SSE stream and relays fixes.
	/// Retries with exponential backoff on any failure.
	/// </summary>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var backoffMs = InitialBackoffMs;

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await StreamFixesAsync(stoppingToken);
				// Stream ended cleanly (server closed) — reset backoff and reconnect
				backoffMs = InitialBackoffMs;
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				// Normal shutdown
				break;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "GPS fix stream disconnected, retrying in {BackoffMs}ms", backoffMs);
			}

			if (stoppingToken.IsCancellationRequested)
				break;

			// Exponential backoff with jitter
			var jitter = Random.Shared.Next(0, backoffMs / 4);
			await Task.Delay(backoffMs + jitter, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			backoffMs = Math.Min(backoffMs * 2, MaxBackoffMs);
		}

		_logger.LogInformation("GPS fix stream service stopped");
	}

	private async Task StreamFixesAsync(CancellationToken ct)
	{
		var host = _config["Peripherals:Gps:Host"] ?? "localhost";
		var restPort = int.TryParse(_config["Peripherals:Gps:RestPort"], out var rp) ? rp : 5200;

		_logger.LogInformation("Connecting to GPS fix stream on {Host}:{Port}", host, restPort);

		var client = _httpClientFactory.CreateClient("GpsSvcStream");
		client.BaseAddress = new Uri($"http://{host}:{restPort}");
		client.Timeout = Timeout.InfiniteTimeSpan; // SSE is long-lived

		using var stream = await client.GetStreamAsync("/api/stream/fixes", ct);
		using var reader = new StreamReader(stream);

		_logger.LogInformation("GPS fix stream connected on {Host}:{Port}", host, restPort);

		while (!ct.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(ct);
			if (line is null)
				break; // Stream closed by server

			// SSE format: lines prefixed with "data:"
			if (!line.StartsWith("data:", StringComparison.Ordinal))
				continue;

			var json = line["data:".Length..].Trim();
			if (string.IsNullOrEmpty(json))
				continue;

			GpsFix? fix;
			try
			{
				fix = JsonSerializer.Deserialize<GpsFix>(json, JsonOptions);
			}
			catch (JsonException ex)
			{
				_logger.LogDebug(ex, "Skipping malformed GPS fix JSON");
				continue;
			}

			if (fix is null)
				continue;

			await _publisher.PublishGpsFixAsync(fix, ct);
		}
	}
}
