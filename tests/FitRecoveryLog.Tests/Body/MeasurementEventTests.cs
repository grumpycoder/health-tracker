using FitRecoveryLog.Application.Body;
using FitRecoveryLog.Domain.Body;
using FitRecoveryLog.Domain.Body.Events;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Body;

[TestFixture]
public class MeasurementEventTests
{
    private static readonly DateOnly Date = new(2026, 8, 5);

    [Test]
    public void Update_WithWeight_RaisesMeasurementRecorded()
    {
        var m = Measurement.Create(Date);
        m.Update(185.4, null, null, null, null, null, null, null, null, null, null, null, null);

        var evt = m.DomainEvents.OfType<MeasurementRecorded>().SingleOrDefault();
        Assert.That(evt, Is.Not.Null);
        Assert.That(evt!.WeightLbs, Is.EqualTo(185.4));
    }

    [Test]
    public void Update_WithoutWeightOrWaist_RaisesNothing()
    {
        var m = Measurement.Create(Date);
        // Only body-composition values — nothing HealthKit mirrors.
        m.Update(null, null, 40, null, null, null, 18.0, null, null, null, null, null, "belt notch");

        Assert.That(m.DomainEvents, Is.Empty);
    }

    [Test]
    public void Rehydrate_DoesNotRaise()
    {
        var m = Measurement.Rehydrate(Guid.NewGuid(), Date, 185, 34, null, null, null, null,
            null, null, null, null, null, null, null);

        Assert.That(m.DomainEvents, Is.Empty, "reconstructing persisted state must not raise events");
    }

    [Test]
    public async Task Handler_MirrorsWeightAndWaist()
    {
        var mirror = new FakeMirror();
        var handler = new MeasurementRecordedHandler(mirror);

        await handler.HandleAsync(new MeasurementRecorded(Guid.NewGuid(), Date, 185.4, 34.0));

        Assert.Multiple(() =>
        {
            Assert.That(mirror.Weight, Is.EqualTo(185.4));
            Assert.That(mirror.Waist, Is.EqualTo(34.0));
        });
    }

    [Test]
    public async Task Handler_SkipsMissingValues()
    {
        var mirror = new FakeMirror();
        var handler = new MeasurementRecordedHandler(mirror);

        await handler.HandleAsync(new MeasurementRecorded(Guid.NewGuid(), Date, 185.4, null));

        Assert.Multiple(() =>
        {
            Assert.That(mirror.Weight, Is.EqualTo(185.4));
            Assert.That(mirror.Waist, Is.Null, "a null waist must not be written");
        });
    }

    private sealed class FakeMirror : IHealthMirror
    {
        public double? Weight, Waist;
        public Task WriteWeightAsync(DateOnly date, double lbs, Guid id, CancellationToken ct = default) { Weight = lbs; return Task.CompletedTask; }
        public Task WriteWaistAsync(DateOnly date, double inches, Guid id, CancellationToken ct = default) { Waist = inches; return Task.CompletedTask; }
    }
}
