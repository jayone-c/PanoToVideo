using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// Yaw 帧调度 TDD 测试。
/// 契约：开发规划 §1.4 第2步。
/// 360° 整数倍 -> 无缝循环 t/N（避免循环边界重复首帧导致停顿）；
/// 否则 -> t/(N-1)。
/// 顺/逆时针决定 Yaw 增量正负。
/// </summary>
public class YawScheduleTests
{
    [Fact]
    public void 首帧_返回起始Yaw()
    {
        Assert.Equal(0.0, YawSchedule.YawAt(0, 60, 360, RotationDirection.Clockwise));
    }

    [Fact]
    public void 无缝循环360_末帧用t除N()
    {
        // N=60, rotation=360: 末帧 t=59/60 -> 360*59/60 = 354
        var last = YawSchedule.YawAt(59, 60, 360, RotationDirection.Clockwise);
        Assert.Equal(354.0, last, 3);
    }

    [Fact]
    public void 无缝循环_帧间隔均匀()
    {
        // 360/60 = 6° per frame
        for (int i = 0; i < 60; i++)
        {
            var yaw = YawSchedule.YawAt(i, 60, 360, RotationDirection.Clockwise);
            Assert.Equal(i * 6.0, yaw, 3);
        }
    }

    [Fact]
    public void 非整数倍_末帧用t除N减一()
    {
        // N=60, rotation=180: 末帧 t=59/59 -> 180
        var last = YawSchedule.YawAt(59, 60, 180, RotationDirection.Clockwise);
        Assert.Equal(180.0, last, 3);
    }

    [Fact]
    public void 非整数倍_首末不重合()
    {
        var first = YawSchedule.YawAt(0, 60, 180, RotationDirection.Clockwise);
        var last = YawSchedule.YawAt(59, 60, 180, RotationDirection.Clockwise);
        Assert.NotEqual(first, last);
    }

    [Fact]
    public void Rot720_无缝循环()
    {
        // N=60, rotation=720: 末帧 720*59/60 = 708
        Assert.Equal(708.0, YawSchedule.YawAt(59, 60, 720, RotationDirection.Clockwise), 3);
    }

    [Fact]
    public void 逆时针_末帧朝向为负()
    {
        var cw = YawSchedule.YawAt(59, 60, 360, RotationDirection.Clockwise);
        var ccw = YawSchedule.YawAt(59, 60, 360, RotationDirection.Counterclockwise);
        Assert.Equal(-cw, ccw, 3);
    }

    [Fact]
    public void 顺逆时针_首帧一致()
    {
        var cw0 = YawSchedule.YawAt(0, 60, 360, RotationDirection.Clockwise);
        var ccw0 = YawSchedule.YawAt(0, 60, 360, RotationDirection.Counterclockwise);
        Assert.Equal(cw0, ccw0, 3);
    }

    [Fact]
    public void 单帧任务_返回起始Yaw()
    {
        Assert.Equal(0.0, YawSchedule.YawAt(0, 1, 360, RotationDirection.Clockwise));
    }

    [Theory]
    [InlineData(360, true)]
    [InlineData(720, true)]
    [InlineData(180, false)]
    [InlineData(400, false)]
    public void IsSeamlessLoop_判定正确(int rotation, bool expected)
    {
        Assert.Equal(expected, YawSchedule.IsSeamlessLoop(rotation));
    }
}
