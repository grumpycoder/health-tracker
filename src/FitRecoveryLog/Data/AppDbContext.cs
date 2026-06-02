using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DailyLog> DailyLogs => Set<DailyLog>();
    public DbSet<WorkoutRoutine> WorkoutRoutines => Set<WorkoutRoutine>();
    public DbSet<RoutineExercise> RoutineExercises => Set<RoutineExercise>();
    public DbSet<ExerciseDefinition> ExerciseDefinitions => Set<ExerciseDefinition>();
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<ExerciseSet> ExerciseSets => Set<ExerciseSet>();
    public DbSet<ExerciseFeedback> ExerciseFeedback => Set<ExerciseFeedback>();
    public DbSet<MealEntry> MealEntries => Set<MealEntry>();
    public DbSet<DrinkEntry> DrinkEntries => Set<DrinkEntry>();
    public DbSet<BodyMeasurement> BodyMeasurements => Set<BodyMeasurement>();
    public DbSet<SleepEntry> SleepEntries => Set<SleepEntry>();
    public DbSet<RecoveryEntry> RecoveryEntries => Set<RecoveryEntry>();
    public DbSet<PhysicalWorkloadEntry> PhysicalWorkloadEntries => Set<PhysicalWorkloadEntry>();
    public DbSet<MedicationEntry> MedicationEntries => Set<MedicationEntry>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<WeeklyReview> WeeklyReviews => Set<WeeklyReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DailyLog>().HasIndex(x => x.Date).IsUnique();
        modelBuilder.Entity<BodyMeasurement>().HasIndex(x => x.Date);
        modelBuilder.Entity<WorkoutSession>().HasIndex(x => x.Date);

        modelBuilder.Entity<WorkoutRoutine>()
            .HasMany(r => r.Exercises)
            .WithOne(e => e.Routine!)
            .HasForeignKey(e => e.RoutineId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkoutSession>()
            .HasMany(s => s.Sets)
            .WithOne(s => s.WorkoutSession!)
            .HasForeignKey(s => s.WorkoutSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkoutSession>()
            .HasMany(s => s.Feedback)
            .WithOne(f => f.WorkoutSession!)
            .HasForeignKey(f => f.WorkoutSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void StampTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTimeOffset.Now;
        }
    }
}
