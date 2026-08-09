using System.Text;
using FitRecoveryLog.Application.Ai;
using FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>
/// Web <see cref="IAiDataProvider"/> over the sync-pull cache (no EF). Mirrors the phone's
/// today-context format so the shared coach's daily check-in behaves identically. The heavier
/// windows (8-week analysis, routine design) aren't wired on the web yet and throw rather than
/// send a partial prompt. Cessation is only rendered when the user opts in (web withholds it).
/// </summary>
public sealed class WebAiDataProvider : IAiDataProvider
{
    private readonly AppState _state;
    public WebAiDataProvider(AppState state) => _state = state;

    public async Task<string> GetTodayContextAsync(bool includeCessation, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var sb = new StringBuilder();

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var dayStart = today.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        sb.AppendLine($"NOW: {now:yyyy-MM-dd HH:mm} ({now.DayOfWeek})");

        var day = WebSyncClient.Rows<DailyLog>(pull).FirstOrDefault(x => x.Date == today);
        sb.AppendLine($"PLANNED DAY TYPE: {(day is null || day.DayType == DayType.Unset ? "(not set)" : day.DayType.ToString())}");

        sb.AppendLine("TODAY'S NOTES:");
        var notes = WebSyncClient.Rows<NoteEntry>(pull).Where(n => n.Time >= dayStart && n.Time < dayEnd).OrderBy(n => n.Time).ToList();
        if (notes.Count == 0) sb.AppendLine("(none)");
        foreach (var n in notes) sb.AppendLine($"  {n.Time:HH:mm} \"{n.Text}\"");

        var sleep = WebSyncClient.Rows<SleepEntry>(pull).FirstOrDefault(s => s.Date == today);
        sb.AppendLine("SLEEP (last night): " + (sleep is null
            ? "(not logged)"
            : $"{sleep.DurationHours:0.#}h score:{sleep.SleepScore}{(sleep.ScoreEstimated ? "(rough estimate, not Apple's)" : "")} interruptions:{sleep.Interruptions}" +
              (string.IsNullOrWhiteSpace(sleep.Notes) ? "" : $" note:\"{sleep.Notes}\"")));

        var rec = WebSyncClient.Rows<RecoveryEntry>(pull).FirstOrDefault(r => r.Date == today);
        sb.AppendLine("RECOVERY CHECK-IN TODAY: " + (rec is null
            ? "(not logged)"
            : $"recovery:{rec.RecoveryRating?.ToString() ?? "-"}/10 fatigue:{rec.FatigueRating?.ToString() ?? "-"}/10" +
              (rec.SorenessLocationList.Count == 0 ? "" : $" soreness:{rec.SorenessSeverity}({string.Join("/", rec.SorenessLocationList)})") +
              (string.IsNullOrWhiteSpace(rec.Notes) ? "" : $" note:\"{rec.Notes}\"")));

        sb.AppendLine("WORKOUT TODAY:");
        var sessions = WebSyncClient.Rows<WorkoutSession>(pull).Where(s => s.Date == today).ToList();
        var setsBySession = WebSyncClient.Rows<ExerciseSet>(pull)
            .Where(x => sessions.Any(s => s.Id == x.WorkoutSessionId))
            .GroupBy(x => x.WorkoutSessionId).ToDictionary(g => g.Key, g => g.ToList());
        if (sessions.Count == 0) sb.AppendLine("(none yet)");
        foreach (var s in sessions)
        {
            var sets = setsBySession.GetValueOrDefault(s.Id) ?? new();
            sb.AppendLine($"  {(s.TotalSeconds ?? 0) / 60}min, {sets.Count(x => x.Completed)}/{sets.Count} sets" +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));
        }

        sb.AppendLine("MEALS TODAY:");
        var meals = WebSyncClient.Rows<MealEntry>(pull).Where(m => m.Time >= dayStart && m.Time < dayEnd).OrderBy(m => m.Time).ToList();
        if (meals.Count == 0) sb.AppendLine("(none yet)");
        foreach (var m in meals)
            sb.AppendLine($"  {m.Time:HH:mm} {m.MealType} \"{m.Description}\"" +
                          (string.IsNullOrWhiteSpace(m.PortionNote) ? "" : $" portion:\"{m.PortionNote}\"") +
                          (m.TagList.Count == 0 ? "" : $" tags:{string.Join("/", m.TagList)}") +
                          (m.Satiety == Satiety.Unset ? "" : $" satiety:{m.Satiety}"));

        sb.AppendLine("DRINKS TODAY:");
        var drinks = WebSyncClient.Rows<DrinkEntry>(pull).Where(d => d.Time >= dayStart && d.Time < dayEnd).OrderBy(d => d.Time).ToList();
        if (drinks.Count == 0) sb.AppendLine("(none yet)");
        foreach (var d in drinks)
            sb.AppendLine($"  {d.Time:HH:mm} \"{d.Description}\"" +
                          (d.Ounces is { } oz ? $" {oz:0.#}oz" : "") +
                          (d.SugarCount is { } su ? $" sugar:{su}" : ""));

        sb.AppendLine("TODAY'S TOTALS SO FAR (only counts items logged with macros — may be incomplete):");
        sb.AppendLine($"  Calories: {NutritionMath.Calories(meals, drinks)}");
        sb.AppendLine($"  Protein: {NutritionMath.Protein(meals, drinks):0} g");
        sb.AppendLine($"  Carbs: {NutritionMath.Carbs(meals, drinks):0} g");
        sb.AppendLine($"  Fat: {NutritionMath.Fat(meals, drinks):0} g");
        sb.AppendLine($"  Fiber: {NutritionMath.Fiber(meals, drinks):0} g");
        sb.AppendLine($"  Added sugar: {NutritionMath.AddedSugar(meals, drinks):0} g");
        sb.AppendLine($"  Fluids (all drinks): {NutritionMath.FluidOz(drinks)} oz");
        sb.AppendLine("Compare these to the DAILY MACRO/HYDRATION TARGETS above and note where the day is " +
                      "tracking under/in/over range — but remember totals may be incomplete if not everything " +
                      "was logged with macros, so don't scold a low number that's just unlogged food.");

        sb.AppendLine("PHYSICAL WORKLOAD TODAY:");
        var work = WebSyncClient.Rows<PhysicalWorkloadEntry>(pull).Where(w => w.Date == today).ToList();
        if (work.Count == 0) sb.AppendLine("(none)");
        foreach (var w in work)
            sb.AppendLine($"  {w.Activity} {(w.DurationMinutes is { } mins ? $"{mins}min " : "")}{w.Intensity}");

        sb.AppendLine("LAST 7 DAYS (pattern context — cite specific entries when claiming a trend; never extrapolate a pattern the data doesn't show):");
        var weekStart = dayStart.AddDays(-7);
        var weekMeals = WebSyncClient.Rows<MealEntry>(pull).Where(m => m.Time >= weekStart && m.Time < dayStart).ToList();
        var weekDrinks = WebSyncClient.Rows<DrinkEntry>(pull).Where(d => d.Time >= weekStart && d.Time < dayStart).ToList();
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

        // Cessation is withheld unless the user opts in (WebAiSettings currently returns false).
        return sb.ToString();

        static string Trunc(string s, int len) => s.Length <= len ? s : s[..(len - 1)] + "…";
    }

