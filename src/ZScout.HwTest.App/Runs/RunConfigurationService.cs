using System.Text.Json;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Runs;

/// <summary>
/// Persists and provides the user's preferred default run configuration.
/// Singleton — backed by data/run-config.json with atomic writes.
/// </summary>
public sealed class RunConfigurationService
{
	private readonly string _filePath;
	private readonly SemaphoreSlim _lock = new(1, 1);
	private static readonly JsonSerializerOptions JsonOpts =
		new() { WriteIndented = true };

	public RunConfigurationService(IConfiguration config)
	{
		var dataDir = config["DataDirectory"] ?? "data";
		Directory.CreateDirectory(dataDir);
		_filePath = Path.Combine(dataDir, "run-config.json");
	}

	public async Task<RunConfiguration> GetAsync(CancellationToken ct = default)
	{
		await _lock.WaitAsync(ct);
		try
		{
			if (!File.Exists(_filePath))
				return new RunConfiguration();

			var json = await File.ReadAllTextAsync(_filePath, ct);
			return JsonSerializer.Deserialize<RunConfiguration>(json, JsonOpts)
				   ?? new RunConfiguration();
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task SaveAsync(RunConfiguration config, CancellationToken ct = default)
	{
		await _lock.WaitAsync(ct);
		try
		{
			var tmp = _filePath + ".tmp";
			var json = JsonSerializer.Serialize(config, JsonOpts);
			await File.WriteAllTextAsync(tmp, json, ct);
			File.Move(tmp, _filePath, overwrite: true);
		}
		finally
		{
			_lock.Release();
		}
	}
}
