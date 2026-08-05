namespace FitRecoveryLog.Domain.Notes;

/// <summary>A timestamped freeform note. Text is required.</summary>
public sealed class Note
{
    public Guid Id { get; }
    public DateTime Time { get; private set; }
    public string Text { get; private set; }

    private Note(Guid id, DateTime time, string text) { Id = id; Time = time; Text = text; }

    public static Note Create(DateTime time, string text)
    {
        var note = new Note(Guid.NewGuid(), time, "");
        note.SetText(text);
        return note;
    }

    public static Note Rehydrate(Guid id, DateTime time, string text) => new(id, time, text ?? "");

    public void SetTime(DateTime time) => Time = time;

    public void SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Note text is required.", nameof(text));
        Text = text.Trim();
    }
}
