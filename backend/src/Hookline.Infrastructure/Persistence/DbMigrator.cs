using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Hookline.Infrastructure.Persistence;

/// <summary>
/// Runs every context's migrations under a single Postgres advisory lock so concurrent
/// host instances serialize and never race. If any migration fails the exception
/// propagates — the host fails to start rather than serving a half-migrated schema.
/// </summary>
public static class DbMigrator
{
    // Stable arbitrary key ("HOOKLINE" as ASCII bytes); positive bigint.
    private const long AdvisoryLockKey = 0x484F_4F4B_4C49_4E45;

    public static async Task MigrateAsync(
        string connectionString,
        IEnumerable<DbContext> contexts,
        ILogger logger,
        CancellationToken ct = default)
    {
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);

        await using (var acquire = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", lockConnection))
        {
            acquire.Parameters.AddWithValue("key", AdvisoryLockKey);
            await acquire.ExecuteNonQueryAsync(ct);
        }

        logger.LogInformation("Acquired migration advisory lock.");
        try
        {
            foreach (var context in contexts)
            {
                logger.LogInformation("Applying migrations for {Context}.", context.GetType().Name);
                await context.Database.MigrateAsync(ct);
            }
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", lockConnection);
            release.Parameters.AddWithValue("key", AdvisoryLockKey);
            await release.ExecuteNonQueryAsync(ct);
            logger.LogInformation("Released migration advisory lock.");
        }
    }
}
