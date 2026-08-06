using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Labs;
using FitRecoveryLog.Domain.Meds;
using FitRecoveryLog.Domain.Notes;
using FitRecoveryLog.Domain.Recovery;
using FitRecoveryLog.Infrastructure.Labs;
using FitRecoveryLog.Infrastructure.Meds;
using FitRecoveryLog.Infrastructure.Notes;
using FitRecoveryLog.Infrastructure.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Tests.Recovery;

/// <summary>Round-trip integration tests for the remaining log-page EF repositories
/// (sleep, recovery, medication, lab, note) against in-memory SQLite.</summary>
[TestFixture]
public class EfRecoveryReposTests
{
    private SqliteConnection _connection = null!;
    private TestDbContextFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    [Test]
    public async Task Sleep_RoundTrips_WithEstimatedFlag()
    {
        var repo = new EfSleepRepository(_factory);
        var s = SleepLog.Create(new DateOnly(2026, 8, 5));
        s.SetDuration(7.5);
        s.SetScore(88, estimated: true);
        s.SetInterruptions(2);
        s.SetNotes("woke at 3am");
        await repo.SaveAsync(s);

        var loaded = await repo.GetAsync(s.Id);

        Assert.Multiple(() =>
        {
            Assert.That(loaded!.DurationHours, Is.EqualTo(7.5));
            Assert.That(loaded.Score, Is.EqualTo(88));
            Assert.That(loaded.ScoreEstimated, Is.True);
            Assert.That(loaded.Interruptions, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Recovery_RoundTrips_TagsAndSeverity()
    {
        var repo = new EfRecoveryRepository(_factory);
        var r = RecoveryLog.Create(new DateOnly(2026, 8, 5));
        r.SetRecoveryRating(6);
        r.SetFatigueRating(4);
        r.SetSoreness(Tags.FromCsv("Lower back,Thighs"), SorenessSeverity.Moderate);
        r.SetNotes("yard work");
        await repo.SaveAsync(r);

        var loaded = await repo.GetAsync(r.Id);

        Assert.Multiple(() =>
        {
            Assert.That(loaded!.RecoveryRating, Is.EqualTo(6));
            Assert.That(loaded.FatigueRating, Is.EqualTo(4));
            Assert.That(loaded.Severity, Is.EqualTo(SorenessSeverity.Moderate));
            Assert.That(loaded.SorenessLocations.Values, Is.EquivalentTo(new[] { "Lower back", "Thighs" }));
        });
    }

    [Test]
    public async Task Medication_RoundTrips()
    {
        var repo = new EfMedicationRepository(_factory);
        var d = MedicationDose.Create(new DateTime(2026, 8, 5, 9, 0, 0), "Testosterone Cypionate");
        d.SetDose("100mg");
        d.SetInjectionSite("Left delt");
        await repo.SaveAsync(d);

        var loaded = await repo.GetAsync(d.Id);

        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Name, Is.EqualTo("Testosterone Cypionate"));
            Assert.That(loaded.Dose, Is.EqualTo("100mg"));
            Assert.That(loaded.InjectionSite, Is.EqualTo("Left delt"));
        });
    }

    [Test]
    public async Task Lab_RoundTrips()
    {
        var repo = new EfLabRepository(_factory);
        var l = LabResult.Create(new DateOnly(2026, 8, 5), "Total Testosterone");
        l.SetValue(650);
        l.SetUnit("ng/dL");
        await repo.SaveAsync(l);

        var loaded = await repo.GetAsync(l.Id);

        Assert.Multiple(() =>
        {
            Assert.That(loaded!.LabName, Is.EqualTo("Total Testosterone"));
            Assert.That(loaded.Value, Is.EqualTo(650));
            Assert.That(loaded.Unit, Is.EqualTo("ng/dL"));
        });
    }

    [Test]
    public async Task Note_RoundTrips_AndSoftDeletes()
    {
        var repo = new EfNoteRepository(_factory);
        var n = Note.Create(new DateTime(2026, 8, 5, 14, 0, 0), "felt great after workout");
        await repo.SaveAsync(n);

        var loaded = await repo.GetAsync(n.Id);
        Assert.That(loaded!.Text, Is.EqualTo("felt great after workout"));

        await repo.RemoveAsync(n.Id);
        Assert.That(await repo.GetAsync(n.Id), Is.Null);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<Persistence.AppDbContext>
    {
        private readonly SqliteConnection _connection;
        public TestDbContextFactory(SqliteConnection connection) => _connection = connection;
        public Persistence.AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<Persistence.AppDbContext>().UseSqlite(_connection).Options);
    }
}
