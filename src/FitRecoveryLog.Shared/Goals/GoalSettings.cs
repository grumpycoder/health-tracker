namespace FitRecoveryLog.Data;

/// <summary>
/// The user's coaching goals + macro/hydration targets, as a single synced record so the web can
/// edit them and the AI (and Trends targets) reason with real numbers instead of neutral defaults.
/// Treated as a singleton — the latest non-deleted row wins. Ranges are min/max (0/0 = unset);
/// calories, carbs, and water split by day type (rest vs active), mirroring the phone's
/// NutritionGoals. The phone still keeps its own on-device goals for now; this row is web-authored.
/// </summary>
public class GoalSettings : EntityBase
{
    public string? CoachingGoals { get; set; }
    public bool IncludeCessationData { get; set; }

    public int ProteinMin { get; set; }
    public int ProteinMax { get; set; }
    public int FatMin { get; set; }
    public int FatMax { get; set; }
    public int FiberMin { get; set; }
    public int FiberMax { get; set; }
    public int AddedSugarMax { get; set; }

    public int CaloriesRestMin { get; set; }
    public int CaloriesRestMax { get; set; }
    public int CaloriesActiveMin { get; set; }
    public int CaloriesActiveMax { get; set; }

    public int CarbsRestMin { get; set; }
    public int CarbsRestMax { get; set; }
    public int CarbsActiveMin { get; set; }
    public int CarbsActiveMax { get; set; }

    public int WaterRestMin { get; set; }
    public int WaterRestMax { get; set; }
    public int WaterActiveMin { get; set; }
    public int WaterActiveMax { get; set; }

    // Body-measurement goals (null = unset). Direction (reduce vs gain) is inferred from the
    // current measurement vs the target, so a single value covers "cut to X" and "build to X".
    public double? GoalWeightLbs { get; set; }
    public double? GoalWaistInches { get; set; }
    public double? GoalBodyFatPercent { get; set; }
    public double? GoalChestInches { get; set; }
    public double? GoalArmsInches { get; set; }
    public double? GoalThighsInches { get; set; }
    public double? GoalShouldersInches { get; set; }
    public double? GoalCalvesInches { get; set; }
}
