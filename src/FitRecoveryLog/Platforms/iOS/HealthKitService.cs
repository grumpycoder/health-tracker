using FitRecoveryLog.Services;
using Foundation;
using HealthKit;

namespace FitRecoveryLog;

/// <summary>iOS HealthKit implementation of <see cref="IHealthService"/>.</summary>
public sealed class HealthKitService : IHealthService
{
    private readonly HKHealthStore? _store = HKHealthStore.IsHealthDataAvailable ? new HKHealthStore() : null;
    private readonly HKQuantityType _bodyMass = HKQuantityType.Create(HKQuantityTypeIdentifier.BodyMass)!;
    private readonly HKQuantityType _waist = HKQuantityType.Create(HKQuantityTypeIdentifier.WaistCircumference)!;
    private readonly HKQuantityType _steps = HKQuantityType.Create(HKQuantityTypeIdentifier.StepCount)!;
    private readonly HKCategoryType _sleep = HKCategoryType.Create(HKCategoryTypeIdentifier.SleepAnalysis)!;
    private readonly HKQuantityType _activeEnergy = HKQuantityType.Create(HKQuantityTypeIdentifier.ActiveEnergyBurned)!;
    private static readonly HKUnit Pound = HKUnit.FromString("lb");
    private static readonly HKUnit Inch = HKUnit.FromString("in");
    private static readonly HKUnit Count = HKUnit.FromString("count");
    private static readonly HKUnit Kcal = HKUnit.FromString("kcal");
    // Active-energy estimate so strength sessions credit the Activity rings when no
    // Apple Watch workout measured them. Scaled by body weight via the MET formula
    // (kcal/min = MET × 3.5 × kg / 200); MET ~5 for traditional strength training.
    private const double StrengthMet = 5.0;
    private const double FallbackKcalPerMinute = 6.0; // when body weight is unknown

    private static double KcalPerMinute(double? bodyWeightLbs) =>
        bodyWeightLbs is { } lbs && lbs > 0
            ? StrengthMet * 3.5 * (lbs * 0.453592) / 200.0
            : FallbackKcalPerMinute;

    public bool IsAvailable => _store is not null;

    public Task<bool> RequestAuthorizationAsync()
    {
        if (_store is null) return Task.FromResult(false);
        var share = new NSSet<HKSampleType>(_bodyMass, _waist, _activeEnergy, HKObjectType.WorkoutType);
        var read = new NSSet<HKObjectType>(_bodyMass, _waist, _steps, _sleep, _activeEnergy, HKObjectType.WorkoutType);
        var tcs = new TaskCompletionSource<bool>();
        _store.RequestAuthorizationToShare(share, read, (ok, _) => tcs.TrySetResult(ok));
        return tcs.Task;
    }

    public Task WriteWeightAsync(DateOnly date, double pounds, Guid sourceId) =>
        WriteQuantityAsync(_bodyMass, Pound, date, pounds);

    public Task WriteWaistAsync(DateOnly date, double inches, Guid sourceId) =>
        WriteQuantityAsync(_waist, Inch, date, inches);

    public Task<IReadOnlyList<(DateOnly, double)>> ReadWeightsAsync(DateTime since) =>
        ReadQuantitiesAsync(_bodyMass, Pound, since);

    public Task<IReadOnlyList<(DateOnly, double)>> ReadWaistsAsync(DateTime since) =>
        ReadQuantitiesAsync(_waist, Inch, since);

    private Task WriteQuantityAsync(HKQuantityType type, HKUnit unit, DateOnly date, double value)
    {
        if (_store is null) return Task.CompletedTask;
        var when = (NSDate)date.ToDateTime(new TimeOnly(12, 0)).ToUniversalTime();
        var quantity = HKQuantity.FromQuantity(unit, value);
        var sample = HKQuantitySample.FromType(type, quantity, when, when);
        var tcs = new TaskCompletionSource<bool>();
        _store.SaveObject(sample, (ok, _) => tcs.TrySetResult(ok));
        return tcs.Task;
    }

