using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Data;

/// <summary>
/// One-time import of real history reconstructed from chat logs
/// (health-history.json, event-stream format). Wipes ALL existing data and
/// reseeds from the file.
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
        var events = doc.Events ?? throw new InvalidOperationException("health-history.json has no events");

        Wipe(db);

        // --- Exercise library + the Morning Workout routine as refined in-app ----
        // (The event stream only details some exercises, so the routine definition
        // is pinned here rather than derived from it.)
        var library = new Dictionary<string, ExerciseDefinition>(StringComparer.OrdinalIgnoreCase);
        ExerciseDefinition Def(string name, ExerciseMeasure measure)
        {
            if (library.TryGetValue(name, out var existing)) return existing;
            var def = new ExerciseDefinition { Name = name, Measure = measure };
            library[name] = def;
            db.ExerciseDefinitions.Add(def);
            return def;
        }

        var routine = new WorkoutRoutine { Name = "Morning Workout" };
        db.WorkoutRoutines.Add(routine);
        var prescriptions = new (string Name, ExerciseMeasure Measure, int? Reps, int? Seconds, int? RestSecs)[]
        {
            ("Incline Pushups", ExerciseMeasure.Reps, 20, null, null),
            ("Squats", ExerciseMeasure.Reps, 15, null, null),
            ("Step Ups", ExerciseMeasure.Reps, 15, null, null),
            ("Planks", ExerciseMeasure.Duration, null, 30, 30), // rest only between plank holds
        };
        for (var i = 0; i < prescriptions.Length; i++)
        {
            var p = prescriptions[i];
            db.RoutineExercises.Add(new RoutineExercise
            {
                RoutineId = routine.Id,
                ExerciseDefinitionId = Def(p.Name, p.Measure).Id,
                Order = i,
                TargetSets = 3,
                TargetReps = p.Reps,
                TargetDurationSeconds = p.Seconds,
                RestSeconds = p.RestSecs
            });
        }

        // --- Walk the event stream (file order preserved per day) -----------------
        var sessionsByDate = new Dictionary<DateOnly, WorkoutSession>();
        var dayTypes = new Dictionary<DateOnly, DayType>();
        var lastTimeByDate = new Dictionary<DateOnly, TimeOnly>(); // drinks inherit the prior event's time
        var medicationSeeded = false;

        foreach (var e in events)
        {
            if (e.Type is null || e.Date is null) continue;
            var date = DateOnly.Parse(e.Date);
            var data = e.Data;

            switch (e.Type)
            {
                case "measurement":
                    db.BodyMeasurements.Add(new BodyMeasurement
                    {
                        Date = date,
                        WeightLbs = GetDouble(data, "weight"),
                        WaistInches = GetDouble(data, "waist"),
                        ClothingFitNotes = GetString(data, "notes")
                    });
                    break;

                case "sleep":
                    db.SleepEntries.Add(new SleepEntry
                    {
                        Date = date,
                        DurationHours = GetDouble(data, "durationHours"),
                        SleepScore = GetInt(data, "score"),
                        Interruptions = GetInt(data, "interruptions"),
                        Notes = GetString(data, "notes")
                    });
                    break;

                case "workout":
                {
                    var seconds = ParseDuration(GetString(data, "duration"));
                    var started = date.ToDateTime(new TimeOnly(17, 0));
                    var session = new WorkoutSession
                    {
                        Date = date,
                        RoutineId = routine.Id,
                        StartedAt = started,
                        EndedAt = started.AddSeconds(seconds ?? 0),
                        TotalSeconds = seconds,
                        Notes = JoinNotes(data)
                    };
                    sessionsByDate[date] = session;
                    db.WorkoutSessions.Add(session);
                    dayTypes[date] = DayType.Workout;
                    break;
                }

                case "exercise":
                {
                    var name = GetString(data, "exercise");
                    if (name is null) break;
                    var seconds = GetInt(data, "seconds");
                    var def = Def(name, seconds is not null ? ExerciseMeasure.Duration : ExerciseMeasure.Reps);
                    if (GetInt(data, "inclineHeightInches") is { } incline)
                        def.EquipmentNotes ??= $"incline ~{incline}in";

                    if (!sessionsByDate.TryGetValue(date, out var session))
                    {
                        session = new WorkoutSession { Date = date, RoutineId = routine.Id };
                        sessionsByDate[date] = session;
                        db.WorkoutSessions.Add(session);
                        dayTypes[date] = DayType.Workout;
                    }
                    var reps = GetInt(data, "reps");
                    for (var set = 1; set <= (GetInt(data, "sets") ?? 1); set++)
                        session.Sets.Add(new ExerciseSet
                        {
                            ExerciseDefinitionId = def.Id,
                            SetNumber = set,
                            Reps = def.Measure == ExerciseMeasure.Reps ? reps : null,
                            DurationSeconds = def.Measure == ExerciseMeasure.Duration ? seconds : null,
                            Completed = true
                        });
                    session.Feedback.Add(new ExerciseFeedback
                    {
                        ExerciseDefinitionId = def.Id,
                        Difficulty = Enum.TryParse<Difficulty>(GetString(data, "difficulty"), true, out var diff)
                            ? diff : Difficulty.Unset,
                        Comment = GetString(data, "comment")
                    });
                    break;
                }

                case "meal":
                {
                    var time = ParseTime(e.Time) ?? new TimeOnly(12, 0);
                    lastTimeByDate[date] = time;
                    db.MealEntries.Add(new MealEntry
                    {
                        Time = date.ToDateTime(time),
                        MealType = GetString(data, "mealType")?.ToLowerInvariant() switch
                        {
                            "breakfast" => MealType.Breakfast,
                            "lunch" or "side" => MealType.Lunch,
                            "dinner" => MealType.Dinner,
                            _ => MealType.Snack
                        },
                        Description = GetString(data, "food") ?? "",
                        PortionNote = GetString(data, "portion") ?? GetInt(data, "quantity")?.ToString(),
                        Satiety = Enum.TryParse<Satiety>(GetString(data, "satiety"), true, out var sat)
                            ? sat : Satiety.Unset
                    });
                    break;
                }

                case "drink":
                {
                    var time = ParseTime(e.Time)
                        ?? (lastTimeByDate.TryGetValue(date, out var prev) ? prev : new TimeOnly(12, 0));
                    db.DrinkEntries.Add(new DrinkEntry
                    {
                        Time = date.ToDateTime(time),
                        Description = GetString(data, "name") ?? "",
                        Ounces = GetDouble(data, "ounces")
                    });
                    break;
                }

                case "observation":
                {
                    var category = GetString(data, "category");
                    var note = GetString(data, "note") ?? "";
                    db.NoteEntries.Add(new NoteEntry
                    {
                        Time = date.ToDateTime(ParseTime(e.Time) ?? new TimeOnly(12, 0)),
                        Text = string.IsNullOrWhiteSpace(category) ? note : $"{category}: {note}"
                    });
                    break;
                }

                case "recovery_day":
                    if (!dayTypes.ContainsKey(date)) dayTypes[date] = DayType.Recovery;
                    break;

                case "medication":
                {
                    var name = GetString(data, "name") ?? "TRT";
                    var dose = GetString(data, "dose");
                    db.MedicationEntries.Add(new MedicationEntry
                    {
                        Name = name,
                        Dose = dose,
                        Frequency = GetString(data, "frequency"),
                        TakenAt = date.ToDateTime(new TimeOnly(9, 0))
                    });
                    if (!medicationSeeded)
                    {
                        medicationSeeded = true;
                        db.MedicationSchedules.Add(new MedicationSchedule
                        {
                            Name = name,
                            Dose = dose,
                            IsInjection = true,
                            Repeat = ReminderRepeat.Biweekly,
                            StartDate = date,
                            ReminderTime = new TimeOnly(9, 0),
                            NotificationId = 1001
                        });
                    }
                    break;
                }
            }
        }

        foreach (var (date, dt) in dayTypes)
            db.DailyLogs.Add(new DailyLog { Date = date, DayType = dt });

        // --- Built-in reminder settings -------------------------------------------
        db.ReminderSettings.AddRange(
            new ReminderSetting { Key = "measurement", Repeat = ReminderRepeat.Weekly, Time = new(8, 0), Active = true, NotificationId = 2001 },
            new ReminderSetting { Key = "labCheck", Repeat = ReminderRepeat.Monthly, Time = new(9, 0), Active = false, NotificationId = 2002 },
            new ReminderSetting { Key = "weeklyReview", Repeat = ReminderRepeat.Weekly, Time = new(18, 0), Active = true, NotificationId = 2003 });

        db.SaveChanges();
    }

    // ---- JSON helpers -----------------------------------------------------------
    private static string? GetString(JsonElement data, string prop) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static double? GetDouble(JsonElement data, string prop) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : null;

    private static int? GetInt(JsonElement data, string prop) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : null;

    /// <summary>Workout notes may be a string or an array of strings.</summary>
    private static string? JoinNotes(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("notes", out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Array => string.Join("; ", v.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())),
            _ => null
        };
    }

    /// <summary>"16:18" (mm:ss) → seconds.</summary>
    private static int? ParseDuration(string? mmss)
    {
        if (mmss is null) return null;
        var parts = mmss.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s)
            ? m * 60 + s : null;
    }

    private static TimeOnly? ParseTime(string? hhmm) =>
        TimeOnly.TryParse(hhmm, out var t) ? t : null;

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

    // ---- JSON shapes (event-stream format) ---------------------------------------
    private sealed class HistoryDoc
    {
        public List<EventDto>? Events { get; set; }
    }

    private sealed class EventDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Date { get; set; }
        public string? Time { get; set; }
        public JsonElement Data { get; set; }
    }
}
