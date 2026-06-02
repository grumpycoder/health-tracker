namespace FitRecoveryLog.Data;

/// <summary>
/// Development-only sample data. Call from a DEBUG-guarded path. Populates an
/// empty database with realistic, recent data (dates relative to "today") so
/// every screen has something to show. No-ops if any data already exists.
/// </summary>
public static class DevSeed
{
    public static void SeedIfEmpty(AppDbContext db)
    {
        if (db.BodyMeasurements.Any() || db.MealEntries.Any() || db.WorkoutRoutines.Any()
            || db.SleepEntries.Any() || db.MedicationEntries.Any())
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var midnight = DateTime.Now.Date;
        DateOnly D(int daysAgo) => today.AddDays(-daysAgo);
        DateTime At(int daysAgo, int h, int m) => midnight.AddDays(-daysAgo).AddHours(h).AddMinutes(m);

        db.BodyMeasurements.AddRange(
            new BodyMeasurement { Date = D(28), WeightLbs = 190.2, WaistInches = 35.5 },
            new BodyMeasurement { Date = D(21), WeightLbs = 188.4, WaistInches = 35.1 },
            new BodyMeasurement { Date = D(14), WeightLbs = 187.1, WaistInches = 34.8 },
            new BodyMeasurement { Date = D(7),  WeightLbs = 186.0, WaistInches = 34.5 },
            new BodyMeasurement { Date = D(0),  WeightLbs = 184.7, WaistInches = 34.2 });

        db.SleepEntries.AddRange(
            new SleepEntry { Date = D(4), SleepScore = 69, DurationHours = 6.5 },
            new SleepEntry { Date = D(3), SleepScore = 74, DurationHours = 7.0 },
            new SleepEntry { Date = D(2), SleepScore = 72, DurationHours = 7.2 },
            new SleepEntry { Date = D(1), SleepScore = 81, DurationHours = 7.8 },
            new SleepEntry { Date = D(0), SleepScore = 78, DurationHours = 7.5 });

        db.RecoveryEntries.AddRange(
            new RecoveryEntry { Date = D(1), RecoveryRating = 6, FatigueRating = 5, SorenessLocations = "Lower back,Thighs", SorenessSeverity = SorenessSeverity.Mild },
            new RecoveryEntry { Date = D(0), RecoveryRating = 8, FatigueRating = 3, SorenessSeverity = SorenessSeverity.None });

        db.DrinkEntries.AddRange(
            new DrinkEntry { Time = At(3, 12, 0), Description = "Sweet tea", Ounces = 32 },
            new DrinkEntry { Time = At(2, 12, 0), Description = "Sweet tea", Ounces = 24 },
            new DrinkEntry { Time = At(1, 12, 0), Description = "Sweet tea", Ounces = 40 },
            new DrinkEntry { Time = At(0, 8, 30), Description = "Coffee", Ounces = 12, SugarCount = 2 },
            new DrinkEntry { Time = At(0, 12, 0), Description = "Sweet tea", Ounces = 16 });

        db.MealEntries.AddRange(
            new MealEntry { Time = At(0, 7, 30), MealType = MealType.Breakfast, Description = "Eggs & turkey", PortionNote = "3 eggs, 2 slices", Tags = "High protein,Home-cooked", Satiety = Satiety.Satisfied },
            new MealEntry { Time = At(0, 12, 15), MealType = MealType.Lunch, Description = "Chicken & rice", PortionNote = "1 breast, 1 cup rice", Tags = "High protein", Satiety = Satiety.Full },
            new MealEntry { Time = At(0, 15, 0), MealType = MealType.Snack, Description = "Protein bar", Tags = "High protein", Satiety = Satiety.StillHungry },
            new MealEntry { Time = At(0, 19, 0), MealType = MealType.Dinner, Description = "Restaurant burger & fries", PortionNote = "large", Tags = "Restaurant meal,High sodium", Satiety = Satiety.Bloated });

        db.MedicationEntries.AddRange(
            new MedicationEntry { Name = "Testosterone Cypionate", Dose = "100mg / 0.5mL", Frequency = "weekly", TakenAt = At(11, 9, 0), InjectionSite = "Left ventroglute" },
            new MedicationEntry { Name = "Testosterone Cypionate", Dose = "100mg / 0.5mL", Frequency = "weekly", TakenAt = At(4, 9, 0), InjectionSite = "Right ventroglute", ReactionNotes = "Mild soreness day after" },
            new MedicationEntry { Name = "Vitamin D", Dose = "5000 IU", Frequency = "daily", TakenAt = At(0, 8, 0) });

        db.LabResults.AddRange(
            new LabResult { Date = D(112), LabName = "Total Testosterone", Value = 540, Unit = "ng/dL", Notes = "baseline" },
            new LabResult { Date = D(18), LabName = "Total Testosterone", Value = 720, Unit = "ng/dL" },
            new LabResult { Date = D(18), LabName = "Hematocrit", Value = 49, Unit = "%" },
            new LabResult { Date = D(18), LabName = "PSA", Value = 1.2, Unit = "ng/mL" },
            new LabResult { Date = D(18), LabName = "A1C", Value = 5.4, Unit = "%" });

        // A routine so the Workout screen has something to start.
        var pushups = new ExerciseDefinition { Name = "Incline push-ups", Measure = ExerciseMeasure.Reps, TargetSets = 3, TargetReps = 20, RestSeconds = 60, EquipmentNotes = "incline ~18in" };
        var lunges = new ExerciseDefinition { Name = "Lunges", Measure = ExerciseMeasure.Reps, TargetSets = 3, TargetReps = 12, RestSeconds = 60 };
        var plank = new ExerciseDefinition { Name = "Plank", Measure = ExerciseMeasure.Duration, TargetSets = 3, TargetDurationSeconds = 45, RestSeconds = 45 };
        var glutes = new ExerciseDefinition { Name = "Glute bridges", Measure = ExerciseMeasure.Reps, TargetSets = 3, TargetReps = 15, RestSeconds = 45 };
        db.ExerciseDefinitions.AddRange(pushups, lunges, plank, glutes);

        var routine = new WorkoutRoutine { Name = "Morning bodyweight" };
        db.WorkoutRoutines.Add(routine);
        db.RoutineExercises.AddRange(
            new RoutineExercise { RoutineId = routine.Id, ExerciseDefinitionId = pushups.Id, Order = 0 },
            new RoutineExercise { RoutineId = routine.Id, ExerciseDefinitionId = lunges.Id, Order = 1 },
            new RoutineExercise { RoutineId = routine.Id, ExerciseDefinitionId = plank.Id, Order = 2 },
            new RoutineExercise { RoutineId = routine.Id, ExerciseDefinitionId = glutes.Id, Order = 3 });

        // Completed sessions (drive the workout-duration trend).
        var defs = new[] { pushups, lunges, plank, glutes };
        WorkoutSession Session(int daysAgo, int durMin)
        {
            var s = new WorkoutSession
            {
                Date = D(daysAgo), RoutineId = routine.Id,
                StartedAt = At(daysAgo, 17, 0), EndedAt = At(daysAgo, 17, durMin), TotalSeconds = durMin * 60
            };
            // Record each exercise's sets so the history detail has content.
            foreach (var def in defs)
                for (var i = 1; i <= (def.TargetSets ?? 1); i++)
                    s.Sets.Add(new ExerciseSet
                    {
                        ExerciseDefinitionId = def.Id,
                        SetNumber = i,
                        Reps = def.Measure == ExerciseMeasure.Reps ? def.TargetReps : null,
                        DurationSeconds = def.Measure == ExerciseMeasure.Duration ? def.TargetDurationSeconds : null,
                        Completed = true
                    });
            return s;
        }

        var recent = Session(1, 34);
        recent.Feedback.Add(new ExerciseFeedback { ExerciseDefinitionId = pushups.Id, Difficulty = Difficulty.Hard, Comment = "Last set on push-ups was very hard" });
        recent.Feedback.Add(new ExerciseFeedback { ExerciseDefinitionId = plank.Id, Difficulty = Difficulty.VeryHard, BreathingDifficulty = true });

        db.WorkoutSessions.AddRange(
            Session(13, 25), Session(9, 30), Session(5, 27), recent);

        db.SaveChanges();
    }
}
