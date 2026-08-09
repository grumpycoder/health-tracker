namespace FitRecoveryLog.Data;

/// <summary>Which angle a progress photo was taken from — drives pose-matched comparison.</summary>
public enum PhotoPose { Front = 0, Back = 1, Left = 2, Right = 3 }

/// <summary>
/// One progress photo: a pose taken on a date. Multiple per day (one per pose). The image bytes
/// live on the device under AppData/photos; only <see cref="FileName"/> (a GUID.jpg) is stored/
/// synced — the file itself stays local, so like the legacy per-day photo it doesn't leave the phone.
/// </summary>
public class ProgressPhoto : EntityBase
{
    public DateOnly Date { get; set; }
    public PhotoPose Pose { get; set; }
    public string FileName { get; set; } = "";
}
