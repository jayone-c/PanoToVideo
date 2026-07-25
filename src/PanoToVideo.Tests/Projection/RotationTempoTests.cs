using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

public class RotationTempoTests
{
    [Fact]
    public void 关闭镜头节奏_保持既有线性旋转()
    {
        var legacy = YawSchedule.YawAt(30, 60, 180, RotationDirection.Clockwise, 15);
        var tempo = new RotationTempo();

        var actual = YawSchedule.YawAt(30, 60, 180, RotationDirection.Clockwise, 15, tempo, durationSeconds: 6);

        Assert.Equal(legacy, actual, 6);
    }

    [Fact]
    public void 慢转区间_每帧角度增量更小()
    {
        var tempo = new RotationTempo(Enabled: true, StartSeconds: 1, TransitionSeconds: 1, HoldSeconds: 2, SlowSpeedPercent: 30);

        var normalIncrement = YawSchedule.YawAt(11, 61, 180, RotationDirection.Clockwise, 0, tempo, durationSeconds: 6)
            - YawSchedule.YawAt(10, 61, 180, RotationDirection.Clockwise, 0, tempo, durationSeconds: 6);
        var slowIncrement = YawSchedule.YawAt(31, 61, 180, RotationDirection.Clockwise, 0, tempo, durationSeconds: 6)
            - YawSchedule.YawAt(30, 61, 180, RotationDirection.Clockwise, 0, tempo, durationSeconds: 6);

        Assert.True(slowIncrement < normalIncrement * 0.5);
    }

    [Fact]
    public void 慢转区间_末帧仍完成原总旋转角度()
    {
        var tempo = new RotationTempo(Enabled: true, StartSeconds: 1, TransitionSeconds: 1, HoldSeconds: 2, SlowSpeedPercent: 30);

        var yaw = YawSchedule.YawAt(60, 61, 180, RotationDirection.Clockwise, 20, tempo, durationSeconds: 6);

        Assert.Equal(200, yaw, 6);
    }

    [Fact]
    public void 慢转区间超出视频时长_不作为有效节奏执行()
    {
        var tempo = new RotationTempo(Enabled: true, StartSeconds: 5, TransitionSeconds: 1, HoldSeconds: 3, SlowSpeedPercent: 30);

        Assert.False(tempo.IsUsableFor(durationSeconds: 6));
    }
}
