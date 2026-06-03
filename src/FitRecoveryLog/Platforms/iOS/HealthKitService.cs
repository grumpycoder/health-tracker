using FitRecoveryLog.Services;
using Foundation;
using HealthKit;

namespace FitRecoveryLog;

/// <summary>iOS HealthKit implementation of <see cref="IHealthService"/>.</summary>
public sealed class HealthKitService : IHealthService
{
    private readonly HKHealthStore? _store = HKHealthStore.IsHealthDataAvailable ? new HKHealthStore() : null;
    private readonly HKQuantityType _bodyMass = HKQuantityType.Create(HKQuantityTypeIdentifier.BodyMass)!;
    private readonly HKQuantityType _steps = HKQuantityType.Create(HKQuantityTypeIdentifier.StepCount)!;
    private readonly HKCategoryType _sleep = HKCategoryType.Create(HKCategoryTypeIdentifier.SleepAnalysis)!;
    private static readonly HKUnit Pound = HKUnit.FromString("lb");
    private static readonly HKUnit Count = HKUnit.FromString("count");

    public bool IsAvailable => _store is not null;

    public Task<bool> RequestAuthorizationAsync()
    {
        if (_store is null) return Task.FromResult(false);
        var share = new NSSet<HKSampleType>(_bodyMass);
        var read = new NSSet<HKObjectType>(_bodyMass, _steps, _sleep);
        var tcs = new TaskCompletionSource<bool>();
        _store.RequestAuthorizationToShare(share, read, (ok, _) => tcs.TrySetResult(ok));
        return tcs.Task;
    }

    public Task WriteWeightAsync(DateOnly date, double pounds, Guid sourceId)
    {
        if (_store is null) return Task.CompletedTask;
        var when = (NSDate)date.ToDateTime(new TimeOnly(12, 0)).ToUniversalTime();
        var quantity = HKQuantity.FromQuantity(Pound, pounds);
        var sample = HKQuantitySample.FromType(_bodyMass, quantity, when, when);
        var tcs = new TaskCompletionSource<bool>();
        _store.SaveObject(sample, (ok, _) => tcs.TrySetResult(ok));
        return tcs.Task;
    }

    public Task<IReadOnlyList<(DateOnly, double)>> ReadWeightsAsync(DateTime since)
    {
        var empty = (IReadOnlyList<(DateOnly, double)>)Array.Empty<(DateOnly, double)>();
        if (_store is null) return Task.FromResult(empty);

        var ownBundleId = NSBundle.MainBundle.BundleIdentifier;
        var predicate = HKQuery.GetPredicateForSamples((NSDate)since.ToUniversalTime(), null, HKQueryOptions.None);
        var tcs = new TaskCompletionSource<IReadOnlyList<(DateOnly, double)>>();
        var query = new HKSampleQuery(_bodyMass, predicate, 0, null, (_, results, _) =>
        {
            var list = new List<(DateOnly, double)>();
            if (results is not null)
            {
                foreach (var s in results.OfType<HKQuantitySample>())
                {
                    // Skip samples this app wrote (avoid re-importing our own data).
                    if (s.SourceRevision?.Source?.BundleIdentifier == ownBundleId) continue;
                    var lbs = s.Quantity.GetDoubleValue(Pound);
                    var date = DateOnly.FromDateTime(((DateTime)s.StartDate).ToLocalTime());
                    list.Add((date, lbs));
                }
            }
            tcs.TrySetResult(list.OrderByDescending(x => x.Item1).ToList());
        });
        _store.ExecuteQuery(query);
        return tcs.Task;
    }

    public Task<int?> ReadStepsAsync(DateOnly date)
    {
        if (_store is null) return Task.FromResult<int?>(null);
        var start = (NSDate)date.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var end = (NSDate)date.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var predicate = HKQuery.GetPredicateForSamples(start, end, HKQueryOptions.StrictStartDate);
        var tcs = new TaskCompletionSource<int?>();
        var query = new HKStatisticsQuery(_steps, predicate, HKStatisticsOptions.CumulativeSum, (_, result, _) =>
        {
            var sum = result?.SumQuantity();
            tcs.TrySetResult(sum is null ? null : (int)sum.GetDoubleValue(Count));
        });
        _store.ExecuteQuery(query);
        return tcs.Task;
    }

    public Task<IReadOnlyList<(DateOnly, double)>> ReadSleepAsync(DateTime since)
    {
        var empty = (IReadOnlyList<(DateOnly, double)>)Array.Empty<(DateOnly, double)>();
        if (_store is null) return Task.FromResult(empty);

        var predicate = HKQuery.GetPredicateForSamples((NSDate)since.ToUniversalTime(), null, HKQueryOptions.None);
        var tcs = new TaskCompletionSource<IReadOnlyList<(DateOnly, double)>>();
        var query = new HKSampleQuery(_sleep, predicate, 0, null, (_, results, _) =>
        {
            // Aggregate per night, keyed by the local date the sample ended (wake day).
            // Asleep stages: AsleepUnspecified(1), AsleepCore(3), AsleepDeep(4), AsleepREM(5).
            // InBed(0)/Awake(2) excluded; fall back to InBed only if no asleep stages.
            var asleep = new Dictionary<DateOnly, double>();
            var inBed = new Dictionary<DateOnly, double>();
            if (results is not null)
            {
                foreach (var s in results.OfType<HKCategorySample>())
                {
                    var start = ((DateTime)s.StartDate).ToLocalTime();
                    var end = ((DateTime)s.EndDate).ToLocalTime();
                    var night = DateOnly.FromDateTime(end);
                    var hours = (end - start).TotalHours;
                    if (hours <= 0) continue;
                    var v = s.Value;
                    if (v == 1 || v == 3 || v == 4 || v == 5)
                        asleep[night] = (asleep.TryGetValue(night, out var a) ? a : 0) + hours;
                    else if (v == 0)
                        inBed[night] = (inBed.TryGetValue(night, out var b) ? b : 0) + hours;
                }
            }
            var nights = asleep.Keys.Union(inBed.Keys);
            var list = nights
                .Select(n => (n, Hours: asleep.TryGetValue(n, out var a) && a > 0 ? a : inBed.GetValueOrDefault(n)))
                .Where(x => x.Hours > 0)
                .OrderByDescending(x => x.n)
                .ToList();
            tcs.TrySetResult(list);
        });
        _store.ExecuteQuery(query);
        return tcs.Task;
    }
}
