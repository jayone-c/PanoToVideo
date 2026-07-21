namespace PanoToVideo.Core.Precheck;

/// <summary>预检结果。</summary>
public sealed record PrecheckResult(bool CanProceed, string Reason, long EstimatedBytes, int TotalFrames);

/// <summary>
/// 导出预检（开发规划 §阶段2任务5、§8）。
/// 总帧数=时长×FPS；码率按像素×帧率换算；预估体积×1.15；磁盘不足不开始。
/// </summary>
public static class ExportPrecheck
{
    public static int TotalFrames(int durationSeconds, int fps) => durationSeconds * fps;

    /// <summary>按输出像素数与帧率，从基准码率换算目标码率（bps）。</summary>
    public static double EstimateBitrate(ExportPreset preset, int width, int height, int fps)
    {
        var baseBitrate = ExportPresetConstants.BaseBitrateFor(preset);
        var basePixels = (double)(ExportPresetConstants.BaseWidth * ExportPresetConstants.BaseHeight * ExportPresetConstants.BaseFps);
        var pixels = (double)(width * height * fps);
        return baseBitrate * (pixels / basePixels);
    }

    /// <summary>预估输出体积（字节）= 码率 × 时长 × 1.15 / 8。</summary>
    public static long EstimateBytes(ExportPreset preset, int width, int height, int fps, int durationSeconds)
    {
        var bitrate = EstimateBitrate(preset, width, height, fps);
        return (long)(bitrate * durationSeconds * ExportPresetConstants.SizeReserveFactor / 8.0);
    }

    public static PrecheckResult Check(
        ExportPreset preset, int width, int height, int fps, int durationSeconds, long availableBytes)
    {
        var totalFrames = TotalFrames(durationSeconds, fps);
        var estimated = EstimateBytes(preset, width, height, fps, durationSeconds);

        if (availableBytes < estimated)
            return new PrecheckResult(
                false,
                $"磁盘空间不足：预估 {estimated} 字节，可用 {availableBytes} 字节",
                estimated, totalFrames);

        return new PrecheckResult(true, string.Empty, estimated, totalFrames);
    }
}