    private Task<IReadOnlyList<(DateOnly, double)>> ReadQuantitiesAsync(HKQuantityType type, HKUnit unit, DateTime since)
    {
        var empty = (IReadOnlyList<(DateOnly, double)>)Array.Empty<(DateOnly, double)>();
        if (_store is null) return Task.FromResult(empty);

        var ownBundleId = NSBundle.MainBundle.BundleIdentifier;
        var predicate = HKQuery.GetPredicateForSamples((NSDate)since.ToUniversalTime(), null, HKQueryOptions.None);
        var tcs = new TaskCompletionSource<IReadOnlyList<(DateOnly, double)>>();
        var query = new HKSampleQuery(type, predicate, 0, null, (_, results, _) =>
        {
            var list = new List<(DateOnly, double)>();
            if (results is not null)
            {
                foreach (var s in results.OfType<HKQuantitySample>())
                {
                    // Skip samples this app wrote (avoid re-importing our own data).
                    if (s.SourceRevision?.Source?.BundleIdentifier == ownBundleId) continue;
                    var value = s.Quantity.GetDoubleValue(unit);
                    var date = DateOnly.FromDateTime(((DateTime)s.StartDate).ToLocalTime());
                    list.Add((date, value));
                }
            }
            tcs.TrySetResult(list.OrderByDescending(x => x.Item1).ToList());
        });
        _store.ExecuteQuery(query);
        return tcs.Task;
    }

    public async Task WriteWorkoutAsync(DateTime start, DateTime end, string name, double? bodyWeightLbs)
    {
        if (_store is null) return;

        // If a workout from another source (the Apple Watch) already covers this
        // window, it has real HR/energy — don't write a duplicate or a guessed
        // estimate on top of it.
        if (await HasExternalWorkoutAsync(start, end)) return;

        var config = new HKWorkoutConfiguration { ActivityType = HKWorkoutActivityType.TraditionalStrengthTraining };
        var builder = new HKWorkoutBuilder(_store, config, HKDevice.LocalDevice);
        await builder.BeginCollectionAsync((NSDate)start.ToUniversalTime());
        try
        {
            // Label the workout with the routine name where Fitness shows a brand.
            var meta = new HKMetadata { WorkoutBrandName = name };
            var mtcs = new TaskCompletionSource<bool>();
            builder.Add(meta, (ok, _) => mtcs.TrySetResult(ok));
            await mtcs.Task;
        }
        catch { /* metadata is cosmetic */ }

        // Estimated active energy so the rings get credit (no Watch measured this).
        try
        {
            var minutes = Math.Max(0, (end - start).TotalMinutes);
            if (minutes > 0)
            {
                var kcal = HKQuantity.FromQuantity(Kcal, minutes * KcalPerMinute(bodyWeightLbs));
                var energy = HKQuantitySample.FromType(_activeEnergy, kcal,
                    (NSDate)start.ToUniversalTime(), (NSDate)end.ToUniversalTime());
                var etcs = new TaskCompletionSource<bool>();
                builder.Add(new HKSample[] { energy }, (ok, _) => etcs.TrySetResult(ok));
                await etcs.Task;
            }
        }
        catch { /* estimate is best-effort */ }

        await builder.EndCollectionAsync((NSDate)end.ToUniversalTime());
        var tcs = new TaskCompletionSource<bool>();
        builder.FinishWorkout((_, error) => tcs.TrySetResult(error is null));
        await tcs.Task;
    }

