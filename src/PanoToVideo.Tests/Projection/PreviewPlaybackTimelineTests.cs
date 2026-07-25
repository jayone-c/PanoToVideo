using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

public class PreviewPlaybackTimelineTests
{
    [Fact]
    public void 两倍速播放_按经过时间推进两倍()
    {
        var next = PreviewPlaybackTimeline.Advance(currentSeconds: 3, elapsedSeconds: 0.25, playbackRate: 2, durationSeconds: 10);

        Assert.Equal(3.5, next.TimeSeconds, 6);
        Assert.False(next.HasReachedEnd);
    }

    [Fact]
    public void 播放到结尾_钳制到视频末尾并停止()
    {
        var next = PreviewPlaybackTimeline.Advance(currentSeconds: 9.8, elapsedSeconds: 0.2, playbackRate: 2, durationSeconds: 10);

        Assert.Equal(10, next.TimeSeconds, 6);
        Assert.True(next.HasReachedEnd);
    }
}
