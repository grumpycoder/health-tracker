using FitRecoveryLog.Application.Workouts;
using FitRecoveryLog.Domain.Workouts;
using Persistence = FitRecoveryLog.Data;

namespace FitRecoveryLog.Web.Infrastructure;

/// <summary>
/// Web implementation of <see cref="IWorkoutRepository"/> over the sync API. Maps the
/// <see cref="WorkoutSession"/> aggregate (session + sets + feedback) to/from the persistence
/// entities, mapping the domain <see cref="Difficulty"/> to the storage enum by ordinal.
/// </summary>
public sealed class ApiWorkoutRepository : IWorkoutRepository
{
    private readonly AppState _state;
    private readonly WebSyncClient _sync;

    public ApiWorkoutRepository(AppState state, WebSyncClient sync)
    {
        _state = state;
        _sync = sync;
    }

    public async Task<WorkoutSession?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var row = WebSyncClient.Rows<Persistence.WorkoutSession>(pull).FirstOrDefault(s => s.Id == id);
        return row is null ? null : ToDomain(pull, row);
    }

    public async Task<IReadOnlyList<WorkoutSession>> ListAsync(CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        return WebSyncClient.Rows<Persistence.WorkoutSession>(pull).Select(r => ToDomain(pull, r)).ToList();
    }

    public async Task SaveAsync(WorkoutSession session, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var existing = WebSyncClient.Rows<Persistence.WorkoutSession>(pull).FirstOrDefault(s => s.Id == session.Id);

        await _sync.PushAsync(new Persistence.WorkoutSession
        {
            Id = session.Id,
            Date = session.Date,
            RoutineId = session.RoutineId,
            StartedAt = session.StartedAt,
            EndedAt = session.EndedAt,
            TotalSeconds = session.TotalSeconds,
            Notes = session.Notes,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        });

        await ReconcileSets(pull, session);
        await ReconcileFeedback(pull, session);
        _state.Invalidate();
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var pull = await _state.DataAsync();
        var row = WebSyncClient.Rows<Persistence.WorkoutSession>(pull).FirstOrDefault(s => s.Id == id);
        if (row is null) return;

        Tombstone(row);
        await _sync.PushAsync(row);

        var sets = WebSyncClient.Rows<Persistence.ExerciseSet>(pull).Where(x => x.WorkoutSessionId == id).ToList();
        sets.ForEach(Tombstone);
        if (sets.Count > 0) await _sync.PushAsync(sets);

        var fb = WebSyncClient.Rows<Persistence.ExerciseFeedback>(pull).Where(x => x.WorkoutSessionId == id).ToList();
        fb.ForEach(Tombstone);
        if (fb.Count > 0) await _sync.PushAsync(fb);

        _state.Invalidate();
    }

    private async Task ReconcileSets(Data.Sync.SyncPullResponse pull, WorkoutSession session)
    {
        var stored = WebSyncClient.Rows<Persistence.ExerciseSet>(pull).Where(s => s.WorkoutSessionId == session.Id).ToList();
        var keep = session.Sets.Select(s => s.Id).ToHashSet();
        var toPush = session.Sets.Select(s => new Persistence.ExerciseSet
        {
            Id = s.Id,
            WorkoutSessionId = session.Id,
            ExerciseDefinitionId = s.ExerciseDefinitionId,
            SetNumber = s.SetNumber,
            Reps = s.Result.Reps,
            Weight = s.Result.Weight,
            DurationSeconds = s.Result.DurationSeconds,
            RestSeconds = s.Result.RestSeconds,
            Completed = s.Completed,
        }).ToList();
        foreach (var gone in stored.Where(s => !keep.Contains(s.Id))) { Tombstone(gone); toPush.Add(gone); }
        if (toPush.Count > 0) await _sync.PushAsync(toPush);
    }

    private async Task ReconcileFeedback(Data.Sync.SyncPullResponse pull, WorkoutSession session)
    {
        var stored = WebSyncClient.Rows<Persistence.ExerciseFeedback>(pull).Where(f => f.WorkoutSessionId == session.Id).ToList();
        var keep = session.Feedback.Select(f => f.Id).ToHashSet();
        var toPush = session.Feedback.Select(f => new Persistence.ExerciseFeedback
        {
            Id = f.Id,
            WorkoutSessionId = session.Id,
            ExerciseDefinitionId = f.ExerciseDefinitionId,
            Difficulty = (Persistence.Difficulty)(int)f.Difficulty,
            PainOrDiscomfort = f.PainOrDiscomfort,
            BreathingDifficulty = f.BreathingDifficulty,
            FormIssues = f.FormIssues,
            Comment = f.Comment,
        }).ToList();
        foreach (var gone in stored.Where(f => !keep.Contains(f.Id))) { Tombstone(gone); toPush.Add(gone); }
        if (toPush.Count > 0) await _sync.PushAsync(toPush);
    }

    private static void Tombstone(Persistence.EntityBase e) { e.IsDeleted = true; e.DeletedAt = DateTime.UtcNow; }

    private static WorkoutSession ToDomain(Data.Sync.SyncPullResponse pull, Persistence.WorkoutSession row)
    {
        var sets = WebSyncClient.Rows<Persistence.ExerciseSet>(pull).Where(s => s.WorkoutSessionId == row.Id)
            .Select(s => WorkoutSet.Rehydrate(s.Id, s.ExerciseDefinitionId, s.SetNumber,
                new SetResult(s.Reps, s.Weight, s.DurationSeconds, s.RestSeconds), s.Completed));
        var feedback = WebSyncClient.Rows<Persistence.ExerciseFeedback>(pull).Where(f => f.WorkoutSessionId == row.Id)
            .Select(f => WorkoutFeedback.Rehydrate(f.Id, f.ExerciseDefinitionId, (Difficulty)(int)f.Difficulty,
                f.PainOrDiscomfort, f.BreathingDifficulty, f.FormIssues, f.Comment));
        return WorkoutSession.Rehydrate(row.Id, row.Date, row.RoutineId, row.StartedAt, row.EndedAt,
            row.TotalSeconds, row.Notes, sets, feedback);
    }
}
