namespace PanoToVideo.Core.Precheck;

/// <summary>
/// 内存预检结果（P0-5：100 张批量输入的内存安全闸门）。
/// CanProceed=false 时 Reason 给出用户可读原因（含实际预估与可用内存）。
/// </summary>
public sealed record MemoryPrecheckResult(bool CanProceed, string Reason, long EstimatedRgbaBytes, long AvailableBytes)
{
    public static MemoryPrecheckResult Ok(long estimated, long available) =>
        new(true, string.Empty, estimated, available);

    public static MemoryPrecheckResult Fail(long estimated, long available, string reason) =>
        new(false, reason, estimated, available);
}

/// <summary>
/// 内存预检（P0-5：开发规划 §阶段2任务4）。
/// 纯算式：单帧 RGBA 字节数 = w*h*4；按安全系数放大后与可用内存比较。
/// 100 张批量输入的峰值约为 max(单张常驻) + 渲染缓冲，此处用 safetyFactor 兜底。
/// 不依赖文件系统与 GC，便于单测。
/// </summary>
public static class MemoryPrecheck
{
    /// <summary>单张 ERP 解码为 RGBA 所需字节数（每像素 4 字节）。</summary>
    public static long EstimateRgbaBytes(int width, int height) => (long)width * height * 4;

    /// <summary>
    /// 内存预检：预估单张峰值 RGBA 占用 × 安全系数，与可用内存比较。
    /// </summary>
    /// <param name="largestImageBytes">单张最大图的 RGBA 字节数（调用方可用 EstimateRgbaBytes 计算）。</param>
    /// <param name="availableBytes">可用内存字节（调用方用 GC.GetGCMemoryInfo().TotalAvailableMemoryBytes）。</param>
    /// <param name="safetyFactor">安全系数，默认 1.5（覆盖渲染缓冲、GDI 对象、FFmpeg 子进程等开销）。</param>
    public static MemoryPrecheckResult Check(long largestImageBytes, long availableBytes, double safetyFactor = 1.5)
    {
        if (safetyFactor <= 0)
            throw new ArgumentOutOfRangeException(nameof(safetyFactor), "安全系数必须为正数");

        var estimated = (long)(largestImageBytes * safetyFactor);

        if (availableBytes <= 0)
            return MemoryPrecheckResult.Fail(estimated, availableBytes,
                $"可用内存为 {availableBytes} 字节，无法启动批量任务");

        if (estimated > availableBytes)
            return MemoryPrecheckResult.Fail(estimated, availableBytes,
                $"内存不足：单张峰值预估 {estimated} 字节（含安全系数 {safetyFactor}），可用 {availableBytes} 字节");

        return MemoryPrecheckResult.Ok(estimated, availableBytes);
    }
}