    /// <summary>True if a workout NOT authored by this app overlaps [start, end] —
    /// i.e. the user started one on their Apple Watch.</summary>
    private Task<bool> HasExternalWorkoutAsync(DateTime start, DateTime end)
    {
        if (_store is null) return Task.FromResult(false);
        var ownBundleId = NSBundle.MainBundle.BundleIdentifier;
        // Overlap: workout starts before our end AND ends after our start.
        var predicate = HKQuery.GetPredicateForSamples(
            (NSDate)start.ToUniversalTime(), (NSDate)end.ToUniversalTime(), HKQueryOptions.None);
        var tcs = new TaskCompletionSource<bool>();
        var query = new HKSampleQuery(HKObjectType.WorkoutType, predicate, 1, null, (_, results, _) =>
        {
            var external = results?.OfType<HKWorkout>()
                .Any(w => w.SourceRevision?.Source?.BundleIdentifier != ownBundleId) ?? false;
            tcs.TrySetResult(external);
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

    private sealed class NightAgg
    {
        public double Asleep, InBed, DeepRem;
        public int Awake;
        public bool Stages;
        public readonly List<(DateTime Start, DateTime End)> AsleepSpans = new();
    }

    /// <summary>Interruptions for sources without explicit awake samples: gaps of
    /// 5min–2h between merged asleep spans within the night.</summary>
    private static int GapInterruptions(List<(DateTime Start, DateTime End)> spans)
    {
        if (spans.Count < 2) return 0;
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var gaps = 0;
        var end = spans[0].End;
        foreach (var s in spans.Skip(1))
        {
            var gap = (s.Start - end).TotalMinutes;
            if (gap is >= 5 and <= 120) gaps++;
            if (s.End > end) end = s.End;
        }
        return gaps;
    }

    public Task<IReadOnlyList<SleepNight>> ReadSleepAsync(DateTime since)
    {
        var empty = (IReadOnlyList<SleepNight>)Array.Empty<SleepNight>();
        if (_store is null) return Task.FromResult(empty);

        var predicate = HKQuery.GetPredicateForSamples((NSDate)since.ToUniversalTime(), null, HKQueryOptions.None);
        var tcs = new TaskCompletionSource<IReadOnlyList<SleepNight>>();
        var query = new HKSampleQuery(_sleep, predicate, 0, null, (_, results, _) =>
        {
            // Aggregate per night, keyed by the local date the sample ended (wake day).
            // Values: InBed(0), AsleepUnspecified(1), Awake(2), AsleepCore(3),
            // AsleepDeep(4), AsleepREM(5). Stage detail (3-5 or awake gaps) is what
            // lets us estimate interruptions and a score; a bare InBed/Unspecified
            // block doesn't.
            var agg = new Dictionary<DateOnly, NightAgg>();
            if (results is not null)
            {
                foreach (var s in results.OfType<HKCategorySample>())
                {
                    var start = ((DateTime)s.StartDate).ToLocalTime();
                    var end = ((DateTime)s.EndDate).ToLocalTime();
                    // Key by wake day. Stage samples are individual segments, so the
                    // pre-midnight ones must roll FORWARD to the same night as the
                    // morning ones (ending in the evening = belongs to tomorrow's wake).
                    var night = DateOnly.FromDateTime(end.Hour >= 18 ? end.AddDays(1) : end);
                    var hours = (end - start).TotalHours;
                    if (hours <= 0) continue;
                    if (!agg.TryGetValue(night, out var n)) agg[night] = n = new NightAgg();
                    switch (s.Value)
                    {
                        case 0: n.InBed += hours; break;
                        case 1: n.Asleep += hours; n.AsleepSpans.Add((start, end)); break;
                        case 2: n.Awake++; n.Stages = true; break;
                        case 3: n.Asleep += hours; n.Stages = true; n.AsleepSpans.Add((start, end)); break;
                        case 4 or 5: n.Asleep += hours; n.DeepRem += hours; n.Stages = true; n.AsleepSpans.Add((start, end)); break;
                    }
                }
            }
            var list = agg
                .Select(kv =>
                {
                    var n = kv.Value;
                    // Explicit awake samples when present; otherwise count the gaps
                    // between asleep segments (sources without stage tracking).
                    var gapCount = GapInterruptions(n.AsleepSpans);
                    var interruptions = Math.Max(n.Awake, gapCount);
                    var hasDetail = n.Stages || n.AsleepSpans.Count > 1;
                    return new SleepNight(kv.Key,
                        n.Asleep > 0 ? n.Asleep : n.InBed,
                        interruptions, n.DeepRem, hasDetail);
                })
                .Where(x => x.Hours > 0)
                .OrderByDescending(x => x.Date)
                .ToList();
            tcs.TrySetResult(list);
        });
        _store.ExecuteQuery(query);
        return tcs.Task;
    }
}
