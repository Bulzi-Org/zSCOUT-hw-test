using ZScout.HwTest.App.Hardware.Common;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Compass;

/// <summary>
/// Compass adapter: probes the QMC5883L magnetometer via I2C.
/// Uses i2cdetect to confirm device presence at the expected I2C address (0x0d).
/// Uses i2cget to read one bearing register as proof of communication.
/// Container mode: requires /dev/i2c-* device pass-through.
/// </summary>
public sealed class CompassAdapter : IHardwareAdapter
{
	public PeripheralId PeripheralId => PeripheralId.Compass;

	// QMC5883L default I2C address
	private const string ExpectedAddress = "0x0d";

	private readonly ILogger<CompassAdapter> _logger;
	private readonly IConfiguration _config;

	public CompassAdapter(ILogger<CompassAdapter> logger, IConfiguration config)
	{
		_logger = logger;
		_config = config;
	}

	public async Task<DiagnosticEnvelope> ProbeAsync(RunMode mode, CancellationToken ct = default)
	{
		var messages = new List<string>();

		// Determine I2C bus (default i2c-1 on CM5, configurable via appsettings)
		var bus = _config["Peripherals:Compass:I2cBus"] ?? "1";
		var busPath = $"/dev/i2c-{bus}";

		// 1. Verify i2c bus device exists
		if (!File.Exists(busPath))
		{
			messages.Add($"I2C bus device {busPath} not found. Is i2c enabled in /boot/config.txt?");
			return DiagnosticEnvelope.Unavailable(PeripheralId, $"I2C bus {busPath} not available");
		}
		messages.Add($"I2C bus device {busPath} present");

		// 2. Scan for QMC5883L at expected address
		var detectResult = await ProcessHelper.RunAsync(
			"i2cdetect", $"-y {bus}", 5_000, ct);

		var deviceFound = detectResult.ExitCode == 0 &&
						  detectResult.Stdout.Contains(
							  ExpectedAddress.Replace("0x", "").TrimStart('0'),
							  StringComparison.OrdinalIgnoreCase);

		messages.Add(deviceFound
			? $"QMC5883L detected at I2C address {ExpectedAddress} on bus {bus}"
			: $"QMC5883L not found at {ExpectedAddress} on i2c-{bus}");

		if (!deviceFound)
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
						["bus"] = bus,
						["expected_address"] = ExpectedAddress,
						["device_found"] = false
					}
				},
				CapturedAtUtc = DateTimeOffset.UtcNow
			};

		// 3. Read status register (0x06) as proof of communication
		var readResult = await ProcessHelper.RunAsync(
			"i2cget", $"-y {bus} {ExpectedAddress} 0x06", 5_000, ct);

		var registerRead = readResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(readResult.Stdout);
		var registerValue = readResult.Stdout.Trim();

		messages.Add(registerRead
			? $"Register 0x06 read successfully: {registerValue}"
			: $"i2cget failed (exit {readResult.ExitCode}): {readResult.Stderr.Trim()}");

		return new DiagnosticEnvelope
		{
			PeripheralId = PeripheralId,
			DependencyAvailable = true,
			Status = registerRead ? PeripheralStatus.Ready : PeripheralStatus.Degraded,
			Messages = messages,
			Snapshot = new HealthSnapshot
			{
				Values = new Dictionary<string, object?>
				{
					["bus"] = bus,
					["expected_address"] = ExpectedAddress,
					["device_found"] = deviceFound,
					["register_0x06"] = registerValue
				}
			},
			CapturedAtUtc = DateTimeOffset.UtcNow
		};
	}

	public async Task<string?> ReadRawSampleAsync(CancellationToken ct = default)
	{
		var bus = _config["Peripherals:Compass:I2cBus"] ?? "1";
		// Read X-axis LSB (0x00) for a live heading sample
		var result = await ProcessHelper.RunAsync(
			"i2cget", $"-y {bus} {ExpectedAddress} 0x00", 3_000, ct);
		return result.ExitCode == 0 ? $"x_lsb={result.Stdout.Trim()}" : null;
	}
}
