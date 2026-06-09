namespace Hookline.SharedKernel.Auth;

/// <summary>
/// Hub roles, ordered by power. <see cref="Owner"/> is supreme; the enum is
/// extensible. Numeric order encodes the hierarchy used by <c>HasAtLeast</c>.
/// </summary>
public enum UserRole
{
    Member = 0,
    Admin = 1,
    Owner = 2,
}

/// <summary>Account state.</summary>
public enum UserStatus
{
    Active = 0,
    Disabled = 1,
}

/// <summary>
/// The caller resolved per request from the BFF's signed identity assertion
/// (or the system principal for background jobs). Authorization policies key off this.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Email { get; }
    UserRole? Role { get; }

    /// <summary>True for the internal system principal that runs background jobs.</summary>
    bool IsSystem { get; }

    /// <summary>True when the caller's role is at least <paramref name="role"/>.</summary>
    bool HasAtLeast(UserRole role);
}
