using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Data;

/// <summary>
/// One-time import of real history reconstructed from chat logs
/// (health-history.json). Wipes ALL existing data and reseeds from the file.
/// </summary>
public static class HistorySeed
{
    /// <summary>Reads health-history.json embedded in this assembly and applies it.</summary>
    public static void ApplyEmbedded(AppDbContext db)
    {
        using var stream = typeof(HistorySeed).Assembly.GetManifestResourceStream("health-history.json")
            ?? throw new InvalidOperationException("Embedded health-history.json not found");
        using var reader = new StreamReader(stream);
        Apply(db, reader.ReadToEnd());
    }

    public static void Apply(AppDbContext db, string json)
    {
        var doc = JsonSerializer.Deserialize<HistoryDoc>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("health-history.json is empty or invalid");

        Wipe(db);

        // --- Body measurements -------------------------------------------------
        foreach (var m in doc.Measurements ?? [])
            db.BodyMeasurements.Add(new BodyMeasurement
            {
                Date = DateOnly.Parse(m.Date!),
                WeightLbs = m.Weight,
                WaistInches = m.Waist
            });

        // --- Medications: dose log + a biweekly TRT schedule --------------------
        var meds = doc.Medications ?? [];
        foreach (var m in meds)
            db.MedicationEntries.Add(new MedicationEntry
            {
                Name = m.Name ?? "",
                Dose = m.Dose,
                Frequency = "Biweekly",
                TakenAt = DateOnly.Parse(m.Date!).ToDateTime(new TimeOnly(9, 0)),
                ReactionNotes = m.Notes
            });
        if (meds.Count > 0)
            db.MedicationSchedules.Add(new MedicationSchedule
            {
                Name = meds[0].Name ?? "TRT",
                Dose = meds[0].Dose,
                IsInjection = string.Equals(meds[0].Route, "Injection", StringComparison.OrdinalIgnoreCase),
                Repeat = ReminderRepeat.Biweekly,
                StartDate = DateOnly.Parse(meds[0].Date!),
                ReminderTime = new TimeOnly(9, 0),
                NotificationId = 1001
            });

        // --- Workouts: library exercises, one routine, sessions -----------------
        var workouts = doc.Workouts ?? [];
        var library = new Dictionary<string, ExerciseDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var ex in workouts.SelectMany(w => w.Exercises ?? []))
        {
            if (ex.Name is null || library.ContainsKey(ex.Name)) continue;
            var def = new ExerciseDefinition
            {
                Name = ex.Name,
                Measure = ex.Seconds is not null ? ExerciseMeasure.Duration : ExerciseMeasure.Reps
            };
            library[ex.Name] = def;
            db.ExerciseDefinitions.Add(def);
        }

        // Prescribe the routine from the most recent workout's numbers.
        if (workouts.Count > 0)
        {
            var routine = new WorkoutRoutine { Name = "Morning Workout" };
            db.WorkoutRoutines.Add(routine);
            var latest = workouts[^1].Exercises ?? [];
            for (var i = 0; i < latest.Count; i++)
            {
                var ex = latest[i];
                if (ex.Name is null) continue;
                db.RoutineExercises.Add(new RoutineExercise
                {
                    RoutineId = routine.Id,
                    ExerciseDefinitionId = library[ex.Name].Id,
                    Order = i,
                    TargetSets = ex.Sets,
                    TargetReps = ex.Reps,
                    TargetDurationSeconds = ex.Seconds,
                    // No rest between exercises except 30s between plank holds.
                    RestSeconds = string.Equals(ex.Name, "Planks", StringComparison.OrdinalIgnoreCase) ? 30 : null
                });
            }

            foreach (var w in workouts)
            {
                var date = DateOnly.Parse(w.Date!);
                var started = date.ToDateTime(new TimeOnly(17, 0));
                var session = new WorkoutSession
                {
                    Date = date,
                    RoutineId = routine.Id,
                    StartedAt = started,
                    EndedAt = started.AddSeconds(w.DurationSeconds ?? 0),
                    TotalSeconds = w.DurationSeconds,
                    Notes = w.Notes
                };
                foreach (var ex in w.Exercises ?? [])
                {
                    if (ex.Name is null) continue;
                    var def = library[ex.Name];
                    for (var set = 1; set <= (ex.Sets ?? 1); set++)
                        session.Sets.Add(new ExerciseSet
                        {
                            ExerciseDefinitionId = def.Id,
                            SetNumber = set,
                            Reps = def.Measure == ExerciseMeasure.Reps ? ex.Reps : null,
                            DurationSeconds = def.Measure == ExerciseMeasure.Duration ? ex.Seconds : null,
                            Completed = true
                        });
                    session.Feedback.Add(new ExerciseFeedback
                    {
                        ExerciseDefinitionId = def.Id,
                        Difficulty = Enum.TryParse<Difficulty>(ex.Difficulty, true, out var d) ? d : Difficulty.Unset,
                        Comment = ex.Comment
                    });
                }
                db.WorkoutSessions.Add(session);
            }
        }

