namespace Hookline.Modules.YouTubeUploads.Domain;

/// <summary>
/// Routes a Slack channel to a Google account. One Slack channel → exactly one account
/// (<see cref="SlackChannelId"/> is unique); an account may receive from many channels.
/// Both the workspace and the account live in the shared <c>connections</c> schema, so
/// these are plain id values — never cross-schema FKs.
/// </summary>
public class ChannelMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shared Connections workspace id (plain value, no FK).</summary>
    public Guid SlackWorkspaceId { get; set; }
    public string SlackChannelId { get; set; } = default!;
    public string SlackChannelName { get; set; } = default!;

    /// <summary>Shared Connections Google account id (plain value, no FK).</summary>
    public Guid GoogleAccountId { get; set; }

    /// <summary>
    /// Active (true) or paused (false). Uploads are event-driven, so a paused route is skipped at ingest
    /// (<c>SlackIngestService</c> enqueues nothing for its Slack messages) rather than tearing down a
    /// recurring job — there is none. The flag is read fresh on every message, so pausing reacts
    /// immediately and survives a restart with no reconcile step.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
