namespace FitRecoveryLog.Data;

/// <summary>A reusable routine: an ordered list of exercises.</summary>
public class WorkoutRoutine : EntityBase
{
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    /// <summary>Archived routines are kept (with their history) but hidden from the active
    /// routine list; they can be restored. Distinct from a soft-delete (<see cref="EntityBase.IsDeleted"/>).</summary>
    public bool Archived { get; set; }
    public List<RoutineExercise> Exercises { get; set; } = new();
}
