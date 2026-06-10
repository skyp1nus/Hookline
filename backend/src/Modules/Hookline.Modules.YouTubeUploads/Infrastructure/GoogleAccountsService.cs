using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

public sealed record GoogleConnection(bool Connected, string? Scopes, DateTimeOffset? ConnectedAt);

public sealed record GoogleAccountDto(
    Guid Id, string Label, string? YouTubeChannelId, string? YouTubeChannelTitle,
    string? AvatarUrl, string? AccountEmail, string Status, DateTimeOffset CreatedAt,
    Guid? ProjectId, string? ProjectLabel);

/// <summary>
/// Decrypted creds + token for ONE account, ready to build a Drive/YouTube service. The client id +
/// secret are the account's ISSUING project (never a different one). In-memory only — never serialized.
/// </summary>
public sealed record GoogleUploadCreds(
    Guid AccountId, Guid ProjectId, string ClientId, string ClientSecret, string RefreshToken);

/// <summary>
/// Manages connected Google/YouTube accounts. The account RECORD (identity + refresh token + display
/// metadata) lives in the shared <c>connections</c> store (resolved via <see cref="IGoogleConnections"/>);
/// the module-local <see cref="GoogleAccountBinding"/> ties each account to the project that issued its
/// refresh token and snapshots the pool key (YouTube channel id). Candidate SELECTION runs entirely on
/// the <c>youtube_uploads</c> bindings; only the FINAL refresh-token resolve for the chosen account crosses to
/// <c>connections</c>, via the accessor — never a SQL join.
/// </summary>
public sealed class GoogleAccountsService(
    GoogleCredentialFactory factory,
    GoogleProjectsService projects,
    IOptions<YouTubeUploadsOptions> options,
    YouTubeUploadsDbContext db,
    IGoogleConnections googleAccounts,
    YouTubeUploadService youtube,
    IQuotaService quota)
{
    private string RedirectUri => options.Value.Google.RedirectUri;

    /// <summary>Builds the consent URL for a specific project. The redirect URI stays the global one
    /// (every project's client must register the same <c>/google/oauth/callback</c>).</summary>
    public async Task<string> BuildConsentUrlAsync(Guid projectId, string state, CancellationToken ct = default)
    {
        var creds = await projects.GetClientCredsAsync(projectId, ct)
            ?? throw new InvalidOperationException("Selected project was not found.");
        if (!creds.IsActive) throw new InvalidOperationException("Selected project is disabled.");

        var flow = factory.CreateFlow(creds.ClientId, creds.ClientSecret);
        var request = flow.CreateAuthorizationCodeRequest(RedirectUri);
        request.State = state;
        return request.Build().AbsoluteUri;
    }

    /// <summary>Exchanges the code with the chosen project, fetches the channel id/title, writes the account
    /// to the SHARED store, and creates the module-local binding (account ↔ issuing project + pool key).</summary>
    public async Task<Guid> ExchangeAndStoreAsync(Guid projectId, string code, CancellationToken ct)
    {
        var creds = await projects.GetClientCredsAsync(projectId, ct)
            ?? throw new InvalidOperationException("Selected project was not found.");
        if (!creds.IsActive) throw new InvalidOperationException("Selected project is disabled.");

        var flow = factory.CreateFlow(creds.ClientId, creds.ClientSecret);
        var token = await flow.ExchangeCodeForTokenAsync("user", code, RedirectUri, ct);
        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new InvalidOperationException(
                "Google returned no refresh token. Revoke prior access or re-consent (prompt=consent).");

        string? channelId = null, channelTitle = null, avatarUrl = null;
        try
        {
            (channelId, channelTitle, avatarUrl) = await youtube.GetChannelInfoAsync(creds.ClientId, creds.ClientSecret, token.RefreshToken, ct);
            await quota.ChargeUnitsAsync(projectId, 1); // channels.list ≈ 1 unit against the non-upload pool
        }
        catch { /* channel lookup is best-effort; the account still works for upload */ }

        // 1) Account record → shared connections store (refresh token encrypted on write by the converter).
        var accountId = await googleAccounts.CreateAccountAsync(new GoogleAccountWrite(
            ChannelId: channelId,
            ChannelTitle: channelTitle ?? "YouTube account",
            RefreshToken: token.RefreshToken,
            Scopes: string.Join(' ', GoogleCredentialFactory.Scopes),
            AccountEmail: null,
            AvatarUrl: avatarUrl), ct);

        // 2) Module-local binding → ties the shared account to its issuing project + snapshots the pool key.
        var now = DateTimeOffset.UtcNow;
        db.GoogleAccountBindings.Add(new GoogleAccountBinding
        {
            AccountId = accountId,
            ProjectId = projectId,
            YouTubeChannelId = channelId,
            Label = channelTitle ?? "YouTube account",
            Status = "Active",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return accountId;
    }

    public async Task<IReadOnlyList<GoogleAccountDto>> ListAccountsAsync(CancellationToken ct = default)
    {
        var bindings = await db.GoogleAccountBindings.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(ct);

        var accounts = (await googleAccounts.ListAsync(ct)).ToDictionary(a => a.Id);
        var details = new Dictionary<Guid, GoogleAccountDetail>();
        foreach (var b in bindings)
        {
            if (await googleAccounts.GetAsync(b.AccountId, ct) is { } d) details[b.AccountId] = d;
        }
        var projectLabels = (await projects.ListAsync(ct)).ToDictionary(p => p.Id, p => p.Label);

        return bindings.Select(b =>
        {
            details.TryGetValue(b.AccountId, out var d);
            return new GoogleAccountDto(
                b.AccountId,
                b.Label,
                d?.ChannelId,
                d?.ChannelTitle ?? b.Label,
                d?.AvatarUrl,
                d?.AccountEmail,
                b.Status,
                b.CreatedAt,
                b.ProjectId,
                projectLabels.GetValueOrDefault(b.ProjectId));
        }).ToList();
    }

    /// <summary>The default account (oldest binding) — used when a job carries no explicit account.</summary>
    public Task<Guid?> GetDefaultAccountIdAsync(CancellationToken ct = default) =>
        db.GoogleAccountBindings.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => (Guid?)b.AccountId)
            .FirstOrDefaultAsync(ct);

    /// <summary>Decrypted creds (issuing-project id+secret + refresh token) for ONE account. Null if the
    /// account/binding is missing, the project is undecryptable, or the refresh token is gone.</summary>
    public async Task<GoogleUploadCreds?> GetAccountCredsAsync(Guid accountId, CancellationToken ct = default)
    {
        var binding = await db.GoogleAccountBindings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.AccountId == accountId, ct);
        if (binding is null) return null;

        var clientCreds = await projects.GetClientCredsAsync(binding.ProjectId, ct);
        if (clientCreds is null) return null;
        var refreshToken = await googleAccounts.GetRefreshTokenAsync(accountId, ct);
        if (string.IsNullOrEmpty(refreshToken)) return null;

        return new GoogleUploadCreds(accountId, binding.ProjectId, clientCreds.ClientId, clientCreds.ClientSecret, refreshToken);
    }

    /// <summary>
    /// Every account that can upload to the SAME YouTube channel as <paramref name="targetAccountId"/>:
    /// all Active bindings sharing its channel id, each bound to an Active project. This is the rotation
    /// pool — N projects backing one channel ⇒ N independent daily quotas. Falls back to just the target
    /// when it has no channel id. Undecryptable / token-less rows are skipped. Ordered oldest-binding-first.
    /// </summary>
    public async Task<IReadOnlyList<GoogleUploadCreds>> GetUploadCandidatesForChannelAsync(
        Guid targetAccountId, CancellationToken ct = default)
    {
        var target = await db.GoogleAccountBindings.AsNoTracking()
            .Where(b => b.AccountId == targetAccountId)
            .Select(b => new { b.YouTubeChannelId })
            .FirstOrDefaultAsync(ct);
        if (target is null) return Array.Empty<GoogleUploadCreds>();

        // Candidate SELECTION — entirely on youtube_uploads tables (bindings ⋈ projects), single schema.
        var query = db.GoogleAccountBindings.AsNoTracking().Where(b => b.Status == "Active");
        query = string.IsNullOrEmpty(target.YouTubeChannelId)
            ? query.Where(b => b.AccountId == targetAccountId)
            : query.Where(b => b.YouTubeChannelId == target.YouTubeChannelId);

        var rows = await query
            .Join(db.GoogleProjects.AsNoTracking().Where(p => p.Status == GoogleProject.StatusActive),
                  b => b.ProjectId, p => p.Id,
                  (b, p) => new { b.AccountId, b.CreatedAt, b.ProjectId })
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        var result = new List<GoogleUploadCreds>(rows.Count);
        var projectCache = new Dictionary<Guid, GoogleClientCreds?>();
        foreach (var r in rows)
        {
            if (!projectCache.TryGetValue(r.ProjectId, out var clientCreds))
            {
                clientCreds = await projects.GetClientCredsAsync(r.ProjectId, ct);
                projectCache[r.ProjectId] = clientCreds;
            }
            if (clientCreds is null) continue;

            // FINAL credential resolve — the one cross-schema hop, via the accessor (contract, not a join).
            var refreshToken = await googleAccounts.GetRefreshTokenAsync(r.AccountId, ct);
            if (string.IsNullOrEmpty(refreshToken)) continue;

            result.Add(new GoogleUploadCreds(r.AccountId, r.ProjectId, clientCreds.ClientId, clientCreds.ClientSecret, refreshToken));
        }
        return result;
    }

    /// <summary>
    /// The DISTINCT Active projects backing the same YouTube channel as <paramref name="targetAccountId"/>
    /// — i.e. the rotation pool's per-project quota counters. Single-schema (bindings ⋈ projects). Empty
    /// when the account is gone.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetChannelProjectIdsAsync(Guid targetAccountId, CancellationToken ct = default)
    {
        var target = await db.GoogleAccountBindings.AsNoTracking()
            .Where(b => b.AccountId == targetAccountId)
            .Select(b => new { b.YouTubeChannelId })
            .FirstOrDefaultAsync(ct);
        if (target is null) return Array.Empty<Guid>();

        var query = db.GoogleAccountBindings.AsNoTracking().Where(b => b.Status == "Active");
        query = string.IsNullOrEmpty(target.YouTubeChannelId)
            ? query.Where(b => b.AccountId == targetAccountId)
            : query.Where(b => b.YouTubeChannelId == target.YouTubeChannelId);

        return await query
            .Join(db.GoogleProjects.AsNoTracking().Where(p => p.Status == GoogleProject.StatusActive),
                  b => b.ProjectId, p => p.Id, (b, p) => p.Id)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>Display name of the YouTube channel an account targets — shown in the per-channel Slack status.</summary>
    public async Task<string?> GetAccountChannelLabelAsync(Guid accountId, CancellationToken ct = default)
    {
        var binding = await db.GoogleAccountBindings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.AccountId == accountId, ct);
        if (binding is null) return null;
        var detail = await googleAccounts.GetAsync(accountId, ct);
        return detail?.ChannelTitle ?? binding.Label;
    }

    /// <summary>Flags an account's binding as broken (revoked / wrong project) so rotation skips it.</summary>
    public async Task MarkAccountErrorAsync(Guid accountId, CancellationToken ct = default)
    {
        var binding = await db.GoogleAccountBindings.FirstOrDefaultAsync(b => b.AccountId == accountId, ct);
        if (binding is null || binding.Status == "Error") return;
        binding.Status = "Error";
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var binding = await db.GoogleAccountBindings.FirstOrDefaultAsync(b => b.AccountId == accountId, ct);
        if (binding is not null)
        {
            db.GoogleAccountBindings.Remove(binding);
            await db.SaveChangesAsync(ct);
        }
        // Deactivate the shared account (publishes GoogleAccountDisconnected for any other listeners).
        return await googleAccounts.DeactivateAsync(accountId, ct) || binding is not null;
    }

    public Task<int> CountAccountsAsync(CancellationToken ct = default) =>
        db.GoogleAccountBindings.CountAsync(ct);

    public async Task<GoogleConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        var first = await db.GoogleAccountBindings.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (first is null) return new GoogleConnection(false, null, null);
        var detail = await googleAccounts.GetAsync(first.AccountId, ct);
        return new GoogleConnection(true, detail?.Scopes, first.CreatedAt);
    }
}
