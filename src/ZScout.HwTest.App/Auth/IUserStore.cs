using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Auth;

/// <summary>
/// File-backed store for UserAccount records persisted as JSON.
/// </summary>
public interface IUserStore
{
	Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken ct = default);
	Task<UserAccount?> FindByIdAsync(string userId, CancellationToken ct = default);
	Task<IReadOnlyList<UserAccount>> GetAllAsync(CancellationToken ct = default);
	Task UpsertAsync(UserAccount account, CancellationToken ct = default);
}
