using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.Core.Queue;

/// <summary>
/// 队列项模型（开发规划阶段2任务1）。
/// 缩略图、源文件名、尺寸、状态、进度、实际FPS、输出路径、错误原因。
/// 状态转换委托 <see cref="TaskStateTransitions"/>（非法转换抛异常）。
/// </summary>
public sealed class QueueItem
{
    public string SourceFileName { get; }
    public int Width { get; }
    public int Height { get; }
    public TaskStatus Status { get; private set; } = TaskStatus.PendingValidation;
    public QueueProgress Progress { get; } = new();
    public double AverageFps { get; private set; }
    public string? OutputPath { get; private set; }
    public string? ErrorMessage { get; private set; }
    public byte[]? Thumbnail { get; private set; }

    public QueueItem(string sourceFileName, int width, int height)
    {
        SourceFileName = sourceFileName;
        Width = width;
        Height = height;
    }

    /// <summary>状态转换（委托 TaskStateTransitions，非法抛 InvalidOperationException）。</summary>
    public void TransitionTo(TaskStatus to)
    {
        Status = TaskStateTransitions.Transition(Status, to);
    }

    public void SetThumbnail(byte[] thumbnail) => Thumbnail = thumbnail;

    public void UpdateProgress(int framesDone, int totalFrames, double projectionFps, double encodingFps, TimeSpan elapsed) =>
        Progress.Update(framesDone, totalFrames, projectionFps, encodingFps, elapsed);

    public void SetOutput(string outputPath, double averageFps)
    {
        OutputPath = outputPath;
        AverageFps = averageFps;
    }

    public void SetError(string error) => ErrorMessage = error;
}
