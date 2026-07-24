using PanoToVideo.Core.Logging;
using PanoToVideo.Core.Naming;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Validation;

namespace PanoToVideo.Core.Exporting;

/// <summary>
/// 导出执行结果（供编排层决策重命名/清理）。
/// ProjectionDevice/EncoderName/UsedCpuFallback 由执行器上报真实链路信息，
/// 供 TaskLogRecord 准确记录实际设备与回退状态（PRD #5：不得伪称硬件加速）。
/// 用 init 属性追加，既有 4 参数构造调用无需改动。
/// </summary>
public sealed record ExportExecutionResult(bool Success, string? ErrorMessage, TimeSpan Elapsed, double AverageFps)
{
    /// <summary>实际投影设备标签（如 "RTX 4090 D" / "CPU"），未上报时编排层兜底 "GPU"。</summary>
    public string? ProjectionDevice { get; init; }

    /// <summary>实际编码器标签（如 "H.264 NVENC" / "libx264"），未上报时兜底 "H.264"。</summary>
    public string? EncoderName { get; init; }

    /// <summary>是否走了 CPU 回退路径。</summary>
    public bool UsedCpuFallback { get; init; }
}

/// <summary>
/// 单图导出编排（开发规划阶段1任务4 + §8 输出契约）。
/// 纯逻辑：校验 -> 预检(磁盘) -> 命名 -> 临时文件 -> 执行导出(委托) -> 原子重命名/清理。
/// IO/GPU 执行通过 <see cref="IExportExecutor"/> 注入，编排本身可单测。
/// </summary>
public sealed class SingleImageExportOrchestrator
{
    /// <summary>
    /// 编排单图导出。
    /// </summary>
    /// <param name="imageInfo">已读图的 ERP 校验输入</param>
    /// <param name="parameters">渲染参数（已校验）</param>
    /// <param name="preset">质量预设</param>
    /// <param name="outputDir">输出根目录（在其下建 exports/）</param>
    /// <param name="availableBytes">可用磁盘空间</param>
    /// <param name="existingOutputFiles">输出目录已存在文件名集合（重名检测）</param>
    /// <param name="executor">实际执行 GPU 导出的委托（写临时文件 tmpPath）</param>
    /// <returns>导出结果：成功返回最终路径，失败返回错误；含 TaskLogRecord。</returns>
    public ExportResult Export(
        ImageInfo imageInfo,
        RenderParameters parameters,
        ExportPreset preset,
        string outputDir,
        long availableBytes,
        IReadOnlyCollection<string> existingOutputFiles,
        IExportExecutor executor,
        CancellationToken cancellationToken = default,
        IProgress<ExportProgress>? progress = null)
    {
        // 0. 参数校验（PRD：非法 FPS/FOV/尺寸在导出前阻断，不入编码）
        var paramValidator = new RenderParametersValidator();
        var paramValidation = paramValidator.Validate(parameters);
        if (!paramValidation.IsValid)
            return ExportResult.Failed($"参数校验失败: {string.Join("; ", paramValidation.Errors)}");

        // 1. ERP 校验
        var validator = new EquirectValidator();
        var validation = validator.Validate(imageInfo);
        if (!validation.IsValid)
            return ExportResult.Failed($"ERP 校验失败: {validation.Reason}");

        // 2. 预检：总帧数 + 预估体积 + 磁盘空间
        var precheck = ExportPrecheck.Check(preset, parameters.Width, parameters.Height,
            parameters.Fps, parameters.DurationSeconds, availableBytes);
        if (!precheck.CanProceed)
            return ExportResult.Failed(precheck.Reason);

        // 3. 命名 + 唯一路径
        var exportsDir = OutputNaming.CombineExportsDir(outputDir);
        var baseName = OutputNaming.BuildFileName(
            Path.GetFileNameWithoutExtension(imageInfo.SourcePath),
            parameters.Width, parameters.Height, parameters.DurationSeconds, parameters.RotationDegrees);
        var finalPath = OutputNaming.ResolveUniquePath(exportsDir, baseName, existingOutputFiles);

        // 4. 临时文件: {最终名}.{guid}.tmp.mp4
        var tmpPath = Path.Combine(exportsDir, $"{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp.mp4");

        // 5. 执行导出（委托写临时文件，支持取消与逐帧进度）
        ExportExecutionResult exec;
        try
        {
            exec = executor.Execute(tmpPath, imageInfo, parameters, preset, cancellationToken, progress);
        }
        catch (OperationCanceledException)
        {
            executor.Cleanup(tmpPath);
            return ExportResult.Failed("已取消");
        }
        catch (Exception ex)
        {
            executor.Cleanup(tmpPath);
            return ExportResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
        if (!exec.Success)
        {
            // 失败清理临时文件，不删源图
            executor.Cleanup(tmpPath);
            return ExportResult.Failed(exec.ErrorMessage ?? "导出执行失败", exec.Elapsed, exec.AverageFps);
        }

        // 6. 原子重命名 tmp -> 最终
        try
        {
            executor.AtomicMove(tmpPath, finalPath);
        }
        catch (Exception ex)
        {
            executor.Cleanup(tmpPath);
            return ExportResult.Failed($"原子重命名失败: {ex.Message}", exec.Elapsed, exec.AverageFps);
        }

        var log = new TaskLogRecord(parameters,
            ProjectionDevice: exec.ProjectionDevice ?? "GPU",
            EncoderName: exec.EncoderName ?? "H.264",
            UsedCpuFallback: exec.UsedCpuFallback,
            Elapsed: exec.Elapsed, AverageFps: exec.AverageFps,
            OutputPath: finalPath, ErrorMessage: null);
        return ExportResult.Ok(finalPath, log);
    }
}

/// <summary>导出执行接口（注入 GPU 实现，编排可单测）。</summary>
public interface IExportExecutor
{
    /// <summary>执行 GPU 导出，写到 tmpPath。支持取消与逐帧进度上报。</summary>
    ExportExecutionResult Execute(
        string tmpPath,
        ImageInfo imageInfo,
        RenderParameters parameters,
        ExportPreset preset,
        CancellationToken cancellationToken = default,
        IProgress<ExportProgress>? progress = null);

    /// <summary>原子重命名 tmp -> final。</summary>
    void AtomicMove(string tmpPath, string finalPath);

    /// <summary>清理临时文件（失败/取消时，不删源图）。</summary>
    void Cleanup(string tmpPath);
}

/// <summary>导出编排结果。</summary>
public sealed record ExportResult(bool Success, string? Error, string? OutputPath, TaskLogRecord? Log)
{
    public static ExportResult Ok(string outputPath, TaskLogRecord log) =>
        new(true, null, outputPath, log);

    public static ExportResult Failed(string error, TimeSpan elapsed = default, double fps = 0) =>
        new(false, error, null, null);
}
