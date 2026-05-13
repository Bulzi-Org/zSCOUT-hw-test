using System.Text.Json;

namespace ZScout.HwTest.App.Persistence;

/// <summary>
/// Generic JSON file-backed repository.
/// Each entity type is stored in its own JSON file as a list.
/// Writes are atomic (tmp + rename).
/// </summary>
public abstract class FileBackedRepository<T> : IRepository<T> where T : class
{
	private readonly string _filePath;
	private readonly SemaphoreSlim _lock = new(1, 1);
	private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

	protected FileBackedRepository(string dataDirectory, string fileName)
	{
		Directory.CreateDirectory(dataDirectory);
		_filePath = Path.Combine(dataDirectory, fileName);
	}

	protected abstract string GetId(T entity);

	public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
	{
		var all = await LoadAsync(ct);
		return all.FirstOrDefault(e => GetId(e) == id);
	}

	public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
		=> await LoadAsync(ct);

	public async Task SaveAsync(T entity, CancellationToken ct = default)
	{
		await _lock.WaitAsync(ct);
		try
		{
			var all = (await LoadAsync(ct)).ToList();
			var id = GetId(entity);
			var idx = all.FindIndex(e => GetId(e) == id);
			if (idx >= 0) all[idx] = entity;
			else all.Add(entity);
			await PersistAsync(all, ct);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task DeleteAsync(string id, CancellationToken ct = default)
	{
		await _lock.WaitAsync(ct);
		try
		{
			var all = (await LoadAsync(ct)).Where(e => GetId(e) != id).ToList();
			await PersistAsync(all, ct);
		}
		finally
		{
			_lock.Release();
		}
	}

	private async Task<List<T>> LoadAsync(CancellationToken ct)
	{
		if (!File.Exists(_filePath)) return [];
		await using var fs = File.OpenRead(_filePath);
		return await JsonSerializer.DeserializeAsync<List<T>>(fs, _json, ct) ?? [];
	}

	private async Task PersistAsync(List<T> entities, CancellationToken ct)
	{
		var tmp = _filePath + ".tmp";
		await using (var fs = File.Create(tmp))
			await JsonSerializer.SerializeAsync(fs, entities, _json, ct);
		File.Move(tmp, _filePath, overwrite: true);
	}
}
