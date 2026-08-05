using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Workouts;

namespace FitRecoveryLog.Application.Workouts;

/// <summary>
/// Decorates an <see cref="IWorkoutRepository"/> so that saving a session automatically
/// dispatches any domain events it raised, then clears them. This is the persistence-boundary
/// dispatch point: use cases just mutate the aggregate and save — no use case has to remember
/// to dispatch, so a newly-raised event can never be silently dropped.
/// </summary>
public sealed class EventDispatchingWorkoutRepository : IWorkoutRepository
{
    private readonly IWorkoutRepository _inner;
    private readonly IDomainEventDispatcher _dispatcher;

    public EventDispatchingWorkoutRepository(IWorkoutRepository inner, IDomainEventDispatcher dispatcher)
    {
        _inner = inner;
        _dispatcher = dispatcher;
    }

    public Task<WorkoutSession?> GetAsync(Guid id, CancellationToken ct = default) => _inner.GetAsync(id, ct);
    public Task<IReadOnlyList<WorkoutSession>> ListAsync(CancellationToken ct = default) => _inner.ListAsync(ct);
    public Task RemoveAsync(Guid id, CancellationToken ct = default) => _inner.RemoveAsync(id, ct);

    public async Task SaveAsync(WorkoutSession session, CancellationToken ct = default)
    {
        await _inner.SaveAsync(session, ct);
        if (session.DomainEvents.Count == 0) return;
        await _dispatcher.DispatchAsync(session.DomainEvents, ct);
        session.ClearDomainEvents();
    }
}
