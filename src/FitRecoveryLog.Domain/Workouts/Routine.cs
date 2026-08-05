namespace FitRecoveryLog.Domain.Workouts;

/// <summary>
/// A workout routine: a named, ordered set of prescribed exercises. Aggregate root — all
/// changes to its exercises go through it, so it can guarantee its invariants:
/// a routine always has a name, and its exercises are always contiguously ordered from 1.
/// Archiving hides a routine from active use without losing it (distinct from deletion).
/// </summary>
public sealed class Routine
{
    private readonly List<RoutineExercise> _exercises;

    public Guid Id { get; }
    public string Name { get; private set; }
    public string? Notes { get; private set; }
    public bool Archived { get; private set; }

    /// <summary>The routine's exercises, always in ascending prescribed order.</summary>
    public IReadOnlyList<RoutineExercise> Exercises =>
        _exercises.OrderBy(e => e.Order).ToList();

    private Routine(Guid id, string name, string? notes, bool archived, List<RoutineExercise> exercises)
    {
        Id = id;
        Name = name;
        Notes = notes;
        Archived = archived;
        _exercises = exercises;
    }

    /// <summary>Create a new routine. Name is required.</summary>
    public static Routine Create(string name, string? notes = null)
    {
        var routine = new Routine(Guid.NewGuid(), "", null, false, new List<RoutineExercise>());
        routine.Rename(name);
        routine.SetNotes(notes);
        return routine;
    }

    /// <summary>Reconstruct a routine from persisted state (used by repositories). Orders are
    /// normalized so stored gaps/dupes can't violate the invariant.</summary>
    public static Routine Rehydrate(Guid id, string name, string? notes, bool archived,
                                    IEnumerable<RoutineExercise> exercises)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        var routine = new Routine(id, name, string.IsNullOrWhiteSpace(notes) ? null : notes,
            archived, exercises.OrderBy(e => e.Order).ToList());
        routine.Normalize();
        return routine;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A routine must have a name.", nameof(name));
        Name = name.Trim();
    }

    public void SetNotes(string? notes) => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    public void Archive() => Archived = true;
    public void Restore() => Archived = false;

    /// <summary>Append an exercise; it takes the next order slot. Returns its id.</summary>
    public Guid AddExercise(Guid exerciseDefinitionId, ExercisePrescription prescription)
    {
        var nextOrder = _exercises.Count == 0 ? 1 : _exercises.Max(e => e.Order) + 1;
        var exercise = new RoutineExercise(Guid.NewGuid(), exerciseDefinitionId, nextOrder, prescription);
        _exercises.Add(exercise);
        Normalize();
        return exercise.Id;
    }

    public void RemoveExercise(Guid routineExerciseId)
    {
        var exercise = Find(routineExerciseId);
        _exercises.Remove(exercise);
        Normalize();
    }

    public void UpdateExercise(Guid routineExerciseId, ExercisePrescription prescription) =>
        Find(routineExerciseId).SetPrescription(prescription);

    /// <summary>Move an exercise to a new 1-based position; the rest close the gap.</summary>
    public void MoveExercise(Guid routineExerciseId, int newPosition)
    {
        if (newPosition < 1 || newPosition > _exercises.Count)
            throw new ArgumentOutOfRangeException(nameof(newPosition));
        var ordered = _exercises.OrderBy(e => e.Order).ToList();
        var moving = Find(routineExerciseId);
        ordered.Remove(moving);
        ordered.Insert(newPosition - 1, moving);
        for (var i = 0; i < ordered.Count; i++) ordered[i].SetOrder(i + 1);
    }

    private RoutineExercise Find(Guid routineExerciseId) =>
        _exercises.FirstOrDefault(e => e.Id == routineExerciseId)
        ?? throw new InvalidOperationException("Exercise is not part of this routine.");

    // Keep orders contiguous 1..N in current sequence.
    private void Normalize()
    {
        var ordered = _exercises.OrderBy(e => e.Order).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].SetOrder(i + 1);
    }
}
