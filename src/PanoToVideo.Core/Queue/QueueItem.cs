using System.ComponentModel;
using System.Runtime.CompilerServices;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.Core.Queue;

/// <summary>
/// 队列项模型（开发规划阶段2任务1）。
/// 缩略图、源文件名、尺寸、状态、进度、实际FPS、输出路径、错误原因。
/// 状态转换委托 <see cref="TaskStateTransitions"/>（非法转换抛异常）。
/// 实现 INotifyPropertyChanged 供 UI 绑定实时刷新（Core 无 UI 依赖，System.ComponentModel 非 UI）。
/// </summary>
public sealed class QueueItem : INotifyPropertyChanged
{
    public string SourceFileName { get; }
    public int Width { get; }
    public int Height { get; }

    private TaskStatus _status = TaskStatus.PendingValidation;
    public TaskStatus Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(DetailText)); }
    }

    public QueueProgress Progress { get; } = new();
    private double _averageFps;
    public double AverageFps { get { return _averageFps; } private set { _averageFps = value; OnPropertyChanged(); } }
    private string? _outputPath;
    public string? OutputPath { get { return _outputPath; } private set { _outputPath = value; OnPropertyChanged(); } }
    private string? _errorMessage;
    public string? ErrorMessage { get { return _errorMessage; } private set { _errorMessage = value; OnPropertyChanged(); } }
    private bool _usedCpuFallback;
    public bool UsedCpuFallback { get => _usedCpuFallback; private set { _usedCpuFallback = value; OnPropertyChanged(); } }
    private string? _encoderName;
    public string? EncoderName { get => _encoderName; private set { _encoderName = value; OnPropertyChanged(); } }
    public byte[]? Thumbnail { get; private set; }

    /// <summary>UI 绑定用进度文本。</summary>
    public string StatusDisplay => Status switch
    {
        TaskStatus.PendingValidation => "校验中",
        TaskStatus.Pending => "等待导出",
        TaskStatus.Processing => "导出中",
        TaskStatus.Completed => "已完成",
        TaskStatus.Failed => "失败",
        TaskStatus.Cancelled => "已取消",
        _ => "未知状态",
    };

    /// <summary>UI 绑定用进度文本。</summary>
    public string ProgressText => Status switch
    {
        TaskStatus.Completed => $"完成 {AverageFps:F0}fps",
        TaskStatus.Failed => "失败",
        TaskStatus.Cancelled => "已取消",
        TaskStatus.Processing => Progress.TotalFrames > 0
            ? $"{Progress.FramesDone}/{Progress.TotalFrames} · {CurrentFps:F0}fps"
            : "处理中",
        _ => "—",
    };

    /// <summary>队列详情：导出时显示已耗时和预计剩余，完成后显示实际输出路径。</summary>
    public string DetailText => Status switch
    {
        TaskStatus.Processing => $"已用 {FormatTime(Progress.Elapsed)} · 预计 {FormatTime(Progress.Eta)}",
        TaskStatus.Completed when !string.IsNullOrWhiteSpace(OutputPath) => $"{(UsedCpuFallback ? "CPU 回退 · " : "")}输出：{OutputPath}",
        TaskStatus.Failed when !string.IsNullOrWhiteSpace(ErrorMessage) => ErrorMessage!,
        _ => "",
    };

    private double CurrentFps => Progress.EncodingFps > 0
        ? Math.Min(Progress.ProjectionFps, Progress.EncodingFps)
        : Progress.ProjectionFps;

    private static string FormatTime(TimeSpan? value) => value is null ? "计算中" : FormatTime(value.Value);
    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

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

    public void SetThumbnail(byte[] thumbnail)
    {
        Thumbnail = thumbnail;
        OnPropertyChanged(nameof(Thumbnail));
    }

    public void UpdateProgress(int framesDone, int totalFrames, double projectionFps, double encodingFps, TimeSpan elapsed)
    {
        Progress.Update(framesDone, totalFrames, projectionFps, encodingFps, elapsed);
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(DetailText));
    }

    public void SetOutput(string outputPath, double averageFps, string? encoderName = null, bool usedCpuFallback = false)
    {
        OutputPath = outputPath;
        AverageFps = averageFps;
        EncoderName = encoderName;
        UsedCpuFallback = usedCpuFallback;
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(DetailText));
    }

    public void SetError(string error)
    {
        ErrorMessage = error;
        OnPropertyChanged(nameof(DetailText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
