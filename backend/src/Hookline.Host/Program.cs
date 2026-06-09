using Hangfire;

using Hookline.Host.Endpoints;
using Hookline.Infrastructure;
using Hookline.Infrastructure.Jobs;
using Hookline.Modules.Sample;
using Hookline.SharedKernel.Jobs;
using Hookline.SharedKernel.Modules;

using Serilog;
using Serilog.Context;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((_, lc) => lc
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Hookline")
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] [{module}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"));

    builder.Services.AddProblemDetails();
    builder.Services.AddHooklineInfrastructure(builder.Configuration, builder.Environment);

    // The explicit module list — no reflection scanning. Adding a module is one line here.
    var modules = new List<IModule> { new SampleModule() };
    foreach (var module in modules)
    {
        module.RegisterServices(builder.Services, builder.Configuration);
    }

    var app = builder.Build();

    // Migrate every schema under an advisory lock, then seed the bootstrap admin.
    // Either throwing fails startup — never a half-migrated boot.
    await app.Services.MigrateHooklineAsync(modules);
    await app.Services.SeedBootstrapAdminAsync();

    // Correlation id + module enrichment for every request's logs.
    app.Use(async (context, next) =>
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N")[..8];
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var module = segments is ["api", var name, ..] ? name : "host";

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("module", module))
        {
            await next();
        }
    });

    app.UseSerilogRequestLogging();
    app.UseHooklineIdentity();

    app.MapHealthChecks("/health");

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthorizationFilter()],
    });

    foreach (var module in modules)
    {
        module.MapEndpoints(app);
    }

    var scheduler = app.Services.GetRequiredService<IJobScheduler>();
    foreach (var module in modules)
    {
        module.RegisterJobs(scheduler);
    }

    app.MapHooklineAuthEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Hookline host terminated unexpectedly.");
    Log.CloseAndFlush();
    // Exit non-zero immediately. Rethrowing here leaves the faulted async host spinning
    // (99% CPU, container still "Up") instead of dying, which would let an orchestrator or
    // healthcheck misread a fail-fast (bad secrets, failed migration, …) as a healthy boot.
    Environment.Exit(1);
}
finally
{
    Log.CloseAndFlush();
}
