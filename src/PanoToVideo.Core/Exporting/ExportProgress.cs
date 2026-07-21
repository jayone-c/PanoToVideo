namespace PanoToVideo.Core.Exporting;

/// <summary>
/// 导出逐帧进度（开发规划阶段2任务2、任务6）。
/// 分阶段上报投影FPS与编码FPS（非伪造串行"渲染中->编码中"），供队列进度与ETA推导。
/// </summary>
public sealed record ExportProgress(
    int FrameIndex,
    int TotalFrames,
    double ProjectionFps,
    double EncodingFps,
    TimeSpan Elapsed)
{
    public double ProgressFraction => TotalFrames > 0 ? (double)(FrameIndex + 1) / TotalFrames : 0;
}
