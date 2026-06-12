using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>
/// The YouTube Uploads module's implementation of the shared <see cref="IGoogleChannelCredentials"/>
/// contract: it owns the OAuth clients ("Projects") and the account↔channel bindings, so it can hand
/// any consumer a force-ssl access credential for the account that owns a channel WITHOUT
/// exposing client secrets or coupling the consumer to this module. The same credential covers both
/// comment monitoring (read) and moderation (write).
/// <para>The force-ssl decision reads the account's granted-scope snapshot from the shared store (what
/// Google actually granted at consent); the access token comes from refreshing the refresh token with
/// its ISSUING Project client (the hard rule — a token is only refreshable by the client that issued
/// it). The refresh token never leaves this module; only a short-lived access token is returned.</para>
/// <para>If Google OAuth is later consolidated into the kernel, THIS class moves to Connections and the
/// consumers (e.g. YouTube Comments) do not change — the contract is about access, not about Projects.</para>
/// </summary>
public sealed class GoogleChannelCredentials(
    YouTubeUploadsDbContext db,
    GoogleAccountsService accounts,
    GoogleCredentialFactory factory,
    IGoogleConnections googleAccounts) : IGoogleChannelCredentials
{
    public async Task<GoogleChannelCredential?> GetChannelCredentialAsync(
        string youtubeChannelId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(youtubeChannelId))
            return null;

        // Resolve ownership on module-local bindings (single schema): the oldest Active binding whose
        // snapshotted channel id matches. Any owning account can read+moderate — a single call, not quota-rotated.
        var accountId = await db.GoogleAccountBindings.AsNoTracking()
            .Where(b => b.IsActive && b.Status == "Active" && b.YouTubeChannelId == youtubeChannelId)
            .OrderBy(b => b.CreatedAt)
            .Select(b => (Guid?)b.AccountId)
            .FirstOrDefaultAsync(ct);
        if (accountId is null)
            return null; // no connected account owns this channel

        // Decide force-ssl from the granted-scope snapshot (what Google granted at consent), not from a
        // refresh response (Google may omit `scope` on refresh). No force-ssl ⇒ honest null.
        var detail = await googleAccounts.GetAsync(accountId.Value, ct);
        if (detail is null || !detail.IsActive)
            return null;
        var scopes = detail.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!scopes.Contains(GoogleScopes.YouTubeForceSsl, StringComparer.Ordinal))
            return null; // account not yet re-consented with the moderation scope

        // Resolve the issuing-client creds + refresh token, then mint a fresh access token via that client.
        var creds = await accounts.GetAccountCredsAsync(accountId.Value, ct);
        if (creds is null)
            return null;

        var userCred = factory.CreateUserCredential(creds.ClientId, creds.ClientSecret, creds.RefreshToken);
        if (!await userCred.RefreshTokenAsync(ct))
            return null;

        var token = userCred.Token;
        if (string.IsNullOrEmpty(token.AccessToken))
            return null;

        var expiresAt = token.IssuedUtc.AddSeconds(token.ExpiresInSeconds ?? 3600);
        return new GoogleChannelCredential(accountId.Value, youtubeChannelId, token.AccessToken, expiresAt, scopes);
    }
}
