using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Dashboard.Services;

/// <summary>
/// Provides a thin, component-friendly wrapper around the current authentication state.
/// Scoped to the Blazor circuit lifetime.
/// </summary>
public sealed class AuthStateService
{
	private readonly AuthenticationStateProvider _authProvider;
	private readonly NavigationManager _nav;

	public AuthStateService(AuthenticationStateProvider authProvider, NavigationManager nav)
	{
		_authProvider = authProvider;
		_nav = nav;
	}

	public async Task<bool> IsAuthenticatedAsync()
	{
		var state = await _authProvider.GetAuthenticationStateAsync();
		return state.User.Identity?.IsAuthenticated == true;
	}

	public async Task<string?> GetUsernameAsync()
	{
		var state = await _authProvider.GetAuthenticationStateAsync();
		return state.User.Identity?.Name;
	}

	public async Task<UserRole?> GetRoleAsync()
	{
		var state = await _authProvider.GetAuthenticationStateAsync();
		var roleStr = state.User.FindFirst(ClaimTypes.Role)?.Value;
		return Enum.TryParse<UserRole>(roleStr, out var role) ? role : null;
	}

	public async Task<string?> GetUserIdAsync()
	{
		var state = await _authProvider.GetAuthenticationStateAsync();
		return state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
	}

	public async Task<bool> IsOperatorOrAdminAsync()
	{
		var role = await GetRoleAsync();
		return role is UserRole.Operator or UserRole.Admin;
	}

	public void RedirectToLogin() => _nav.NavigateTo("/login", forceLoad: true);
}
