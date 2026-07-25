namespace PanoToVideo.Core.Projection;

/// <summary>低清镜头预览的时间轴推进规则；与 UI 定时器解耦，便于稳定测试。</summary>
public static class PreviewPlaybackTimeline
{
    public static PreviewPlaybackStep Advance(double currentSeconds, double elapsedSeconds, double playbackRate, double durationSeconds)
    {
        var duration = Math.Max(0, durationSeconds);
        var elapsed = Math.Max(0, elapsedSeconds);
        var rate = Math.Max(0, playbackRate);
        var next = Math.Clamp(currentSeconds + elapsed * rate, 0, duration);
        return new PreviewPlaybackStep(next, next >= duration);
    }
}

public readonly record struct PreviewPlaybackStep(double TimeSeconds, bool HasReachedEnd);