    public async Task<string> GetEightWeekContextAsync(CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);
        var sinceDt = since.ToDateTime(TimeOnly.MinValue);
        var sb = new StringBuilder();

        // Flat sync cache — no nav properties, so resolve exercises/routines by id ourselves.
        var defsById = WebSyncClient.Rows<ExerciseDefinition>(pull).ToDictionary(d => d.Id);
        string DefName(Guid id) => defsById.TryGetValue(id, out var d) ? d.Name : "?";
        var routinesById = WebSyncClient.Rows<WorkoutRoutine>(pull).ToDictionary(r => r.Id);

        sb.AppendLine("WORKOUTS:");
        var sessions = WebSyncClient.Rows<WorkoutSession>(pull).Where(s => s.Date >= since).OrderBy(s => s.Date).ToList();
        var setsBySession = WebSyncClient.Rows<ExerciseSet>(pull).GroupBy(x => x.WorkoutSessionId).ToDictionary(g => g.Key, g => g.ToList());
        var fbBySession = WebSyncClient.Rows<ExerciseFeedback>(pull).GroupBy(f => f.WorkoutSessionId).ToDictionary(g => g.Key, g => g.ToList());
        if (sessions.Count == 0) sb.AppendLine("(none)");
        foreach (var s in sessions)
        {
            var rName = s.RoutineId is { } rid && routinesById.TryGetValue(rid, out var r) ? r.Name : "Workout";
            sb.AppendLine($"{s.Date:yyyy-MM-dd} {rName} {(s.TotalSeconds ?? 0) / 60}min" +
                          (string.IsNullOrWhiteSpace(s.Notes) ? "" : $" note:\"{s.Notes}\""));
            var sets = setsBySession.GetValueOrDefault(s.Id) ?? new();
            var feedback = fbBySession.GetValueOrDefault(s.Id) ?? new();
            foreach (var g in sets.GroupBy(x => DefName(x.ExerciseDefinitionId)))
            {
                var fb = feedback.FirstOrDefault(f => DefName(f.ExerciseDefinitionId) == g.Key);
                var vals = string.Join(",", g.OrderBy(x => x.SetNumber)
                    .Select(x => x.DurationSeconds is { } t ? $"{t}s" : x.Reps?.ToString() ?? "-"));
                var done = g.Count(x => x.Completed);
                sb.AppendLine($"  {g.Key}: {vals} ({done}/{g.Count()} sets done)" +
                              (fb is null || fb.Difficulty == Difficulty.Unset ? "" : $" rated:{fb.Difficulty}") +
                              (string.IsNullOrWhiteSpace(fb?.Comment) ? "" : $" comment:\"{fb!.Comment}\""));
            }
        }

