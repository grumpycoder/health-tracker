namespace FitRecoveryLog.Data;

public class WeeklyReview : EntityBase
{
    public DateOnly WeekStart { get; set; }
    public int WorkoutsCompleted { get; set; }
    public int RecoveryDays { get; set; }
    public int? AverageWorkoutMinutes { get; set; }
    public double? WeightChangeLbs { get; set; }
    public double? WaistChangeInches { get; set; }
    public string? BestPerformanceNote { get; set; }
    public string? NutritionObservations { get; set; }
    public string? SleepRecoveryObservations { get; set; }
    public string? SuggestedFocus { get; set; }
}
