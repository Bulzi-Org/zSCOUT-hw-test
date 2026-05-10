using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Auth;

/// <summary>
/// Local authentication service: password verification and session cookie management.
/// Passwords are stored as SHA-256 hashes (sufficient for a local-only, offline-first device).
/// </summary>
public sealed class LocalAuthService
{
	private readonly IUserStore _userStore;

	public LocalAuthService(IUserStore userStore)
	{
		_userStore = userStore;
	}

	public static string HashPassword(string password)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
		return Convert.ToHexString(bytes).ToLowerInvariant();
	}

	public async Task<UserAccount?> ValidateCredentialsAsync(
		string username, string password, CancellationToken ct = default)
	{
		var user = await _userStore.FindByUsernameAsync(username, ct);
		if (user is null || !user.IsActive) return null;
		var hash = HashPassword(password);
		return hash == user.PasswordHash ? user : null;
	}

	public static ClaimsPrincipal BuildPrincipal(UserAccount user)
	{
		var claims = new[]
		{
			new Claim(ClaimTypes.NameIdentifier, user.UserId),
			new Claim(ClaimTypes.Name, user.Username),
			new Claim(ClaimTypes.Role, user.Role.ToString())
		};
		var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
		return new ClaimsPrincipal(identity);
	}

	public static async Task SignInAsync(HttpContext context, UserAccount user)
	{
		var principal = BuildPrincipal(user);
		await context.SignInAsync(
			CookieAuthenticationDefaults.AuthenticationScheme,
			principal,
			new AuthenticationProperties { IsPersistent = false });
	}

	public static async Task SignOutAsync(HttpContext context)
		=> await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
