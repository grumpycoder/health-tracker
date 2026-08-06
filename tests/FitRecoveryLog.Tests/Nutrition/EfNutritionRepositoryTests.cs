using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Nutrition;
using FitRecoveryLog.Infrastructure.Nutrition;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Tests.Nutrition;

/// <summary>Integration tests for the EF meal/drink repositories against in-memory SQLite,
/// exercising the Macros/Tags value-object and domain-enum mapping.</summary>
[TestFixture]
public class EfNutritionRepositoryTests
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
    public async Task Meal_Save_ThenGet_RoundTrips()
    {
        var repo = new EfMealRepository(_factory);
        var meal = Meal.Create(new DateTime(2026, 8, 5, 12, 30, 0), MealType.Snack, "Chicken bowl");
        // Macros arg order: calories, protein, carbs, fat, fiber, sugar, addedSugar, sodium.
        meal.SetMacros(new Macros(500, 40, 50, 15, 8, 10, 2, 600));
        meal.SetTags(Tags.FromCsv("High protein,Home-cooked"));
        meal.SetSatiety(Satiety.Satisfied);
        meal.SetQualityStars(4);
        await repo.SaveAsync(meal);

        var loaded = await repo.GetAsync(meal.Id);

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.MealType, Is.EqualTo(MealType.Snack));
            Assert.That(loaded.Satiety, Is.EqualTo(Satiety.Satisfied));
            Assert.That(loaded.QualityStars, Is.EqualTo(4));
            Assert.That(loaded.Macros.Calories, Is.EqualTo(500));
            Assert.That(loaded.Macros.ProteinG, Is.EqualTo(40));
            Assert.That(loaded.Macros.SodiumMg, Is.EqualTo(600));
            Assert.That(loaded.Macros.AddedSugarG, Is.EqualTo(2));
            Assert.That(loaded.Tags.Values, Is.EquivalentTo(new[] { "High protein", "Home-cooked" }));
        });
    }

    [Test]
    public async Task Drink_Save_ThenGet_RoundTrips()
    {
        var repo = new EfDrinkRepository(_factory);
        var drink = Drink.Create(new DateTime(2026, 8, 5, 9, 0, 0), "Protein shake");
        drink.SetOunces(16);
        drink.SetSugarCount(0);
        drink.SetMacros(new Macros(160, 30, 5, 2, 1, 3, 0, 200));
        drink.SetTags(Tags.FromCsv("High protein"));
        await repo.SaveAsync(drink);

        var loaded = await repo.GetAsync(drink.Id);

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Ounces, Is.EqualTo(16));
            Assert.That(loaded.Macros.Calories, Is.EqualTo(160));
            Assert.That(loaded.Macros.ProteinG, Is.EqualTo(30));
            Assert.That(loaded.Tags.Values, Is.EquivalentTo(new[] { "High protein" }));
        });
    }

    [Test]
    public async Task Meal_Remove_SoftDeletes()
    {
        var repo = new EfMealRepository(_factory);
        var meal = Meal.Create(new DateTime(2026, 8, 5, 12, 0, 0), MealType.Snack, "Bar");
        await repo.SaveAsync(meal);

        await repo.RemoveAsync(meal.Id);

        Assert.That(await repo.GetAsync(meal.Id), Is.Null);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<Persistence.AppDbContext>
    {
        private readonly SqliteConnection _connection;
        public TestDbContextFactory(SqliteConnection connection) => _connection = connection;
        public Persistence.AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<Persistence.AppDbContext>().UseSqlite(_connection).Options);
    }
}
