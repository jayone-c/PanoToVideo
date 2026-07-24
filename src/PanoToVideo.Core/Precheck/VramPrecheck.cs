namespace PanoToVideo.Core.Precheck;

/// <summary>
/// VRAM 预检结果。availableBytes 为当前所选适配器的专用显存容量；
/// DXGI 在此路径中不能可靠给出实时空闲值，因此按保守可用比例预检。
/// </summary>
public sealed record VramPrecheckResult(bool CanProceed, string Reason, long? EstimatedVramBytes, long? AvailableVramBytes)
{
    public static VramPrecheckResult Ok(long estimated, long available) =>
        new(true, string.Empty, estimated, available);
}

/// <summary>
/// GPU 显存预检。估算 ERP 纹理、RGBA 渲染目标、NV12 输出纹理与编码缓冲的峰值；
/// 专用显存未知时不阻断（核显/共享显存），由实际初始化错误触发 CPU 回退。
/// </summary>
public static class VramPrecheck
{
    public static long EstimateRequiredBytes(int inputWidth, int inputHeight, int outputWidth, int outputHeight)
    {
        // ERP RGBA + 两张 BGRA 工作纹理 + NV12 帧及编码器余量。
        var erp = (long)inputWidth * inputHeight * 4;
        var output = (long)outputWidth * outputHeight;
        return erp + output * 4 * 2 + output * 3 / 2 * 6;
    }

    public static VramPrecheckResult Check(
        int inputWidth, int inputHeight, int outputWidth, int outputHeight,
        long dedicatedVideoMemoryBytes, double usableRatio = 0.8)
    {
        if (usableRatio <= 0 || usableRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(usableRatio));

        var estimated = EstimateRequiredBytes(inputWidth, inputHeight, outputWidth, outputHeight);
        if (dedicatedVideoMemoryBytes <= 0)
            return new VramPrecheckResult(true, "共享显存或显存容量未知，将在启动时继续检查", estimated, null);

        var usable = (long)(dedicatedVideoMemoryBytes * usableRatio);
        if (estimated > usable)
            return new VramPrecheckResult(false,
                $"显存不足：预计需要 {estimated / 1024 / 1024} MB，可安全使用 {usable / 1024 / 1024} MB",
                estimated, usable);

        return VramPrecheckResult.Ok(estimated, usable);
    }
}
