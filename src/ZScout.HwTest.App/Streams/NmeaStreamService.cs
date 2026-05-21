namespace ZScout.HwTest.App.Streams;

/// <summary>
/// Singleton service that connects to gps-svc <c>GET /api/stream/nmea</c> SSE endpoint
/// and relays raw NMEA sentences through <see cref="LiveEventPublisher"/>.
/// Reference-counted: the first subscriber starts the stream, the last unsubscriber stops it.
/// </summary>
public sealed class NmeaStreamService : IDisposable
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IConfiguration _config;
	private readonly ILogger<NmeaStreamService> _logger;
	private readonly LiveEventPublisher _events;

	private readonly Lock _lock = new();
	private int _subscriberCount;
	private CancellationTokenSource? _cts;
	private Task? _readTask;
	private bool _connected;

	public NmeaStreamService(
		IHttpClientFactory httpClientFactory,
		IConfiguration config,
		ILogger<NmeaStreamService> logger,
		LiveEventPublisher events)
	{
		_httpClientFactory = httpClientFactory;
		_config = config;
		_logger = logger;
		_events = events;
	}

	/// <summary>Whether the SSE stream is currently connected.</summary>
	public bool IsConnected
	{
		get { lock (_lock) { return _connected; } }
	}

	/// <summary>
	/// Increments the subscriber count. Starts the SSE reader on the first subscriber.
	/// </summary>
	public void Subscribe()
	{
		lock (_lock)
		{
			_subscriberCount++;
			if (_subscriberCount == 1)
				StartReading();
		}
	}

	/// <summary>
	/// Decrements the subscriber count. Stops the SSE reader when the last subscriber leaves.
	/// </summary>
	public void Unsubscribe()
	{
		lock (_lock)
		{
			_subscriberCount = Math.Max(0, _subscriberCount - 1);
			if (_subscriberCount == 0)
				StopReading();
		}
	}

	private void StartReading()
	{
		_cts = new CancellationTokenSource();
		var ct = _cts.Token;
		_readTask = Task.Run(() => ReadLoopAsync(ct), ct);
	}

	private void StopReading()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;
		_readTask = null;
		SetConnected(false);
	}

	private async Task ReadLoopAsync(CancellationToken ct)
	{
		var host = _config["Peripherals:Gps:Host"] ?? "localhost";
		var restPort = int.TryParse(_config["Peripherals:Gps:RestPort"], out var rp) ? rp : 5200;
		var baseUri = $"http://{host}:{restPort}";

		_logger.LogInformation("NMEA stream: connecting to {BaseUri}/api/stream/nmea", baseUri);

		while (!ct.IsCancellationRequested)
		{
			try
			{
				var client = _httpClientFactory.CreateClient("NmeaSse");
				client.BaseAddress = new Uri(baseUri);
				client.Timeout = Timeout.InfiniteTimeSpan;

				using var stream = await client.GetStreamAsync("/api/stream/nmea", ct);
				using var reader = new StreamReader(stream);

				SetConnected(true);
				_logger.LogInformation("NMEA stream: connected to {BaseUri}/api/stream/nmea", baseUri);

				while (!ct.IsCancellationRequested)
				{
					var line = await reader.ReadLineAsync(ct);
					if (line is null) break; // stream closed

					// SSE format: lines prefixed with "data:"
					if (!line.StartsWith("data:", StringComparison.Ordinal))
						continue;

					var sentence = line["data:".Length..].Trim();
					if (string.IsNullOrEmpty(sentence))
						continue;

					_events.PublishNmeaSentence(sentence);
				}

				// Stream ended normally
				SetConnected(false);
				_logger.LogWarning("NMEA stream: SSE connection closed by server");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				// Normal shutdown
				break;
			}
			catch (Exception ex)
			{
				SetConnected(false);
				_logger.LogWarning(ex, "NMEA stream: connection failed, retrying in 3s");

				try { await Task.Delay(3_000, ct); }
				catch (OperationCanceledException) { break; }
			}
		}

		SetConnected(false);
	}

	private void SetConnected(bool connected)
	{
		bool changed;
		lock (_lock)
		{
			changed = _connected != connected;
			_connected = connected;
		}
		if (changed)
			_events.PublishNmeaConnectionState(connected);
	}

	public void Dispose()
	{
		_cts?.Cancel();
		_cts?.Dispose();
	}
}
