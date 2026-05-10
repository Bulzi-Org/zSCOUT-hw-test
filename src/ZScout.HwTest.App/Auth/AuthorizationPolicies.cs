using Microsoft.AspNetCore.Authorization;

namespace ZScout.HwTest.App.Auth;

/// <summary>
/// RBAC policy names used across API endpoints and Blazor components.
/// </summary>
public static class PolicyNames
{
    public const string RequireViewer = "RequireViewer";
    public const string RequireOperator = "RequireOperator";
    public const string RequireAdmin = "RequireAdmin";
}

public static class AuthorizationPolicies
{
    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(PolicyNames.RequireViewer, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireRole("Viewer", "Operator", "Admin"));

        options.AddPolicy(PolicyNames.RequireOperator, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireRole("Operator", "Admin"));

        options.AddPolicy(PolicyNames.RequireAdmin, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireRole("Admin"));
    }
}
