namespace FitRecoveryLog.Domain.Common;

/// <summary>A fact that happened in the domain, worth reacting to (possibly in another
/// aggregate). Raised by aggregates, dispatched after they're persisted.</summary>
public interface IDomainEvent { }

/// <summary>Base for aggregate roots: collects domain events raised while handling behavior,
/// for the application layer to dispatch after a successful save.</summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
