using System.Text.Json;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Auth;

/// <summary>
/// JSON file-backed implementation of IUserStore.
/// All writes are atomic: write to temp file then rename.
/// </summary>
public sealed class UserStore : IUserStore
{
	private readonly string _filePath;
	private static readonly SemaphoreSlim _lock = new(1, 1);
	private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

	public UserStore(IConfiguration config)
	{
		var dataDir = config["DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data");
		Directory.CreateDirectory(dataDir);
		_filePath = Path.Combine(dataDir, "users.json");
	}

	public async Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken ct = default)
	{
		var all = await LoadAsync(ct);
		return all.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
	}

	public async Task<UserAccount?> FindByIdAsync(string userId, CancellationToken ct = default)
	{
		var all = await LoadAsync(ct);
		return all.FirstOrDefault(u => u.UserId == userId);
	}

	public async Task<IReadOnlyList<UserAccount>> GetAllAsync(CancellationToken ct = default)
		=> await LoadAsync(ct);

	public async Task UpsertAsync(UserAccount account, CancellationToken ct = default)
	{
		await _lock.WaitAsync(ct);
		try
		{
			var all = (await LoadAsync(ct)).ToList();
			var idx = all.FindIndex(u => u.UserId == account.UserId);
			if (idx >= 0) all[idx] = account;
			else all.Add(account);
			await SaveAsync(all, ct);
		}
		finally
		{
			_lock.Release();
		}
	}

	private async Task<List<UserAccount>> LoadAsync(CancellationToken ct)
	{
		if (!File.Exists(_filePath)) return [];
		await using var fs = File.OpenRead(_filePath);
		return await JsonSerializer.DeserializeAsync<List<UserAccount>>(fs, _json, ct) ?? [];
	}

	private async Task SaveAsync(List<UserAccount> accounts, CancellationToken ct)
	{
		var tmp = _filePath + ".tmp";
		await using (var fs = File.Create(tmp))
			await JsonSerializer.SerializeAsync(fs, accounts, _json, ct);
		File.Move(tmp, _filePath, overwrite: true);
	}
}