        sb.AppendLine();
        sb.AppendLine("MEALS (date time type description [portion] [tags] [satiety]; 'items:' = per-item macros " +
                      "so you can see which plate items drove any overage):");
        var meals = WebSyncClient.Rows<MealEntry>(pull).Where(m => m.Time >= sinceDt).OrderBy(m => m.Time).ToList();
        if (meals.Count == 0) sb.AppendLine("(none)");
        foreach (var m in meals)
        {
            sb.AppendLine($"{m.Time:yyyy-MM-dd HH:mm} {m.MealType} \"{m.Description}\"" +
                          (string.IsNullOrWhiteSpace(m.PortionNote) ? "" : $" portion:\"{m.PortionNote}\"") +
                          (m.TagList.Count == 0 ? "" : $" tags:{string.Join("/", m.TagList)}") +
                          (m.Satiety == Satiety.Unset ? "" : $" satiety:{m.Satiety}"));
            var items = m.ItemsSummary();
            if (items.Length > 0) sb.AppendLine($"    items: {items}");
        }

        sb.AppendLine();
        sb.AppendLine("DRINKS:");
        var drinks = WebSyncClient.Rows<DrinkEntry>(pull).Where(d => d.Time >= sinceDt).OrderBy(d => d.Time).ToList();
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
        var measurements = WebSyncClient.Rows<BodyMeasurement>(pull).Where(m => m.Date >= since).OrderBy(m => m.Date).ToList();
        if (measurements.Count == 0) sb.AppendLine("(none)");
        foreach (var m in measurements)
            sb.AppendLine($"{m.Date:yyyy-MM-dd}" +
                          (m.WeightLbs is { } w ? $" weight:{w:0.#}lbs" : "") +
                          (m.WaistInches is { } wa ? $" waist:{wa:0.##}in" : "") +
                          (m.ChestInches is { } c ? $" chest:{c:0.##}in" : "") +
                          (m.ArmsInches is { } a ? $" arms:{a:0.##}in" : "") +
                          (m.ThighsInches is { } t ? $" thighs:{t:0.##}in" : "") +
                          (m.ShouldersInches is { } sh ? $" shoulders:{sh:0.##}in" : "") +
                          (m.CalvesInches is { } cv ? $" calves:{cv:0.##}in" : ""));

