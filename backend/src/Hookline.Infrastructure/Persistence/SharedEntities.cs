namespace Hookline.Infrastructure.Persistence;

/// <summary>Append-only audit trail row (schema <c>shared</c>).</summary>
public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Actor { get; set; } = "system";
    public Guid? ActorId { get; set; }
    public string? Role { get; set; }
    public string? Module { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Hub-wide key/value setting (DB override layer, schema <c>shared</c>).</summary>
public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
