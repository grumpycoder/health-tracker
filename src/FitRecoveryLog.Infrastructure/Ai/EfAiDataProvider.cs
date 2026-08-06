using System.Text;
using FitRecoveryLog.Application.Ai;
using FitRecoveryLog.Data;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Infrastructure.Ai;

/// <summary>
/// Phone implementation of <see cref="IAiDataProvider"/>: reads the local SQLite database (via EF)
/// and formats the day's context for AI prompts. Lives here (not the app project) so the shared
/// coach stays free of EF and of the non-migrated entities these prompts touch (daily logs,
/// physical workload, cessation). The nutrition roll-ups are computed inline to avoid a dependency
/// on the app's display-side helper.
/// </summary>
public sealed class EfAiDataProvider : IAiDataProvider
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public EfAiDataProvider(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<string> GetTodayContextAsync(bool includeCessation, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var sb = new StringBuilder();

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var dayStart = today.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        sb.AppendLine($"NOW: {now:yyyy-MM-dd HH:mm} ({now.DayOfWeek})");

        var day = await db.DailyLogs.FirstOrDefaultAsync(x => x.Date == today, ct);
        sb.AppendLine($"PLANNED DAY TYPE: {(day is null || day.DayType == DayType.Unset ? "(not set)" : day.DayType.ToString())}");

        sb.AppendLine("TODAY'S NOTES:");
        var notes = await db.NoteEntries.Where(n => n.Time >= dayStart && n.Time < dayEnd)
            .OrderBy(n => n.Time).ToListAsync(ct);
        if (notes.Count == 0) sb.AppendLine("(none)");
        foreach (var n in notes)
            sb.AppendLine($"  {n.Time:HH:mm} \"{n.Text}\"");

        var sleep = await db.SleepEntries.FirstOrDefaultAsync(s => s.Date == today, ct);
        sb.AppendLine("SLEEP (last night): " + (sleep is null
            ? "(not logged)"
            : $"{sleep.DurationHours:0.#}h score:{sleep.SleepScore}{(sleep.ScoreEstimated ? "(rough estimate, not Apple's)" : "")} interruptions:{sleep.Interruptions}" +
              (string.IsNullOrWhiteSpace(sleep.Notes) ? "" : $" note:\"{sleep.Notes}\"")));

        sb.AppendLine("WORKOUT TODAY:");
        var sessions = await db.WorkoutSessions.Where(s => s.Date == today)
            .Include(s => s.Sets).ThenInclude(x => x.ExerciseDefinition).ToListAsync(ct);
        if (sessions.Count == 0) sb.AppendLine("(none yet)");
        foreach (var s in sessions)
            sb.AppendLine($"  {(s.TotalSeconds ?? 0) / 60}min, {s.Sets.Count(x => x.Completed)}/{s.Sets.Count} sets" +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));

        sb.AppendLine("MEALS TODAY:");
        var meals = await db.MealEntries.Where(m => m.Time >= dayStart && m.Time < dayEnd).OrderBy(m => m.Time).ToListAsync(ct);
        if (meals.Count == 0) sb.AppendLine("(none yet)");
        foreach (var m in meals)
            sb.AppendLine($"  {m.Time:HH:mm} {m.MealType} \"{m.Description}\"" +
                          (string.IsNullOrWhiteSpace(m.PortionNote) ? "" : $" portion:\"{m.PortionNote}\"") +
                          (m.TagList.Count == 0 ? "" : $" tags:{string.Join("/", m.TagList)}") +
                          (m.Satiety == Satiety.Unset ? "" : $" satiety:{m.Satiety}"));

        sb.AppendLine("DRINKS TODAY:");
        var drinks = await db.DrinkEntries.Where(d => d.Time >= dayStart && d.Time < dayEnd).OrderBy(d => d.Time).ToListAsync(ct);
        if (drinks.Count == 0) sb.AppendLine("(none yet)");
        foreach (var d in drinks)
            sb.AppendLine($"  {d.Time:HH:mm} \"{d.Description}\"" +
                          (d.Ounces is { } oz ? $" {oz:0.#}oz" : "") +
                          (d.SugarCount is { } su ? $" sugar:{su}" : ""));

        // Running totals (only items logged with macros). Coffee teaspoons (~4g each) count as
        // sugar/added-sugar; scanned SugarG wins for total sugar when present.
        int cal = meals.Sum(m => m.Calories ?? 0) + drinks.Sum(d => d.Calories ?? 0);
        double protein = meals.Sum(m => m.ProteinG ?? 0) + drinks.Sum(d => d.ProteinG ?? 0);
        double carbs = meals.Sum(m => m.CarbsG ?? 0) + drinks.Sum(d => d.CarbsG ?? 0);
        double fat = meals.Sum(m => m.FatG ?? 0) + drinks.Sum(d => d.FatG ?? 0);
        double fiber = meals.Sum(m => m.FiberG ?? 0) + drinks.Sum(d => d.FiberG ?? 0);
        double addedSugar = meals.Sum(m => m.AddedSugarG ?? 0)
            + drinks.Sum(d => (d.AddedSugarG ?? 0) + (d.SugarCount ?? 0) * 4.0);
        int fluidOz = (int)Math.Round(drinks.Sum(d => d.Ounces ?? 0));
        sb.AppendLine("TODAY'S TOTALS SO FAR (only counts items logged with macros — may be incomplete):");
        sb.AppendLine($"  Calories: {cal}");
        sb.AppendLine($"  Protein: {protein:0} g");
        sb.AppendLine($"  Carbs: {carbs:0} g");
        sb.AppendLine($"  Fat: {fat:0} g");
        sb.AppendLine($"  Fiber: {fiber:0} g");
        sb.AppendLine($"  Added sugar: {addedSugar:0} g");
        sb.AppendLine($"  Fluids (all drinks): {fluidOz} oz");
        sb.AppendLine("Compare these to the DAILY MACRO/HYDRATION TARGETS above and note where the day is " +
                      "tracking under/in/over range — but remember totals may be incomplete if not everything " +
                      "was logged with macros, so don't scold a low number that's just unlogged food.");

        sb.AppendLine("PHYSICAL WORKLOAD TODAY:");
        var work = await db.PhysicalWorkloadEntries.Where(w => w.Date == today).ToListAsync(ct);
        if (work.Count == 0) sb.AppendLine("(none)");
        foreach (var w in work)
            sb.AppendLine($"  {w.Activity} {(w.DurationMinutes is { } mins ? $"{mins}min " : "")}{w.Intensity}");

        sb.AppendLine("LAST 7 DAYS (pattern context — cite specific entries when claiming a trend; never extrapolate a pattern the data doesn't show):");
        var weekStart = dayStart.AddDays(-7);
        var weekMeals = await db.MealEntries.Where(m => m.Time >= weekStart && m.Time < dayStart).ToListAsync(ct);
        var weekDrinks = await db.DrinkEntries.Where(d => d.Time >= weekStart && d.Time < dayStart).ToListAsync(ct);
        for (var d = today.AddDays(-7); d < today; d = d.AddDays(1))
        {
            var ds = d.ToDateTime(TimeOnly.MinValue);
            var de = ds.AddDays(1);
            var dayMeals = weekMeals.Where(m => m.Time >= ds && m.Time < de).OrderBy(m => m.Time)
                .Select(m => Trunc(m.Description, 45)).ToList();
            var oz = weekDrinks.Where(x => x.Time >= ds && x.Time < de).Sum(x => x.Ounces ?? 0);
            sb.AppendLine($"  {d:MM-dd}: {(dayMeals.Count == 0 ? "(no meals logged)" : string.Join("; ", dayMeals))}" +
                          (oz > 0 ? $" | {oz:0}oz drinks" : ""));
        }

        if (includeCessation)
        {
            var goals = await db.CessationGoals.Where(g => g.Active).ToListAsync(ct);
            if (goals.Count > 0)
            {
                sb.AppendLine("CESSATION GOALS (user opted in — be supportive; never judgmental about slips):");
                foreach (var g in goals)
                {
                    var todayEvents = await db.CessationEvents
                        .Where(e => e.GoalId == g.Id && e.Time >= dayStart && e.Time < dayEnd).ToListAsync(ct);
                    var cravings = todayEvents.Count(e => e.Type == CessationEventType.Craving);
                    var usedToday = todayEvents.Where(e => e.Type == CessationEventType.Slip).Sum(e => e.Amount ?? 1);

                    if (g.Taper && g.TaperStartDate is { } start && today < g.QuitDate)
                    {
                        var total = g.QuitDate.DayNumber - start.DayNumber;
                        var allow = g.BaselineUnitsPerDay is { } b && total > 0
                            ? (int)Math.Ceiling(b * (g.QuitDate.DayNumber - today.DayNumber) / (double)total) : 0;
                        sb.AppendLine($"  {g.Substance}: tapering, quit day {g.QuitDate:yyyy-MM-dd}; " +
                                      $"today used {usedToday:0.#} of {allow} allowed, {cravings} craving(s)");
                        continue;
                    }

                    var daysQuit = today.DayNumber - g.QuitDate.DayNumber;
                    var lastSlip = await db.CessationEvents
                        .Where(e => e.GoalId == g.Id && e.Type == CessationEventType.Slip)
                        .OrderByDescending(e => e.Time).FirstOrDefaultAsync(ct);
                    sb.AppendLine($"  {g.Substance}: quit {daysQuit} day(s) ago; today " +
                                  $"{cravings} craving(s), {usedToday:0.#} slip unit(s)" +
                                  (lastSlip is null ? "; no slips ever" : $"; last slip {DateOnly.FromDateTime(lastSlip.Time):yyyy-MM-dd}"));
                }
            }
        }

        return sb.ToString();

        static string Trunc(string s, int len) => s.Length <= len ? s : s[..(len - 1)] + "…";
    }

    public async Task<string> GetEightWeekContextAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);
        var sinceDt = since.ToDateTime(TimeOnly.MinValue);
        var sb = new StringBuilder();

        sb.AppendLine("WORKOUTS:");
        var sessions = await db.WorkoutSessions
            .Where(s => s.Date >= since)
            .Include(s => s.Routine)
            .Include(s => s.Sets).ThenInclude(x => x.ExerciseDefinition)
            .Include(s => s.Feedback).ThenInclude(f => f.ExerciseDefinition)
            .OrderBy(s => s.Date).ToListAsync(ct);
        if (sessions.Count == 0) sb.AppendLine("(none)");
        foreach (var s in sessions)
        {
            sb.AppendLine($"{s.Date:yyyy-MM-dd} {s.Routine?.Name ?? "Workout"} {(s.TotalSeconds ?? 0) / 60}min" +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));
            foreach (var g in s.Sets.GroupBy(x => x.ExerciseDefinition?.Name ?? "?"))
            {
                var fb = s.Feedback.FirstOrDefault(f => f.ExerciseDefinition?.Name == g.Key);
                var vals = string.Join(",", g.OrderBy(x => x.SetNumber)
                    .Select(x => x.DurationSeconds is { } t ? $"{t}s" : x.Reps?.ToString() ?? "-"));
                var done = g.Count(x => x.Completed);
                sb.AppendLine($"  {g.Key}: {vals} ({done}/{g.Count()} sets done)" +
                              (fb is null || fb.Difficulty == Difficulty.Unset ? "" : $" rated:{fb.Difficulty}") +
                              (string.IsNullOrWhiteSpace(fb?.Comment) ? "" : $" comment:\"{fb!.Comment}\""));
            }
        }

        sb.AppendLine();
        sb.AppendLine("MEALS (date time type description [portion] [tags] [satiety]):");
        var meals = await db.MealEntries.Where(m => m.Time >= sinceDt).OrderBy(m => m.Time).ToListAsync(ct);
        if (meals.Count == 0) sb.AppendLine("(none)");
        foreach (var m in meals)
            sb.AppendLine($"{m.Time:yyyy-MM-dd HH:mm} {m.MealType} \"{m.Description}\"" +
                          (string.IsNullOrWhiteSpace(m.PortionNote) ? "" : $" portion:\"{m.PortionNote}\"") +
                          (m.TagList.Count == 0 ? "" : $" tags:{string.Join("/", m.TagList)}") +
                          (m.Satiety == Satiety.Unset ? "" : $" satiety:{m.Satiety}"));

        sb.AppendLine();
        sb.AppendLine("DRINKS:");
        var drinks = await db.DrinkEntries.Where(d => d.Time >= sinceDt).OrderBy(d => d.Time).ToListAsync(ct);
        if (drinks.Count == 0) sb.AppendLine("(none)");
        foreach (var d in drinks)
            sb.AppendLine($"{d.Time:yyyy-MM-dd HH:mm} \"{d.Description}\"" +
                          (d.Ounces is { } oz ? $" {oz:0.#}oz" : "") +
                          (d.SugarCount is { } su ? $" sugar:{su}" : ""));

        var withOz = drinks.Where(d => d.Ounces is not null && !string.IsNullOrWhiteSpace(d.Description)).ToList();
        if (withOz.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("DRINK VOLUME BY WEEK (total oz, week 1 = oldest):");
            foreach (var g in withOz.GroupBy(d => d.Description.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var weeks = g.GroupBy(d => (DateOnly.FromDateTime(d.Time).DayNumber - since.DayNumber) / 7)
                    .OrderBy(w => w.Key)
                    .Select(w => $"wk{w.Key + 1}:{w.Sum(x => x.Ounces ?? 0):0}oz");
                sb.AppendLine($"- {g.Key}: {string.Join(" ", weeks)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("BODY MEASUREMENTS (numbers only):");
        var measurements = await db.BodyMeasurements.Where(m => m.Date >= since).OrderBy(m => m.Date).ToListAsync(ct);
        if (measurements.Count == 0) sb.AppendLine("(none)");
        foreach (var m in measurements)
            sb.AppendLine($"{m.Date:yyyy-MM-dd}" +
                          (m.WeightLbs is { } w ? $" weight:{w:0.#}lbs" : "") +
                          (m.WaistInches is { } wa ? $" waist:{wa:0.##}in" : "") +
                          (m.ChestInches is { } c ? $" chest:{c:0.##}in" : "") +
                          (m.ArmsInches is { } a ? $" arms:{a:0.##}in" : "") +
                          (m.ThighsInches is { } t ? $" thighs:{t:0.##}in" : ""));

        sb.AppendLine();
        sb.AppendLine("SLEEP:");
        var sleep = await db.SleepEntries.Where(s => s.Date >= since).OrderBy(s => s.Date).ToListAsync(ct);
        if (sleep.Count == 0) sb.AppendLine("(none)");
        foreach (var s in sleep)
            sb.AppendLine($"{s.Date:yyyy-MM-dd}" +
                          (s.DurationHours is { } h ? $" {h:0.#}h" : "") +
                          (s.SleepScore is { } sc ? $" score:{sc}{(s.ScoreEstimated ? "(rough estimate, not Apple's)" : "")}" : "") +
                          (s.Interruptions is { } i ? $" interruptions:{i}" : "") +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));

        return sb.ToString();
    }

    public async Task<string> GetRoutineDesignContextAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);
        var sb = new StringBuilder();

        sb.AppendLine("EXERCISE LIBRARY (name | measure | muscles | equipment):");
        var defs = await db.ExerciseDefinitions.Where(e => !e.Retired).OrderBy(e => e.Name).ToListAsync(ct);
        if (defs.Count == 0) sb.AppendLine("(empty)");
        foreach (var d in defs)
            sb.AppendLine($"- {d.Name} | {(d.Measure == ExerciseMeasure.Duration ? "duration" : "reps")}" +
                          $" | {(string.IsNullOrWhiteSpace(d.MuscleGroups) ? "muscles unset" : d.MuscleGroups)}" +
                          (string.IsNullOrWhiteSpace(d.EquipmentNotes) ? "" : $" | {d.EquipmentNotes}"));

        sb.AppendLine();
        sb.AppendLine("EXISTING ROUTINES (do not duplicate these):");
        var routines = await db.WorkoutRoutines
            .Include(r => r.Exercises).ThenInclude(e => e.ExerciseDefinition)
            .Where(r => !r.Archived).ToListAsync(ct);
        if (routines.Count == 0) sb.AppendLine("(none)");
        foreach (var r in routines)
            sb.AppendLine($"- {r.Name}: {string.Join(", ", r.Exercises.OrderBy(e => e.Order).Select(e => e.ExerciseDefinition?.Name ?? "?"))}");

        sb.AppendLine();
        sb.AppendLine("TRAINING HISTORY (last 8 weeks; per exercise: sessions, best set, latest rating):");
        var sessions = await db.WorkoutSessions.Where(s => s.Date >= since)
            .Include(s => s.Sets).ThenInclude(x => x.ExerciseDefinition)
            .Include(s => s.Feedback).ThenInclude(f => f.ExerciseDefinition)
            .OrderByDescending(s => s.Date).ToListAsync(ct);
        var byExercise = sessions.SelectMany(s => s.Sets)
            .Where(x => x.ExerciseDefinition is not null)
            .GroupBy(x => x.ExerciseDefinition!.Name)
            .ToList();
        if (byExercise.Count == 0) sb.AppendLine("(none)");
        foreach (var g in byExercise)
        {
            var sessCount = sessions.Count(s => s.Sets.Any(x => x.ExerciseDefinition?.Name == g.Key));
            var bestSecs = g.Max(x => x.DurationSeconds ?? 0);
            var bestReps = g.Max(x => x.Reps ?? 0);
            var lastFb = sessions.SelectMany(s => s.Feedback)
                .FirstOrDefault(f => f.ExerciseDefinition?.Name == g.Key && f.Difficulty != Difficulty.Unset);
            sb.AppendLine($"- {g.Key}: {sessCount} session(s), best {(bestSecs > 0 ? $"{bestSecs}s" : $"{bestReps} reps")}" +
                          (lastFb is null ? "" : $", rated {lastFb.Difficulty}") +
                          (lastFb?.PainOrDiscomfort == true ? ", PAIN flagged" : ""));
        }

        var muscleByName = defs.ToDictionary(d => d.Name, d => d.MuscleGroupList, StringComparer.OrdinalIgnoreCase);
        var volume = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sessions.SelectMany(s => s.Sets))
        {
            var name = set.ExerciseDefinition?.Name;
            if (name is null || !muscleByName.TryGetValue(name, out var groups)) continue;
            foreach (var mg in groups) volume[mg] = volume.GetValueOrDefault(mg) + 1;
        }
        sb.AppendLine();
        if (volume.Count > 0)
        {
            sb.AppendLine("MUSCLE VOLUME (last 8 weeks, total sets per group — LOW groups are under-trained; prioritize them):");
            foreach (var kv in volume.OrderBy(x => x.Value))
                sb.AppendLine($"- {kv.Key}: {kv.Value} sets");
            var untouched = MuscleCatalog.Where(m => !volume.ContainsKey(m)).ToList();
            if (untouched.Count > 0)
                sb.AppendLine($"- Not trained at all: {string.Join(", ", untouched)}");
        }
        else
        {
            sb.AppendLine("MUSCLE VOLUME: unavailable — library exercises have no muscle-group tags yet. Infer muscle groups from exercise names instead.");
        }

        return sb.ToString();
    }

    public async Task<IReadOnlyCollection<string>> RecentlyEasyExercisesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);
        var sessions = await db.WorkoutSessions.Where(s => s.Date >= since)
            .Include(s => s.Feedback).ThenInclude(f => f.ExerciseDefinition)
            .OrderByDescending(s => s.Date).ToListAsync(ct);
        var latest = new Dictionary<string, Difficulty>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in sessions.SelectMany(s => s.Feedback))
            if (f.Difficulty != Difficulty.Unset && f.ExerciseDefinition?.Name is { } n && !latest.ContainsKey(n))
                latest[n] = f.Difficulty;
        return latest.Where(kv => kv.Value == Difficulty.Easy).Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private const int WindowDays = 56; // 8 weeks

    private static readonly string[] MuscleCatalog =
        { "Chest", "Back", "Shoulders", "Biceps", "Triceps", "Forearms", "Core", "Glutes", "Quads", "Hamstrings", "Calves" };
}
