namespace Hookline.SharedKernel.Connections;

/// <summary>Well-known Google OAuth scope strings shared across modules (one source of truth so the
/// consent set, the scope snapshot and the moderation-capability check can never drift).</summary>
public static class GoogleScopes
{
    /// <summary>Manage a YouTube account — required to moderate (reject) comments on owned channels.
    /// Value matches <c>Google.Apis.YouTube.v3.YouTubeService.Scope.YoutubeForceSsl</c>.</summary>
    public const string YouTubeForceSsl = "https://www.googleapis.com/auth/youtube.force-ssl";
}

/// <summary>
/// A short-lived <c>youtube.force-ssl</c> access credential for the Google account that OWNS a given
/// YouTube channel. Handed to a caller that performs the actual YouTube call itself — this is an Access
/// primitive (token + expiry + granted scopes), deliberately free of any Google SDK type so it can live
/// in the SharedKernel. <c>youtube.force-ssl</c> authorizes full read AND write of the owner's YouTube
/// account, so the SAME credential covers comment monitoring (<c>commentThreads.list</c>) and moderation
/// (<c>comments.setModerationStatus</c>).
/// </summary>
public sealed record GoogleChannelCredential(
    Guid AccountId,
    string YouTubeChannelId,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes);

/// <summary>
/// Resolves a <c>youtube.force-ssl</c> Google access credential for the account that owns a YouTube
/// channel. ONE getter serves both read and write: the YouTube Comments module uses the returned token
/// to MONITOR comments (<c>commentThreads.list</c>/<c>comments.list</c>) and to MODERATE them
/// (<c>comments.setModerationStatus</c>) — so monitoring-gating and reject-gating are consistent by
/// construction (a channel either has a force-ssl owner account, enabling both, or neither).
/// <para><b>This contract is about GOOGLE ACCESS, not comments.</b> The caller performs the actual
/// YouTube call with the returned access token — the YouTube domain is the caller's, credential
/// resolution is the implementer's.</para>
/// <para><b>Ownership of the OAuth client stays with whoever owns Google OAuth.</b> Today the YouTube
/// Uploads module owns the OAuth clients ("Projects") and implements this contract over them, so the
/// refresh token stays issued + refreshed by its own Project client (no token migration, Uploads'
/// credential path untouched). If Google OAuth is later consolidated into the kernel, only the
/// IMPLEMENTER of this interface moves (Uploads → Connections) — the contract and its consumers do
/// not change. That clean swap is the whole reason this is about access, not about Projects.</para>
/// </summary>
public interface IGoogleChannelCredentials
{
    /// <summary>
    /// Resolve a <c>youtube.force-ssl</c> access credential for the ACTIVE account that owns
    /// <paramref name="youtubeChannelId"/>. Returns <c>null</c> when no active account owns that
    /// channel, or the owning account has not been granted the force-ssl scope — the caller surfaces an
    /// honest "no connected Google account for this channel" state (disabled monitoring / "re-connect to
    /// enable") rather than failing silently.
    /// </summary>
    Task<GoogleChannelCredential?> GetChannelCredentialAsync(string youtubeChannelId, CancellationToken ct = default);
}
