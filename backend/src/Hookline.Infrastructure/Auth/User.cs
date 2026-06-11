using Hookline.SharedKernel.Auth;

namespace Hookline.Infrastructure.Auth;

/// <summary>A hub user. Roles: Owner (supreme) / Admin / Member.</summary>
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