        // --- Physical workload (recovery-day activities) -------------------------
        foreach (var r in doc.RecoveryDays ?? [])
            db.PhysicalWorkloadEntries.Add(new PhysicalWorkloadEntry
            {
                Date = DateOnly.Parse(r.Date!),
                Activity = r.Activity ?? "",
                DurationMinutes = r.DurationHours is { } h ? (int)(h * 60) : null,
                Intensity = Enum.TryParse<Intensity>(r.Intensity, true, out var i) ? i : Intensity.Moderate,
                Notes = r.Notes
            });

        // --- Sleep ---------------------------------------------------------------
        foreach (var s in doc.Sleep ?? [])
            db.SleepEntries.Add(new SleepEntry
            {
                Date = DateOnly.Parse(s.Date!),
                DurationHours = s.DurationMinutes is { } min ? Math.Round(min / 60.0, 1) : null,
                SleepScore = s.Score,
                Interruptions = s.Interruptions,
                Notes = s.Notes
            });

        // --- Nutrition: drinks split out, "Side" rides with lunch ----------------
        foreach (var n in doc.Nutrition ?? [])
        {
            var time = DateOnly.Parse(n.Date!).ToDateTime(TimeOnly.Parse(n.Time ?? "12:00"));
            if (string.Equals(n.Type, "Drink", StringComparison.OrdinalIgnoreCase))
            {
                db.DrinkEntries.Add(new DrinkEntry { Time = time, Description = n.Food ?? "", Ounces = n.Ounces });
                continue;
            }
            db.MealEntries.Add(new MealEntry
            {
                Time = time,
                MealType = n.Type?.ToLowerInvariant() switch
                {
                    "breakfast" => MealType.Breakfast,
                    "lunch" or "side" => MealType.Lunch,
                    "dinner" => MealType.Dinner,
                    _ => MealType.Snack
                },
                Description = n.Food ?? "",
                PortionNote = n.Portion ?? n.Quantity?.ToString(),
                Satiety = Enum.TryParse<Satiety>(n.Satiety, true, out var sat) ? sat : Satiety.Unset
            });
        }

        // --- Observations → timestamped notes; day types from activity ------------
        foreach (var o in doc.Observations ?? [])
            db.NoteEntries.Add(new NoteEntry
            {
                Time = DateOnly.Parse(o.Date!).ToDateTime(new TimeOnly(12, 0)),
                Text = string.IsNullOrWhiteSpace(o.Category) ? o.Note ?? "" : $"{o.Category}: {o.Note}"
            });

        var days = new Dictionary<DateOnly, DailyLog>();
        DailyLog Day(DateOnly d) => days.TryGetValue(d, out var log)
            ? log
            : days[d] = new DailyLog { Date = d };

        foreach (var w in workouts) Day(DateOnly.Parse(w.Date!)).DayType = DayType.Workout;
        foreach (var r in doc.RecoveryDays ?? []) Day(DateOnly.Parse(r.Date!)).DayType = DayType.HighWorkload;
        db.DailyLogs.AddRange(days.Values);

