namespace ZScout.HwTest.App.Persistence;

/// <summary>
/// Generic file-backed repository contract.
/// </summary>
public interface IRepository<T> where T : class
{
	Task<T?> GetByIdAsync(string id, CancellationToken ct = default);
	Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
	Task SaveAsync(T entity, CancellationToken ct = default);
	Task DeleteAsync(string id, CancellationToken ct = default);
}
