namespace ZScout.HwTest.Contracts.Models;

public sealed record UserAccount
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string PasswordHash { get; init; }
    public required UserRole Role { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
