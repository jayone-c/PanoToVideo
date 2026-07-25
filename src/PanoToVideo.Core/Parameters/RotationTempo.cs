namespace PanoToVideo.Core.Parameters;

/// <summary>
/// 单个镜头慢转区间：正常旋转 → 平滑减速 → 慢转维持 → 平滑恢复。
/// SlowSpeedPercent 相对于视频内正常段速度；角度调度会自动补偿，以保持总旋转角度不变。
/// </summary>
public sealed record RotationTempo(
    bool Enabled = false,
    int StartSeconds = 8,
    int TransitionSeconds = 1,
    int HoldSeconds = 3,
    int SlowSpeedPercent = 30)
{
    public bool IsUsableFor(int durationSeconds) =>
        Enabled &&
        durationSeconds > 0 &&
        StartSeconds >= 0 &&
        TransitionSeconds > 0 &&
        HoldSeconds >= 0 &&
        SlowSpeedPercent is >= 10 and <= 90 &&
        StartSeconds + 2 * TransitionSeconds + HoldSeconds <= durationSeconds;
}
