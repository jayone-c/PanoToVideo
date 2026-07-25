using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Validation;

namespace PanoToVideo.Core.Exporting;

/// <summary>
/// 将硬件执行器与 CPU 回退执行器组合为同一个导出契约。
/// 只有硬件执行失败且任务未被用户取消时，才清理临时文件并从同一任务自动改走 CPU。
/// </summary>
public sealed class FallbackExportExecutor : IExportExecutor
{
    private readonly IExportExecutor _hardware;
    private readonly IExportExecutor _cpuFallback;

    public FallbackExportExecutor(IExportExecutor hardware, IExportExecutor cpuFallback)
    {
        _hardware = hardware;
        _cpuFallback = cpuFallback;
    }

    public ExportExecutionResult Execute(
        string tmpPath, ImageInfo imageInfo, RenderParameters parameters, ExportPreset preset,
        CancellationToken cancellationToken = default, IProgress<ExportProgress>? progress = null)
    {
        var hardwareResult = _hardware.Execute(tmpPath, imageInfo, parameters, preset, cancellationToken, progress);
        if (hardwareResult.Success || cancellationToken.IsCancellationRequested)
            return hardwareResult;

        _hardware.Cleanup(tmpPath);
        var cpuResult = _cpuFallback.Execute(tmpPath, imageInfo, parameters, preset, cancellationToken, progress);
        if (cpuResult.Success)
        {
            return cpuResult with
            {
                Elapsed = hardwareResult.Elapsed + cpuResult.Elapsed,
                EncoderName = "libx264（硬件编码失败后回退）",
                UsedCpuFallback = true,
            };
        }

        return cpuResult with
        {
            ErrorMessage = $"硬件导出失败：{hardwareResult.ErrorMessage ?? "未知错误"}；CPU 回退失败：{cpuResult.ErrorMessage ?? "未知错误"}",
            Elapsed = hardwareResult.Elapsed + cpuResult.Elapsed,
            UsedCpuFallback = true,
        };
    }

    public void AtomicMove(string tmpPath, string finalPath) => _cpuFallback.AtomicMove(tmpPath, finalPath);

    public void Cleanup(string tmpPath)
    {
        _hardware.Cleanup(tmpPath);
        _cpuFallback.Cleanup(tmpPath);
    }
}
