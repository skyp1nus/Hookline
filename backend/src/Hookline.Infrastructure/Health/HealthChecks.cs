using Hookline.Infrastructure.Persistence;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using StackExchange.Redis;

namespace Hookline.Infrastructure.Health;

public sealed class PostgresHealthCheck(SharedDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Postgres is unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Postgres check failed.", ex);
        }
    }
}

public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis check failed.", ex);
        }
    }
}