        // --- Built-in reminder settings -------------------------------------------
        db.ReminderSettings.AddRange(
            new ReminderSetting { Key = "measurement", Repeat = ReminderRepeat.Weekly, Time = new(8, 0), Active = true, NotificationId = 2001 },
            new ReminderSetting { Key = "labCheck", Repeat = ReminderRepeat.Monthly, Time = new(9, 0), Active = false, NotificationId = 2002 },
            new ReminderSetting { Key = "weeklyReview", Repeat = ReminderRepeat.Weekly, Time = new(18, 0), Active = true, NotificationId = 2003 });

        db.SaveChanges();
    }

    /// <summary>Delete every row in every table (children before parents).
    /// Shared with BackupRestore.</summary>
    internal static void Wipe(AppDbContext db)
    {
        db.ExerciseSets.ExecuteDelete();
        db.ExerciseFeedback.ExecuteDelete();
        db.WorkoutSessions.ExecuteDelete();
        db.RoutineExercises.ExecuteDelete();
        db.WorkoutRoutines.ExecuteDelete();
        db.ExerciseDefinitions.ExecuteDelete();
        db.MealEntries.ExecuteDelete();
        db.DrinkEntries.ExecuteDelete();
        db.BodyMeasurements.ExecuteDelete();
        db.SleepEntries.ExecuteDelete();
        db.RecoveryEntries.ExecuteDelete();
        db.PhysicalWorkloadEntries.ExecuteDelete();
        db.MedicationEntries.ExecuteDelete();
        db.MedicationSchedules.ExecuteDelete();
        db.LabResults.ExecuteDelete();
        db.WeeklyReviews.ExecuteDelete();
        db.ReminderSettings.ExecuteDelete();
        db.DailyLogs.ExecuteDelete();
        db.NoteEntries.ExecuteDelete();
    }

    // ---- JSON shapes (match health-history.json) -------------------------------
    private sealed class HistoryDoc
    {
        public List<MeasurementDto>? Measurements { get; set; }
        public List<MedicationDto>? Medications { get; set; }
        public List<WorkoutDto>? Workouts { get; set; }
        public List<RecoveryDayDto>? RecoveryDays { get; set; }
        public List<SleepDto>? Sleep { get; set; }
        public List<NutritionDto>? Nutrition { get; set; }
        public List<ObservationDto>? Observations { get; set; }
    }

    private sealed class MeasurementDto { public string? Date { get; set; } public double? Weight { get; set; } public double? Waist { get; set; } }
    private sealed class MedicationDto { public string? Date { get; set; } public string? Name { get; set; } public string? Dose { get; set; } public string? Route { get; set; } public string? Notes { get; set; } }
    private sealed class WorkoutDto { public string? Date { get; set; } public int? DurationSeconds { get; set; } public string? Notes { get; set; } public List<WorkoutExerciseDto>? Exercises { get; set; } }
    private sealed class WorkoutExerciseDto { public string? Name { get; set; } public int? Sets { get; set; } public int? Reps { get; set; } public int? Seconds { get; set; } public string? Difficulty { get; set; } public string? Comment { get; set; } }
    private sealed class RecoveryDayDto { public string? Date { get; set; } public string? Activity { get; set; } public double? DurationHours { get; set; } public string? Intensity { get; set; } public string? Notes { get; set; } }
    private sealed class SleepDto { public string? Date { get; set; } public int? DurationMinutes { get; set; } public int? Score { get; set; } public int? Interruptions { get; set; } public string? Notes { get; set; } }
    private sealed class NutritionDto { public string? Date { get; set; } public string? Time { get; set; } public string? Type { get; set; } public string? Food { get; set; } public int? Quantity { get; set; } public double? Ounces { get; set; } public string? Portion { get; set; } public string? Satiety { get; set; } }
    private sealed class ObservationDto { public string? Date { get; set; } public string? Category { get; set; } public string? Note { get; set; } }
}
