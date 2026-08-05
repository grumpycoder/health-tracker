using FitRecoveryLog.Application.Common;
using FitRecoveryLog.Domain.Workouts;

namespace FitRecoveryLog.Application.Workouts;

/// <summary>
/// Application use cases for workouts. Thin over the <see cref="WorkoutSession"/> aggregate.
/// <see cref="CompleteAsync"/> also runs the cross-aggregate rule — a completed workout marks
/// its day as a workout day — via <see cref="IDayTypeService"/>.
/// </summary>
public sealed class WorkoutService
{
    private readonly IWorkoutRepository _workouts;

    public WorkoutService(IWorkoutRepository workouts) => _workouts = workouts;

    public Task<IReadOnlyList<WorkoutSession>> ListAsync(CancellationToken ct = default) => _workouts.ListAsync(ct);
    public Task<WorkoutSession?> GetAsync(Guid id, CancellationToken ct = default) => _workouts.GetAsync(id, ct);

    public async Task<Result<Guid>> CreateAsync(DateOnly date, Guid? routineId = null, CancellationToken ct = default)
    {
        var session = WorkoutSession.Create(date, routineId);
        await _workouts.SaveAsync(session, ct);
        return Result<Guid>.Success(session.Id);
    }

    public Task<Result> SetDateAsync(Guid id, DateOnly date, CancellationToken ct = default) =>
        MutateAsync(id, s => s.SetDate(date), ct);

    public Task<Result> SetNotesAsync(Guid id, string? notes, CancellationToken ct = default) =>
        MutateAsync(id, s => s.SetNotes(notes), ct);

    public Task<Result> SetDurationAsync(Guid id, int? seconds, CancellationToken ct = default) =>
        MutateAsync(id, s => s.SetDurationSeconds(seconds), ct);

    public Task<Result> UpdateSetAsync(Guid id, Guid setId, SetResult result, bool completed, CancellationToken ct = default) =>
        MutateAsync(id, s => s.UpdateSet(setId, result, completed), ct);

    public Task<Result> RemoveSetAsync(Guid id, Guid setId, CancellationToken ct = default) =>
        MutateAsync(id, s => s.RemoveSet(setId), ct);

    public Task<Result> RemoveFeedbackAsync(Guid id, Guid feedbackId, CancellationToken ct = default) =>
        MutateAsync(id, s => s.RemoveFeedback(feedbackId), ct);

    public async Task<Result<Guid>> AddSetAsync(Guid id, Guid exerciseDefinitionId, SetResult result, bool completed = false, CancellationToken ct = default)
    {
        var session = await _workouts.GetAsync(id, ct);
        if (session is null) return Result<Guid>.Failure("Workout not found.");
        Guid setId;
        try { setId = session.AddSet(exerciseDefinitionId, result, completed); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException) { return Result<Guid>.Failure(ex.Message); }
        await _workouts.SaveAsync(session, ct);
        return Result<Guid>.Success(setId);
    }

    public async Task<Result<Guid>> SetFeedbackAsync(Guid id, Guid exerciseDefinitionId, Difficulty difficulty,
        bool pain, bool breathing, bool form, string? comment, CancellationToken ct = default)
    {
        var session = await _workouts.GetAsync(id, ct);
        if (session is null) return Result<Guid>.Failure("Workout not found.");
        Guid feedbackId;
        try { feedbackId = session.SetFeedback(exerciseDefinitionId, difficulty, pain, breathing, form, comment); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Result<Guid>.Failure(ex.Message); }
        await _workouts.SaveAsync(session, ct);
        return Result<Guid>.Success(feedbackId);
    }

    /// <summary>Finish a workout and mark its date as a workout day.</summary>
    public async Task<Result> CompleteAsync(Guid id, DateTime endedAt, CancellationToken ct = default)
    {
        var session = await _workouts.GetAsync(id, ct);
        if (session is null) return Result.Failure("Workout not found.");
        try { session.Finish(endedAt); }
        catch (ArgumentException ex) { return Result.Failure(ex.Message); }
        // Finish() raised WorkoutCompleted; the repository decorator dispatches it on save
        // (-> marks the workout day). The use case just mutates and saves.
        await _workouts.SaveAsync(session, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var session = await _workouts.GetAsync(id, ct);
        if (session is null) return Result.Failure("Workout not found.");
        await _workouts.RemoveAsync(id, ct);
        return Result.Success();
    }

    private async Task<Result> MutateAsync(Guid id, Action<WorkoutSession> change, CancellationToken ct)
    {
        var session = await _workouts.GetAsync(id, ct);
        if (session is null) return Result.Failure("Workout not found.");
        try { change(session); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Result.Failure(ex.Message);
        }
        await _workouts.SaveAsync(session, ct);
        return Result.Success();
    }
}
