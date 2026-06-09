namespace Hookline.SharedKernel.Messaging;

/// <summary>
/// Base for in-process integration events. Events carry only IDs/primitives and
/// live in the SharedKernel so any module can react without referencing another.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Handles a specific integration event. Implementations are resolved from DI.</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}

/// <summary>Publishes integration events to all registered handlers (in-process today).</summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IntegrationEvent;
}
