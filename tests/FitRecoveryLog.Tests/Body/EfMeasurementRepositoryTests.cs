using FitRecoveryLog.Data;
using FitRecoveryLog.Domain.Body;
using FitRecoveryLog.Infrastructure.Body;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Body;

/// <summary>Integration tests for the EF measurement repository against in-memory SQLite.</summary>
[TestFixture]
public class EfMeasurementRepositoryTests
{
    private SqliteConnection _connection = null!;
    private TestDbContextFactory _factory = null!;
    private EfMeasurementRepository _repo = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        using (var db = _factory.CreateDbContext()) db.Database.EnsureCreated();
        _repo = new EfMeasurementRepository(_factory);
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    [Test]
    public async Task Save_ThenGet_RoundTrips()
    {
        var m = Measurement.Create(new DateOnly(2026, 8, 5));
        m.Update(185.4, 34.0, null, null, null, null, 18.5, null, null, null, null, null, "belt one notch tighter");
        await _repo.SaveAsync(m);

        var loaded = await _repo.GetAsync(m.Id);

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.WeightLbs, Is.EqualTo(185.4));
            Assert.That(loaded.WaistInches, Is.EqualTo(34.0));
            Assert.That(loaded.BodyFatPercent, Is.EqualTo(18.5));
            Assert.That(loaded.ClothingFitNotes, Is.EqualTo("belt one notch tighter"));
        });
    }

    [Test]
    public async Task Update_PreservesPhotoPath()
    {
        // A progress photo lives on the same row but isn't part of the aggregate.
        var id = Guid.NewGuid();
        using (var db = _factory.CreateDbContext())
        {
            db.BodyMeasurements.Add(new BodyMeasurement { Id = id, Date = new DateOnly(2026, 8, 5), PhotoPath = "abc.jpg" });
            await db.SaveChangesAsync();
        }

        // Update the measurement through the aggregate (adds a weight).
        var m = await _repo.GetAsync(id);
        m!.Update(190, null, null, null, null, null, null, null, null, null, null, null, null);
        await _repo.SaveAsync(m);

        using var verify = _factory.CreateDbContext();
        var row = verify.BodyMeasurements.Single(x => x.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(row.WeightLbs, Is.EqualTo(190));
            Assert.That(row.PhotoPath, Is.EqualTo("abc.jpg"), "photo must survive a measurement update");
        });
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly SqliteConnection _connection;
        public TestDbContextFactory(SqliteConnection connection) => _connection = connection;
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
    }
}
