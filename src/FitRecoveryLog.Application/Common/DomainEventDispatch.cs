using System.Collections;
using FitRecoveryLog.Domain.Common;

namespace FitRecoveryLog.Application.Common;

/// <summary>Handles a specific kind of domain event.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}

/// <summary>Dispatches domain events (raised by aggregates) to their handlers after the
/// aggregate has been persisted.</summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}

/// <summary>
/// Resolves and invokes every registered <see cref="IDomainEventHandler{TEvent}"/> for each
/// event via the DI container — so new reactions are added by registering a handler, with no
/// change to the code that raises or dispatches events.
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _services;
    public DomainEventDispatcher(IServiceProvider services) => _services = services;

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handlers = _services.GetService(typeof(IEnumerable<>).MakeGenericType(handlerType)) as IEnumerable;
            if (handlers is null) continue;

            var handle = handlerType.GetMethod("HandleAsync")!;
            foreach (var handler in handlers)
                await (Task)handle.Invoke(handler, new object[] { domainEvent, ct })!;
        }
    }
}
