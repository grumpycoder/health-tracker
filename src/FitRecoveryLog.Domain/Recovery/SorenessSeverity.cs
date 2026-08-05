namespace FitRecoveryLog.Domain.Recovery;

/// <summary>Severity of muscle soreness. Domain-owned; mapped to storage by ordinal.</summary>
public enum SorenessSeverity
{
    None = 0,
    Mild,
    Moderate,
    Severe,
}
