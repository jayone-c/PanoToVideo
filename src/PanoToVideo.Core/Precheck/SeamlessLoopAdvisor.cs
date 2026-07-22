using PanoToVideo.Core.Projection;

namespace PanoToVideo.Core.Precheck;

/// <summary>无缝循环判定结果。</summary>
public sealed record SeamlessLoopResult(bool IsSeamless, string? WarningMessage);

/// <summary>
/// 无缝循环提示（开发规划阶段3任务5、PRD #20）。
/// 旋转度数非 360° 整数倍时提示"非无缝循环"。
/// 包装 YawSchedule.IsSeamlessLoop，补充用户可读提示。
/// </summary>
public static class SeamlessLoopAdvisor
{
    public static SeamlessLoopResult Advise(int rotationDegrees)
    {
        if (YawSchedule.IsSeamlessLoop(rotationDegrees))
            return new SeamlessLoopResult(true, null);

        return new SeamlessLoopResult(
            false,
            $"旋转度数 {rotationDegrees}° 非无缝循环（需为 360° 整数倍），首尾视角不一致");
    }
}
