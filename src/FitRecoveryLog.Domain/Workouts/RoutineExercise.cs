namespace FitRecoveryLog.Domain.Workouts;

/// <summary>
/// An exercise within a <see cref="Routine"/> — a local-identity entity inside the routine
/// aggregate. Its <see cref="Order"/> and <see cref="Prescription"/> are only changed through
/// the aggregate root, so the routine can keep its ordering invariant.
/// </summary>
public sealed class RoutineExercise
{
    public Guid Id { get; }
    public Guid ExerciseDefinitionId { get; }
    public int Order { get; private set; }
    public ExercisePrescription Prescription { get; private set; }

    internal RoutineExercise(Guid id, Guid exerciseDefinitionId, int order, ExercisePrescription prescription)
    {
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException("An exercise definition is required.", nameof(exerciseDefinitionId));
        Id = id;
        ExerciseDefinitionId = exerciseDefinitionId;
        Order = order;
        Prescription = prescription;
    }

    /// <summary>Reconstruct from persisted state (repositories). New exercises are created
    /// through <see cref="Routine.AddExercise"/> instead.</summary>
    public static RoutineExercise Rehydrate(Guid id, Guid exerciseDefinitionId, int order, ExercisePrescription prescription) =>
        new(id, exerciseDefinitionId, order, prescription);

    internal void SetOrder(int order) => Order = order;
    internal void SetPrescription(ExercisePrescription prescription) => Prescription = prescription;
}
