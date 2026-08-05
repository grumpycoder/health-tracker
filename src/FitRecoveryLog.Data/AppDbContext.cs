using Microsoft.EntityFrameworkCore;

namespace FitRecoveryLog.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>For derived contexts (e.g. the cloud/server SQL Server context) that pass
    /// their own strongly-typed options. Keeps one model definition across providers.</summary>
    protected AppDbContext(DbContextOptions options) : base(options) { }

    public DbSet<DailyLog> DailyLogs => Set<DailyLog>();
    public DbSet<NoteEntry> NoteEntries => Set<NoteEntry>();
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
    public DbSet<MedicationSchedule> MedicationSchedules => Set<MedicationSchedule>();
    public DbSet<MedicationEntry> MedicationEntries => Set<MedicationEntry>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<WeeklyReview> WeeklyReviews => Set<WeeklyReview>();
    public DbSet<ReminderSetting> ReminderSettings => Set<ReminderSetting>();
    public DbSet<CessationGoal> CessationGoals => Set<CessationGoal>();
    public DbSet<CessationEvent> CessationEvents => Set<CessationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The unique-index filter and the case-insensitive collation are written in
        // provider-specific SQL, so pick the dialect for whichever provider is active
        // (SQLite on the phone, SQL Server in the cloud). Same model, both targets.
        var isSqlite = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
        var liveRowFilter = isSqlite ? "\"IsDeleted\" = 0" : "[IsDeleted] = 0";

        // Unique indexes are filtered to live rows so a tombstoned row doesn't block
        // re-adding the same date/name after a soft-delete.
        modelBuilder.Entity<DailyLog>().HasIndex(x => x.Date).IsUnique().HasFilter(liveRowFilter);
        modelBuilder.Entity<CessationEvent>().HasIndex(x => new { x.GoalId, x.Time });
        modelBuilder.Entity<BodyMeasurement>().HasIndex(x => x.Date);

        // Library exercises are unique by name, matched case-insensitively. SQLite needs an
        // explicit NOCASE collation; SQL Server's default collation is already case-insensitive.
        if (isSqlite)
            modelBuilder.Entity<ExerciseDefinition>().Property(e => e.Name).UseCollation("NOCASE");
        modelBuilder.Entity<ExerciseDefinition>().HasIndex(e => e.Name).IsUnique().HasFilter(liveRowFilter);
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

        // Hide soft-deleted (tombstoned) rows from every normal query. FindAsync and
        // IgnoreQueryFilters() still see them (used by the delete path and full wipe).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(EntityBase).IsAssignableFrom(entityType.ClrType)) continue;
            var e = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var body = System.Linq.Expressions.Expression.Not(
                System.Linq.Expressions.Expression.Property(e, nameof(EntityBase.IsDeleted)));
            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(System.Linq.Expressions.Expression.Lambda(body, e));
        }

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

    /// <summary>When true, SaveChanges does not stamp <c>UpdatedAt</c> or convert deletes to
    /// tombstones. The sync client sets this while applying rows pulled from the cloud so it
    /// writes the server's <c>UpdatedAt</c> verbatim — otherwise applied rows would look
    /// locally-modified and echo straight back on the next push.</summary>
    public bool SuppressTimestamps { get; set; }

    protected virtual void StampTimestamps()
    {
        if (SuppressTimestamps) return;

        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Deleted)
            {
                // Convert every delete into a soft-delete tombstone so it can sync to
                // other clients. Physical removal only happens via ExecuteDelete (Wipe).
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = DateTime.UtcNow;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
            // Added rows keep their constructor UTC stamps (or, on restore, the values
            // from the backup) — don't overwrite them here.
        }
    }
}
