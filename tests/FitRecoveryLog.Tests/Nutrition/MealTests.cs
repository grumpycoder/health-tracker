using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Application.Nutrition;
using FitRecoveryLog.Domain.Common;
using FitRecoveryLog.Domain.Nutrition;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Nutrition;

[TestFixture]
public class MealTests
{
    [Test]
    public void Create_TrimsDescription()
    {
        var m = Meal.Create(DateTime.Now, MealType.Lunch, "  Chicken bowl  ");
        Assert.That(m.Description, Is.EqualTo("Chicken bowl"));
    }

    [TestCase(0, null)]
    [TestCase(6, null)]
    [TestCase(3, 3)]
    public void QualityStars_OnlyOneToFive(int input, int? expected)
    {
        var m = Meal.Create(DateTime.Now, MealType.Snack);
        m.SetQualityStars(input);
        Assert.That(m.QualityStars, Is.EqualTo(expected));
    }

    [Test]
    public void SetMacros_And_Tags_AreCarried()
    {
        var m = Meal.Create(DateTime.Now, MealType.Dinner);
        m.SetMacros(new Macros(500, 40, 30, 20, 5, 6, 3, 400));
        m.SetTags(Tags.FromCsv("High protein, restaurant, High Protein"));
        Assert.Multiple(() =>
        {
            Assert.That(m.Macros.Calories, Is.EqualTo(500));
            Assert.That(m.Tags.Values, Has.Count.EqualTo(2)); // de-duped case-insensitively
        });
    }
}

[TestFixture]
public class MealServiceTests
{
    private FakeMealRepository _repo = null!;
    private MealService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = new FakeMealRepository();
        _service = new MealService(_repo);
    }

    private static MealData Data() => new(DateTime.Now, MealType.Lunch, "Bowl", null, Satiety.Satisfied, 4,
        new Macros(500, 40, 30, 20, 5, 6, 3, 400), Tags.FromCsv("High protein"));

    [Test]
    public async Task Create_Persists_WithMacros()
    {
        var result = await _service.CreateAsync(Data());
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_repo.Store[result.Value].Macros.Calories, Is.EqualTo(500));
            Assert.That(_repo.Store[result.Value].QualityStars, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task Update_Unknown_Fails()
    {
        var result = await _service.UpdateAsync(Guid.NewGuid(), Data());
        Assert.That(result.IsSuccess, Is.False);
    }

    private sealed class FakeMealRepository : IMealRepository
    {
        public readonly Dictionary<Guid, Meal> Store = new();
        public Task<Meal?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Store.GetValueOrDefault(id));
        public Task<IReadOnlyList<Meal>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Meal>>(Store.Values.ToList());
        public Task SaveAsync(Meal meal, CancellationToken ct = default) { Store[meal.Id] = meal; return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken ct = default) { Store.Remove(id); return Task.CompletedTask; }
    }
}
