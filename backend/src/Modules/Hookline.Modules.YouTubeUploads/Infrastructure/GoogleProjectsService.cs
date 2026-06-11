using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Secrets;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>Read model for the admin UI — NEVER carries the client secret.</summary>
public sealed record GoogleProjectDto(
    Guid Id, string Label, string ClientId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Decrypted creds for building an auth flow / credential. In-memory only — never serialized.</summary>
public sealed record GoogleClientCreds(Guid Id, string ClientId, string ClientSecret, string Status)
{
    public bool IsActive => Status == GoogleProject.StatusActive;
}

/// <summary>
/// CRUD over <see cref="GoogleProject"/> (one per Google Cloud project, module-local). The client secret
/// is encrypted at rest via the shared <c>ISecretProtector</c> and is write-only from the UI's
/// perspective — it is never returned in a read.
/// </summary>
public sealed class GoogleProjectsService(YouTubeUploadsDbContext db, ISecretProtector protector)
{
    public const string StatusActive = GoogleProject.StatusActive;
    public const string StatusDisabled = GoogleProject.StatusDisabled;

    public static string NormalizeStatus(string? status) =>
        string.Equals(status, StatusDisabled, StringComparison.OrdinalIgnoreCase) ? StatusDisabled : StatusActive;

    public async Task<IReadOnlyList<GoogleProjectDto>> ListAsync(CancellationToken ct = default) =>
        await db.GoogleProjects.AsNoTracking()
            .OrderBy(c => c.CreatedAt)
            .Select(c => new GoogleProjectDto(c.Id, c.Label, c.ClientId, c.Status, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);

    public Task<bool> AnyActiveAsync(CancellationToken ct = default) =>
        db.GoogleProjects.AnyAsync(c => c.Status == StatusActive, ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.GoogleProjects.CountAsync(ct);

    public Task<bool> ClientIdExistsAsync(string clientId, CancellationToken ct = default) =>
        db.GoogleProjects.AnyAsync(c => c.ClientId == clientId.Trim(), ct);

    public async Task<GoogleProjectDto> CreateAsync(string label, string clientId, string clientSecret, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var project = new GoogleProject
        {
            Label = string.IsNullOrWhiteSpace(label) ? clientId : label.Trim(),
            ClientId = clientId.Trim(),
            EncryptedClientSecret = protector.Protect(clientSecret),
            Status = StatusActive,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GoogleProjects.Add(project);
        await db.SaveChangesAsync(ct);
        return new GoogleProjectDto(project.Id, project.Label, project.ClientId, project.Status, project.CreatedAt, project.UpdatedAt);
    }

    /// <summary>Patch label and/or status. Returns false if the project doesn't exist.</summary>
    public async Task<bool> UpdateAsync(Guid id, string? label, string? status, CancellationToken ct = default)
    {
        var project = await db.GoogleProjects.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (project is null) return false;
        if (label is not null) project.Label = string.IsNullOrWhiteSpace(label) ? project.Label : label.Trim();
        if (status is not null) project.Status = NormalizeStatus(status);
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Delete a project. FAILS (returns "client_in_use") while any account binding references it —
    /// the issuing-client binding is permanent, so the accounts must be disconnected first.</summary>
    public async Task<(bool ok, string? error)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var project = await db.GoogleProjects.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (project is null) return (false, "not_found");
        if (await db.GoogleAccountBindings.AnyAsync(b => b.ProjectId == id, ct)) return (false, "client_in_use");
        db.GoogleProjects.Remove(project);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public Task<int> CountAccountsAsync(Guid id, CancellationToken ct = default) =>
        db.GoogleAccountBindings.CountAsync(b => b.ProjectId == id, ct);

    /// <summary>Decrypted creds for the given project, or null if it doesn't exist / is undecryptable.</summary>
    public async Task<GoogleClientCreds?> GetClientCredsAsync(Guid id, CancellationToken ct = default)
    {
        var project = await db.GoogleProjects.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Id, c.ClientId, c.EncryptedClientSecret, c.Status })
            .FirstOrDefaultAsync(ct);
        if (project is null) return null;
        var secret = protector.TryUnprotect(project.EncryptedClientSecret, out var s) ? s : null;
        return secret is null ? null : new GoogleClientCreds(project.Id, project.ClientId, secret, project.Status);
    }
}
