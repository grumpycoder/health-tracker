using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Body;

namespace FitRecoveryLog.Application.Body;

/// <summary>
/// Decorates an <see cref="IMeasurementRepository"/> so saving a measurement dispatches any
/// domain events it raised (e.g. <see cref="Domain.Body.Events.MeasurementRecorded"/>), then
/// clears them. Same persistence-boundary dispatch as the workout repository.
/// </summary>
public sealed class EventDispatchingMeasurementRepository : IMeasurementRepository
{
    private readonly IMeasurementRepository _inner;
    private readonly IDomainEventDispatcher _dispatcher;

    public EventDispatchingMeasurementRepository(IMeasurementRepository inner, IDomainEventDispatcher dispatcher)
    {
        _inner = inner;
        _dispatcher = dispatcher;
    }

    public Task<Measurement?> GetAsync(Guid id, CancellationToken ct = default) => _inner.GetAsync(id, ct);
    public Task<IReadOnlyList<Measurement>> ListAsync(CancellationToken ct = default) => _inner.ListAsync(ct);
    public Task RemoveAsync(Guid id, CancellationToken ct = default) => _inner.RemoveAsync(id, ct);

    public async Task SaveAsync(Measurement measurement, CancellationToken ct = default)
    {
        await _inner.SaveAsync(measurement, ct);
        if (measurement.DomainEvents.Count == 0) return;
        await _dispatcher.DispatchAsync(measurement.DomainEvents, ct);
        measurement.ClearDomainEvents();
    }
}
