using Hookline.SharedKernel.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hookline.Infrastructure.Messaging;

/// <summary>
/// In-process event bus. Resolves all registered handlers for the event type and
/// invokes them in a fresh scope. A failing handler is logged and does not stop the
/// others. Swapping this for a real broker is how a module gets extracted later.
/// </summary>
public sealed class InProcessEventBus(IServiceProvider services, ILogger<InProcessEventBus> logger) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        using var scope = services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>();

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(@event, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler {Handler} failed for {Event}",
                    handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }
}
