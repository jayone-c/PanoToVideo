using PanoToVideo.Core.Parameters;

namespace PanoToVideo.Core.Logging;

/// <summary>
/// 任务日志记录（PRD 输出、开发规划 §阶段2任务8）。
/// 记录参数、设备、编码器、耗时、平均FPS、输出文件、失败详情，供 UI 查看与排查。
/// UsedCpuFallback 必须显式标注，不得伪称硬件加速（PRD #5）。
/// </summary>
public sealed record TaskLogRecord(
    RenderParameters Parameters,
    string ProjectionDevice,
    string EncoderName,
    bool UsedCpuFallback,
    TimeSpan Elapsed,
    double AverageFps,
    string OutputPath,
    string? ErrorMessage);
