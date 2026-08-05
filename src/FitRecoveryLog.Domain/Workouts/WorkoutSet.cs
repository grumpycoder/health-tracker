namespace FitRecoveryLog.Domain.Workouts;

/// <summary>A single performed set within a <see cref="WorkoutSession"/>. Local-identity
/// entity; mutated only through the session so set numbering stays consistent per exercise.</summary>
public sealed class WorkoutSet
{
    public Guid Id { get; }
    public Guid ExerciseDefinitionId { get; }
    public int SetNumber { get; private set; }
    public SetResult Result { get; private set; }
    public bool Completed { get; private set; }

    internal WorkoutSet(Guid id, Guid exerciseDefinitionId, int setNumber, SetResult result, bool completed)
    {
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException("An exercise definition is required.", nameof(exerciseDefinitionId));
        Id = id;
        ExerciseDefinitionId = exerciseDefinitionId;
        SetNumber = setNumber;
        Result = result;
        Completed = completed;
    }

    public static WorkoutSet Rehydrate(Guid id, Guid exerciseDefinitionId, int setNumber, SetResult result, bool completed) =>
        new(id, exerciseDefinitionId, setNumber, result, completed);

    internal void SetNumberTo(int number) => SetNumber = number;
    internal void Update(SetResult result, bool completed) { Result = result; Completed = completed; }
}
