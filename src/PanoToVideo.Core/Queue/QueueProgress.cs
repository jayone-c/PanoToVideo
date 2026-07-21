namespace PanoToVideo.Core.Queue;

/// <summary>
/// 队列项进度（开发规划阶段2任务1、任务6）。
/// 帧进度 + 投影/编码实际 FPS（分阶段上报，非伪造串行）+ ETA。
/// </summary>
public sealed class QueueProgress
{
    public int FramesDone { get; private set; }
    public int TotalFrames { get; private set; }
    public double ProjectionFps { get; private set; }
    public double EncodingFps { get; private set; }
    public TimeSpan Elapsed { get; private set; }

    /// <summary>进度分数 [0,1]。</summary>
    public double ProgressFraction => TotalFrames > 0 ? (double)FramesDone / TotalFrames : 0;

    /// <summary>ETA：剩余帧 / min(投影FPS, 编码FPS)。FPS 为 0 时返回 null。</summary>
    public TimeSpan? Eta
    {
        get
        {
            var remaining = TotalFrames - FramesDone;
            if (remaining <= 0) return TimeSpan.Zero;
            var fps = Math.Min(ProjectionFps, EncodingFps);
            if (fps <= 0) return null;
            return TimeSpan.FromSeconds(remaining / fps);
        }
    }

    public void Update(int framesDone, int totalFrames, double projectionFps, double encodingFps, TimeSpan elapsed)
    {
        FramesDone = framesDone;
        TotalFrames = totalFrames;
        ProjectionFps = projectionFps;
        EncodingFps = encodingFps;
        Elapsed = elapsed;
    }
}
