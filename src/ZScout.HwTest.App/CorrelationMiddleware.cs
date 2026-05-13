namespace ZScout.HwTest.App;

/// <summary>
/// Reads or generates an X-Correlation-ID for each HTTP request, logs it as a
/// structured property, and echoes it back in the response header.
/// T040: Correlation ID support across run lifecycle and API requests.
/// </summary>
public sealed class CorrelationMiddleware
{
	private const string HeaderName = "X-Correlation-ID";
	private readonly RequestDelegate _next;
	private readonly ILogger<CorrelationMiddleware> _logger;

	public CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
	{
		_next = next;
		_logger = logger;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
			?? Guid.NewGuid().ToString("N");

		context.Items["CorrelationId"] = correlationId;
		context.Response.OnStarting(() =>
		{
			context.Response.Headers[HeaderName] = correlationId;
			return Task.CompletedTask;
		});

		using (_logger.BeginScope(new Dictionary<string, object>
		{
			["CorrelationId"] = correlationId,
			["RequestPath"] = context.Request.Path.Value ?? ""
		}))
		{
			await _next(context);
		}
	}
}
