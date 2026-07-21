using PanoToVideo.Core.Parameters;

namespace PanoToVideo.Core.Projection;

/// <summary>
/// Yaw 帧调度（开发规划 §1.4 第2步）。
/// 360° 整数倍 -> 无缝循环 t/N（避免循环边界重复首帧导致一帧停顿）；
/// 否则 -> t/(N-1)。
/// 顺/逆时针决定 Yaw 增量正负。
/// </summary>
public static class YawSchedule
{
    public static double YawAt(
        int frameIndex, int totalFrames,
        int rotationDegrees, RotationDirection direction,
        double startYaw = 0.0)
    {
        if (totalFrames <= 1)
            return startYaw;

        var sign = direction == RotationDirection.Clockwise ? 1.0 : -1.0;
        var t = IsSeamlessLoop(rotationDegrees)
            ? (double)frameIndex / totalFrames
            : (double)frameIndex / (totalFrames - 1);
        return startYaw + sign * rotationDegrees * t;
    }

    /// <summary>旋转度数为 360° 整数倍（且非 0）时，循环视为无缝。</summary>
    public static bool IsSeamlessLoop(int rotationDegrees) =>
        rotationDegrees != 0 && rotationDegrees % 360 == 0;
}
