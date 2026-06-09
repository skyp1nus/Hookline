namespace Hookline.Modules.Sample.Domain;

/// <summary>
/// A minimal domain type. It models the shape every real module's <c>Domain</c> folder will
/// take and, crucially, gives the "Domain folders are infrastructure-free" architecture rule
/// a real type to assert against so the rule is no longer vacuously true.
/// </summary>
public sealed record SamplePing(string Status, DateTimeOffset Time);
