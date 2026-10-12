using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Domain.Nutrition;
using FitRecoveryLog.Infrastructure.Nutrition;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Tests.Nutrition;

/// <summary>Saved-food aggregate, service, and EF repository (against in-memory SQLite).</summary>
[TestFixture]
public class SavedFoodTests
{
    // Macros arg order: calories, protein, carbs, fat, fiber, sugar, addedSugar, sodium.
    private static readonly Macros Egg = new(70, 6, 0.5, 5, 0, 0.2, 0, 70);
    private static readonly Macros Toast = new(80, 3, 15, 1, 2, 1.5, 1, 150);

    private SqliteConnection _connection = null!;
    private TestDbContextFactory _factory = null!;
    private EfSavedFoodRepository _repo = null!;
    private SavedFoodService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new TestDbContextFactory(_connection);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        _repo = new EfSavedFoodRepository(_factory);
        _service = new SavedFoodService(_repo);
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    [Test]
    public void Create_BlankName_Throws() =>
        Assert.Throws<ArgumentException>(() => SavedFood.Create("  ", Egg));

    [Test]
    public void Create_TrimsName_AndBlankServingNoteIsNull()
    {
        var food = SavedFood.Create("  Egg ", Egg, " ");
        Assert.Multiple(() =>
        {
            Assert.That(food.Name, Is.EqualTo("Egg"));
            Assert.That(food.ServingNote, Is.Null);
        });
    }

    [Test]
    public async Task Save_NewName_Creates_ThenRoundTripsMacros()
    {
        var result = await _service.SaveAsync("Egg", Egg, "1 large");

        var loaded = await _repo.GetAsync(result.Value);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(loaded!.Macros, Is.EqualTo(Egg));
            Assert.That(loaded.ServingNote, Is.EqualTo("1 large"));
        });
    }

    [Test]
    public async Task Save_ExistingNameDifferentCase_UpdatesSameRow()
    {
        var first = await _service.SaveAsync("Egg", Egg);
        var second = await _service.SaveAsync("  EGG ", Toast);

        var all = await _repo.ListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(second.Value, Is.EqualTo(first.Value));
            Assert.That(all, Has.Count.EqualTo(1));
            Assert.That(all[0].Macros, Is.EqualTo(Toast));
            Assert.That(all[0].Name, Is.EqualTo("Egg"));   // keeps the original spelling
        });
    }

    [Test]
    public async Task Save_BlankName_Fails()
    {
        var result = await _service.SaveAsync(" ", Egg);
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task FindByName_DuplicateRowsFromTwoDevices_NewestWins()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.SavedFoods.Add(new Persistence.SavedFoodEntry { Name = "Egg", Calories = 60, UpdatedAt = DateTime.UtcNow.AddHours(-1) });
            db.SavedFoods.Add(new Persistence.SavedFoodEntry { Name = "egg", Calories = 75, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var found = await _repo.FindByNameAsync("EGG");
        var list = await _repo.ListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(found!.Macros.Calories, Is.EqualTo(75));
            Assert.That(list, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Rename_MovesSavedMacrosToNewName()
    {
        await _service.SaveAsync("Eggs", Egg);

        await _service.RenameAsync("Eggs", "Egg");

        Assert.Multiple(async () =>
        {
            Assert.That(await _repo.FindByNameAsync("Eggs"), Is.Null);
            Assert.That((await _repo.FindByNameAsync("Egg"))!.Macros, Is.EqualTo(Egg));
        });
    }

    [Test]
    public async Task Rename_OntoExistingSavedFood_KeepsTargetAndRemovesOld()
    {
        await _service.SaveAsync("Eggs", Toast);
        await _service.SaveAsync("Egg", Egg);

        await _service.RenameAsync("Eggs", "Egg");

        var all = await _repo.ListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(1));
            Assert.That(all[0].Macros, Is.EqualTo(Egg));
        });
    }

    [Test]
    public async Task Rename_NoSavedFood_IsNoOpSuccess()
    {
        var result = await _service.RenameAsync("Eggs", "Egg");
        Assert.Multiple(async () =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(await _repo.ListAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task Reset_TombstonesRow_SoFoodFallsBackToHistory()
    {
        await _service.SaveAsync("Egg", Egg);

        var result = await _service.ResetAsync("egg");

        using var db = _factory.CreateDbContext();
        Assert.Multiple(async () =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(await _repo.FindByNameAsync("Egg"), Is.Null);
            Assert.That(db.SavedFoods.IgnoreQueryFilters().Single().IsDeleted, Is.True);
        });
    }

    private sealed class TestDbContextFactory : IDbContextFactory<Persistence.AppDbContext>
    {
        private readonly SqliteConnection _connection;
        public TestDbContextFactory(SqliteConnection connection) => _connection = connection;
        public Persistence.AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<Persistence.AppDbContext>().UseSqlite(_connection).Options);
    }
}
