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
        => YawAt(frameIndex, totalFrames, rotationDegrees, direction, startYaw, null, durationSeconds: 0);

    /// <summary>按镜头节奏计算当前帧 Yaw。启用慢转时自动归一化角度，确保成片总旋转角度不变。</summary>
    public static double YawAt(
        int frameIndex, int totalFrames,
        int rotationDegrees, RotationDirection direction,
        double startYaw, RotationTempo? tempo, double durationSeconds)
    {
        if (totalFrames <= 1)
            return startYaw;

        var sign = direction == RotationDirection.Clockwise ? 1.0 : -1.0;
        var t = IsSeamlessLoop(rotationDegrees)
            ? (double)frameIndex / totalFrames
            : (double)frameIndex / (totalFrames - 1);
        if (tempo is null || !tempo.IsUsableFor((int)Math.Round(durationSeconds)))
            return startYaw + sign * rotationDegrees * t;

        var elapsedSeconds = t * durationSeconds;
        var totalWeight = IntegratedSpeed(durationSeconds, tempo);
        var progress = totalWeight <= 0 ? t : IntegratedSpeed(elapsedSeconds, tempo) / totalWeight;
        return startYaw + sign * rotationDegrees * progress;
    }

    /// <summary>RenderParameters 便捷重载，保证所有渲染后端使用同一套节奏。</summary>
    public static double YawAt(int frameIndex, RenderParameters parameters) =>
        YawAt(frameIndex, parameters.TotalFrames, parameters.RotationDegrees, parameters.Direction,
            parameters.StartYaw, parameters.RotationTempo, parameters.DurationSeconds);

    private static double IntegratedSpeed(double timeSeconds, RotationTempo tempo)
    {
        var time = Math.Max(0, timeSeconds);
        var start = tempo.StartSeconds;
        var transition = tempo.TransitionSeconds;
        var hold = tempo.HoldSeconds;
        var slow = tempo.SlowSpeedPercent / 100d;
        var area = Math.Min(time, start);
        var cursor = start;

        if (time <= cursor) return area;
        var deceleration = Math.Min(time - cursor, transition);
        area += IntegrateSmoothSpeed(1, slow, deceleration, transition);
        cursor += transition;

        if (time <= cursor) return area;
        var slowHold = Math.Min(time - cursor, hold);
        area += slow * slowHold;
        cursor += hold;

        if (time <= cursor) return area;
        var recovery = Math.Min(time - cursor, transition);
        area += IntegrateSmoothSpeed(slow, 1, recovery, transition);
        cursor += transition;

        if (time > cursor) area += time - cursor;
        return area;
    }

    /// <summary>余弦缓动速度曲线的解析积分，端点速度连续且加速度平滑。</summary>
    private static double IntegrateSmoothSpeed(double fromSpeed, double toSpeed, double elapsed, double duration)
    {
        if (elapsed <= 0) return 0;
        var u = Math.Clamp(elapsed / duration, 0, 1);
        var easedArea = u / 2d - Math.Sin(Math.PI * u) / (2d * Math.PI);
        return duration * (fromSpeed * u + (toSpeed - fromSpeed) * easedArea);
    }

    /// <summary>旋转度数为 360° 整数倍（且非 0）时，循环视为无缝。</summary>
    public static bool IsSeamlessLoop(int rotationDegrees) =>
        rotationDegrees != 0 && rotationDegrees % 360 == 0;
}
