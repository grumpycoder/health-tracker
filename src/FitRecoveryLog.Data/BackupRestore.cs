using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Data;

/// <summary>
/// Restores a full-backup JSON file produced by the Export page: wipes every
/// table and re-inserts the backup's records (ids preserved).
/// </summary>
public static class BackupRestore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Serialize options must ignore reference cycles — EF fixup links
    // WorkoutSession <-> ExerciseSet/Feedback navigations, which would otherwise
    // throw on serialization.
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() }
    };

    public sealed class Backup
    {
        public List<DailyLog>? DailyLogs { get; set; }
        public List<NoteEntry>? NoteEntries { get; set; }
        public List<BodyMeasurement>? BodyMeasurements { get; set; }
        public List<MealEntry>? Meals { get; set; }
        public List<DrinkEntry>? Drinks { get; set; }
        public List<SleepEntry>? Sleep { get; set; }
        public List<RecoveryEntry>? Recovery { get; set; }
        public List<PhysicalWorkloadEntry>? PhysicalWorkload { get; set; }
        public List<MedicationEntry>? Medications { get; set; }
        public List<MedicationSchedule>? MedicationSchedules { get; set; }
        public List<LabResult>? Labs { get; set; }
        public List<ExerciseDefinition>? Exercises { get; set; }
        public List<WorkoutRoutine>? Routines { get; set; }
        public List<RoutineExercise>? RoutineExercises { get; set; }
        public List<WorkoutSession>? WorkoutSessions { get; set; }
        public List<ExerciseSet>? ExerciseSets { get; set; }
        public List<ExerciseFeedback>? ExerciseFeedback { get; set; }
        public List<WeeklyReview>? WeeklyReviews { get; set; }
        public List<ReminderSetting>? ReminderSettings { get; set; }
    }

    public static Backup Parse(string json) =>
        JsonSerializer.Deserialize<Backup>(json, Opts)
        ?? throw new InvalidOperationException("Not a valid backup file");

    /// <summary>Serialize the entire database to a restore-compatible backup JSON.
    /// Shared by the Export page and the automatic snapshot.</summary>
    public static async Task<string> BuildJsonAsync(AppDbContext db)
    {
        var b = new Backup
        {
            DailyLogs = await db.DailyLogs.ToListAsync(),
            NoteEntries = await db.NoteEntries.ToListAsync(),
            BodyMeasurements = await db.BodyMeasurements.ToListAsync(),
            Meals = await db.MealEntries.ToListAsync(),
            Drinks = await db.DrinkEntries.ToListAsync(),
            Sleep = await db.SleepEntries.ToListAsync(),
            Recovery = await db.RecoveryEntries.ToListAsync(),
            PhysicalWorkload = await db.PhysicalWorkloadEntries.ToListAsync(),
            Medications = await db.MedicationEntries.ToListAsync(),
            MedicationSchedules = await db.MedicationSchedules.ToListAsync(),
            Labs = await db.LabResults.ToListAsync(),
            Exercises = await db.ExerciseDefinitions.ToListAsync(),
            Routines = await db.WorkoutRoutines.ToListAsync(),
            RoutineExercises = await db.RoutineExercises.ToListAsync(),
            WorkoutSessions = await db.WorkoutSessions.ToListAsync(),
            ExerciseSets = await db.ExerciseSets.ToListAsync(),
            ExerciseFeedback = await db.ExerciseFeedback.ToListAsync(),
            WeeklyReviews = await db.WeeklyReviews.ToListAsync(),
            ReminderSettings = await db.ReminderSettings.ToListAsync(),
        };
        return JsonSerializer.Serialize(b, WriteOpts);
    }

    /// <summary>One-line summary of what the backup contains, for the confirm step.</summary>
    public static string Summarize(Backup b)
    {
        var parts = new List<string>();
        void Add(string label, int? n) { if (n is > 0) parts.Add($"{n} {label}"); }
        Add("measurements", b.BodyMeasurements?.Count);
        Add("meals", b.Meals?.Count);
        Add("drinks", b.Drinks?.Count);
        Add("workouts", b.WorkoutSessions?.Count);
        Add("routines", b.Routines?.Count);
        Add("sleep entries", b.Sleep?.Count);
        Add("recovery entries", b.Recovery?.Count);
        Add("workload entries", b.PhysicalWorkload?.Count);
        Add("med doses", b.Medications?.Count);
        Add("med schedules", b.MedicationSchedules?.Count);
        Add("labs", b.Labs?.Count);
        Add("daily logs", b.DailyLogs?.Count);
        Add("notes", b.NoteEntries?.Count);
        Add("weekly reviews", b.WeeklyReviews?.Count);
        return parts.Count == 0 ? "no records" : string.Join(", ", parts);
    }

    public static void Apply(AppDbContext db, Backup b)
    {
        // The exporter serializes navigations too (EF fix-up populates them when
        // everything is loaded in one context). Each table also appears as its own
        // top-level list, so strip navigations to avoid double inserts.
        foreach (var r in b.Routines ?? []) r.Exercises = new();
        foreach (var re in b.RoutineExercises ?? []) { re.Routine = null; re.ExerciseDefinition = null; }
        foreach (var ws in b.WorkoutSessions ?? []) { ws.Routine = null; ws.Sets = new(); ws.Feedback = new(); }
        foreach (var s in b.ExerciseSets ?? []) { s.WorkoutSession = null; s.ExerciseDefinition = null; }
        foreach (var f in b.ExerciseFeedback ?? []) { f.WorkoutSession = null; f.ExerciseDefinition = null; }

        HistorySeed.Wipe(db);

        db.DailyLogs.AddRange(b.DailyLogs ?? []);
        db.NoteEntries.AddRange(b.NoteEntries ?? []);
        db.BodyMeasurements.AddRange(b.BodyMeasurements ?? []);
        db.MealEntries.AddRange(b.Meals ?? []);
        db.DrinkEntries.AddRange(b.Drinks ?? []);
        db.SleepEntries.AddRange(b.Sleep ?? []);
        db.RecoveryEntries.AddRange(b.Recovery ?? []);
        db.PhysicalWorkloadEntries.AddRange(b.PhysicalWorkload ?? []);
        db.MedicationEntries.AddRange(b.Medications ?? []);
        db.MedicationSchedules.AddRange(b.MedicationSchedules ?? []);
        db.LabResults.AddRange(b.Labs ?? []);
        db.ExerciseDefinitions.AddRange(b.Exercises ?? []);
        db.WorkoutRoutines.AddRange(b.Routines ?? []);
        db.RoutineExercises.AddRange(b.RoutineExercises ?? []);
        db.WorkoutSessions.AddRange(b.WorkoutSessions ?? []);
        db.ExerciseSets.AddRange(b.ExerciseSets ?? []);
        db.ExerciseFeedback.AddRange(b.ExerciseFeedback ?? []);
        db.WeeklyReviews.AddRange(b.WeeklyReviews ?? []);

        // Older backups predate reminderSettings — fall back to the defaults.
        if (b.ReminderSettings is { Count: > 0 } rs)
            db.ReminderSettings.AddRange(rs);
        else
            db.ReminderSettings.AddRange(
                new ReminderSetting { Key = "measurement", Repeat = ReminderRepeat.Weekly, Time = new(8, 0), Active = true, NotificationId = 2001 },
                new ReminderSetting { Key = "labCheck", Repeat = ReminderRepeat.Monthly, Time = new(9, 0), Active = false, NotificationId = 2002 },
                new ReminderSetting { Key = "weeklyReview", Repeat = ReminderRepeat.Weekly, Time = new(18, 0), Active = true, NotificationId = 2003 });

        db.SaveChanges();
    }
}
