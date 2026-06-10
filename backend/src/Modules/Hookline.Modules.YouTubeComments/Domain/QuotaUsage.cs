namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>
/// Daily quota consumption per API key. Composite key (ApiKeyId, UsageDate); date is Pacific Time.
/// <see cref="ApiKeyId"/> is the shared Connections <c>api_keys</c> id (plain value, no cross-schema FK) —
/// the key identity/secret lives in the shared store; the per-key daily accounting lives here.
/// </summary>
public class QuotaUsage
{
    public Guid ApiKeyId { get; set; }
    public DateOnly UsageDate { get; set; }
    public int UnitsUsed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
