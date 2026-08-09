namespace FitRecoveryLog.Data;

/// <summary>
/// An editable AI prompt template, stored so prompts can be tuned from the app/web without a
/// build + deploy. One row per <see cref="PromptKey"/> (a stable id like "daily_check"). Rides the
/// existing sync so an edit on the web reaches the phone. When no row exists (or its text is blank),
/// the coach falls back to the embedded default in <c>PromptDefaults</c>, so the app always works.
/// </summary>
public class PromptTemplate : EntityBase
{
    /// <summary>Stable identifier for the prompt this overrides (see <c>PromptDefaults</c>).</summary>
    public string PromptKey { get; set; } = "";

    /// <summary>The template text, with named <c>{placeholders}</c> the coach fills at build time.</summary>
    public string Text { get; set; } = "";
}