        var goals = WebSyncClient.Rows<GoalSettings>(pull).OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
        if (goals is not null && (goals.GoalWeightLbs ?? goals.GoalWaistInches ?? goals.GoalBodyFatPercent
                ?? goals.GoalChestInches ?? goals.GoalArmsInches ?? goals.GoalThighsInches) is not null)
        {
            sb.AppendLine();
            sb.AppendLine("BODY-MEASUREMENT GOALS (target values — judge progress against the latest measurement above; direction is whichever way closes the gap):");
            if (goals.GoalWeightLbs is { } gw) sb.AppendLine($"- weight goal: {gw:0.#} lbs");
            if (goals.GoalWaistInches is { } gwa) sb.AppendLine($"- waist goal: {gwa:0.##} in");
            if (goals.GoalBodyFatPercent is { } gbf) sb.AppendLine($"- body-fat goal: {gbf:0.#} %");
            if (goals.GoalChestInches is { } gc) sb.AppendLine($"- chest goal: {gc:0.##} in");
            if (goals.GoalArmsInches is { } ga) sb.AppendLine($"- arms goal: {ga:0.##} in");
            if (goals.GoalThighsInches is { } gt) sb.AppendLine($"- thighs goal: {gt:0.##} in");
        }

        sb.AppendLine();
        sb.AppendLine("SLEEP:");
        var sleep = WebSyncClient.Rows<SleepEntry>(pull).Where(s => s.Date >= since).OrderBy(s => s.Date).ToList();
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
        var pull = await _state.DataAsync();
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);
        var sb = new StringBuilder();

        var allDefsById = WebSyncClient.Rows<ExerciseDefinition>(pull).ToDictionary(d => d.Id);
        string DefName(Guid id) => allDefsById.TryGetValue(id, out var d) ? d.Name : "?";

        sb.AppendLine("EXERCISE LIBRARY (name | measure | muscles | equipment):");
        var defs = WebSyncClient.Rows<ExerciseDefinition>(pull).Where(e => !e.Retired).OrderBy(e => e.Name).ToList();
        if (defs.Count == 0) sb.AppendLine("(empty)");
        foreach (var d in defs)
            sb.AppendLine($"- {d.Name} | {(d.Measure == ExerciseMeasure.Duration ? "duration" : "reps")}" +
                          $" | {(string.IsNullOrWhiteSpace(d.MuscleGroups) ? "muscles unset" : d.MuscleGroups)}" +
                          (string.IsNullOrWhiteSpace(d.EquipmentNotes) ? "" : $" | {d.EquipmentNotes}"));

        sb.AppendLine();
        sb.AppendLine("EXISTING ROUTINES (do not duplicate these):");
        var routines = WebSyncClient.Rows<WorkoutRoutine>(pull).Where(r => !r.Archived).ToList();
        var reByRoutine = WebSyncClient.Rows<RoutineExercise>(pull).GroupBy(e => e.RoutineId).ToDictionary(g => g.Key, g => g.ToList());
        if (routines.Count == 0) sb.AppendLine("(none)");
        foreach (var r in routines)
        {
            var ex = reByRoutine.GetValueOrDefault(r.Id) ?? new();
            sb.AppendLine($"- {r.Name}: {string.Join(", ", ex.OrderBy(e => e.Order).Select(e => DefName(e.ExerciseDefinitionId)))}");
        }

        sb.AppendLine();
        sb.AppendLine("TRAINING HISTORY (last 8 weeks; per exercise: sessions, best set, latest rating):");
        var sessions = WebSyncClient.Rows<WorkoutSession>(pull).Where(s => s.Date >= since).OrderByDescending(s => s.Date).ToList();
        var sessionDate = sessions.ToDictionary(s => s.Id, s => s.Date);
        var sessionIds = sessionDate.Keys.ToHashSet();
        var sets = WebSyncClient.Rows<ExerciseSet>(pull).Where(x => sessionIds.Contains(x.WorkoutSessionId) && allDefsById.ContainsKey(x.ExerciseDefinitionId)).ToList();
        var feedback = WebSyncClient.Rows<ExerciseFeedback>(pull).Where(f => sessionIds.Contains(f.WorkoutSessionId)).ToList();
        var orderedFb = feedback.OrderByDescending(f => sessionDate.GetValueOrDefault(f.WorkoutSessionId)).ToList();
        var byExercise = sets.GroupBy(x => DefName(x.ExerciseDefinitionId)).ToList();
        if (byExercise.Count == 0) sb.AppendLine("(none)");
        foreach (var g in byExercise)
        {
            var sessCount = sessions.Count(s => sets.Any(x => x.WorkoutSessionId == s.Id && DefName(x.ExerciseDefinitionId) == g.Key));
            var bestSecs = g.Max(x => x.DurationSeconds ?? 0);
            var bestReps = g.Max(x => x.Reps ?? 0);
            var lastFb = orderedFb.FirstOrDefault(f => DefName(f.ExerciseDefinitionId) == g.Key && f.Difficulty != Difficulty.Unset);
            sb.AppendLine($"- {g.Key}: {sessCount} session(s), best {(bestSecs > 0 ? $"{bestSecs}s" : $"{bestReps} reps")}" +
                          (lastFb is null ? "" : $", rated {lastFb.Difficulty}") +
                          (lastFb?.PainOrDiscomfort == true ? ", PAIN flagged" : ""));
        }

        var muscleByName = defs.ToDictionary(d => d.Name, d => d.MuscleGroupList, StringComparer.OrdinalIgnoreCase);
        var volume = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sets)
        {
            var name = DefName(set.ExerciseDefinitionId);
            if (!muscleByName.TryGetValue(name, out var groups)) continue;
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
        var pull = await _state.DataAsync();
        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-WindowDays);

        var defsById = WebSyncClient.Rows<ExerciseDefinition>(pull).ToDictionary(d => d.Id);
        var sessionDate = WebSyncClient.Rows<WorkoutSession>(pull).Where(s => s.Date >= since).ToDictionary(s => s.Id, s => s.Date);
        var orderedFb = WebSyncClient.Rows<ExerciseFeedback>(pull)
            .Where(f => sessionDate.ContainsKey(f.WorkoutSessionId) && f.Difficulty != Difficulty.Unset)
            .OrderByDescending(f => sessionDate[f.WorkoutSessionId]);

        // Latest rating per exercise wins; keep those whose most recent rating in the window is Easy.
        var latest = new Dictionary<string, Difficulty>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in orderedFb)
            if (defsById.TryGetValue(f.ExerciseDefinitionId, out var d) && !latest.ContainsKey(d.Name))
                latest[d.Name] = f.Difficulty;
        return latest.Where(kv => kv.Value == Difficulty.Easy).Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private const int WindowDays = 56; // 8 weeks

    private static readonly string[] MuscleCatalog =
        { "Chest", "Back", "Shoulders", "Biceps", "Triceps", "Forearms", "Core", "Glutes", "Quads", "Hamstrings", "Calves" };
}
