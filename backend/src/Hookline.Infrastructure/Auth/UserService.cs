using System.Security.Cryptography;

using Hookline.SharedKernel.Auth;
using Hookline.SharedKernel.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

namespace Hookline.Infrastructure.Auth;

/// <summary>The hub's user lifecycle: bootstrap, credential validation, and role-safe creation.</summary>
public sealed class UserService(
    AuthDbContext db,
    PasswordHasher hasher,
    IOptions<BootstrapOptions> bootstrap,
    ILogger<UserService> logger)
{
    /// <summary>
    /// First-run seed: if <c>users</c> is empty, create one bootstrap Admin from env.
    /// Idempotent — env creds are inert once any user exists. A blank password is
    /// generated and logged once (break-glass) rather than silently skipped.
    /// </summary>
    public async Task BootstrapAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var email = bootstrap.Value.AdminEmail;
        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning("No users exist and Bootstrap__AdminEmail is unset — cannot seed a bootstrap admin.");
            return;
        }

        var password = bootstrap.Value.AdminPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            password = GenerateBreakGlassPassword();
            logger.LogWarning("BOOTSTRAP_ADMIN_PASSWORD (generated, capture now): {Password}", password);
        }

        db.Users.Add(new User
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = hasher.Hash(password),
            Role = UserRole.Admin,
            Status = UserStatus.Active,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded bootstrap admin {Email}.", email);
    }

    public async Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == email.Trim().ToLowerInvariant(), ct);

        if (user is null || user.Status != UserStatus.Active || !hasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public Task<bool> OwnerExistsAsync(CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Role == UserRole.Owner, ct);

    public Task<User?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);

    /// <summary>
    /// One-time Create-Owner. While no Owner exists, an Admin may create exactly one.
    /// Once an Owner exists, only an Owner may grant the role. The partial unique index
    /// <c>ux_users_single_owner</c> is the race-safe backstop: concurrent creates can
    /// never both succeed.
    /// </summary>
    public async Task<Result<User>> CreateOwnerAsync(ICurrentUser caller, string email, string password, CancellationToken ct = default)
    {
        if (!caller.HasAtLeast(UserRole.Admin))
        {
            return Error.Forbidden;
        }

        var ownerExists = await OwnerExistsAsync(ct);
        if (ownerExists && caller.Role != UserRole.Owner)
        {
            return Error.Forbidden with { Message = "An Owner already exists; only the Owner may grant the Owner role." };
        }

        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = hasher.Hash(password),
            Role = UserRole.Owner,
            Status = UserStatus.Active,
            CreatedBy = caller.UserId,
        };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
            return user;
        }
        catch (DbUpdateException ex) when (IsSingleOwnerViolation(ex))
        {
            db.Entry(user).State = EntityState.Detached;
            return Error.Conflict("An Owner already exists.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(user).State = EntityState.Detached;
            return Error.Conflict("A user with that email already exists.");
        }
    }

    /// <summary>Create a Member/Admin. Admins manage Members; only an Owner may create Admins.</summary>
    public async Task<Result<User>> CreateUserAsync(ICurrentUser caller, string email, string password, UserRole role, CancellationToken ct = default)
    {
        if (role == UserRole.Owner)
        {
            return await CreateOwnerAsync(caller, email, password, ct);
        }

        var allowed = role switch
        {
            UserRole.Admin => caller.Role == UserRole.Owner,
            UserRole.Member => caller.HasAtLeast(UserRole.Admin),
            _ => false,
        };
        if (!allowed)
        {
            return Error.Forbidden;
        }

        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = hasher.Hash(password),
            Role = role,
            Status = UserStatus.Active,
            CreatedBy = caller.UserId,
        };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
            return user;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(user).State = EntityState.Detached;
            return Error.Conflict("A user with that email already exists.");
        }
    }

    private static bool IsSingleOwnerViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_users_single_owner" };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string GenerateBreakGlassPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('/', '_').Replace('+', '-');
}
