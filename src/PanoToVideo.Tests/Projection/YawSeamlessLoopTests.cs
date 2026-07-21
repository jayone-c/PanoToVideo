using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// 360° 首尾视角一致性 TDD 测试（开发规划阶段1任务6）。
/// 验证 YawSchedule 无缝循环设计：360° 整数倍任务首尾视角相同（允许编码压缩差异，不允许几何跳变）。
/// </summary>
public class YawSeamlessLoopTests
{
    [Fact]
    public void Rot360_整数倍_首末帧Yaw相同()
    {
        // N=180(3秒60FPS), rotation=360: 末帧 t=179/180 -> 354°, 首帧0°
        // 无缝循环用 t/N：末帧 360*179/180=358°，与下一周期首帧 360°≡0° 连续
        // 关键：末帧 Yaw + 一帧增量 = 360°(=0°)，即首末衔接无停顿
        var first = YawSchedule.YawAt(0, 180, 360, RotationDirection.Clockwise);
        var last = YawSchedule.YawAt(179, 180, 360, RotationDirection.Clockwise);
        var nextCycleStart = YawSchedule.YawAt(180, 180, 360, RotationDirection.Clockwise);

        // 末帧 + 一帧增量应等于下周期首帧(360≡0)
        double frameInc = 360.0 / 180;
        Assert.Equal(first, last + frameInc - 360.0, 3); // 358 + 2 - 360 = 0
        Assert.Equal(first, ((nextCycleStart % 360) + 360) % 360, 3);
    }

    [Fact]
    public void Rot360_首末帧不重合_但末帧衔接下一周期()
    {
        // 无缝循环设计：末帧 Yaw=354(360*59/60)，与首帧0°不同帧(避免停顿)，
        // 但末帧视角与"若再放一帧的首帧"连续(无跳变)
        var first = YawSchedule.YawAt(0, 60, 360, RotationDirection.Clockwise);
        var last = YawSchedule.YawAt(59, 60, 360, RotationDirection.Clockwise);

        Assert.NotEqual(first, last); // 末帧非首帧(避免一帧停顿)
        Assert.Equal(354.0, last, 3); // 360*59/60

        // 末帧 + 帧增量 = 360(≡0=首帧): 循环衔接
        Assert.Equal(0.0, ((last + 360.0 / 60) % 360), 3);
    }

    [Fact]
    public void 非360整数倍_首末帧不同_非无缝()
    {
        // rotation=180: t/(N-1), 末帧=180°, 首帧0°, 首末不同(非无缝循环)
        var first = YawSchedule.YawAt(0, 60, 180, RotationDirection.Clockwise);
        var last = YawSchedule.YawAt(59, 60, 180, RotationDirection.Clockwise);

        Assert.Equal(0.0, first, 3);
        Assert.Equal(180.0, last, 3);
        Assert.NotEqual(first, last);
        Assert.False(YawSchedule.IsSeamlessLoop(180));
    }

    [Fact]
    public void Rot720_无缝循环_末帧衔接下一周期()
    {
        var first = YawSchedule.YawAt(0, 60, 720, RotationDirection.Clockwise);
        var last = YawSchedule.YawAt(59, 60, 720, RotationDirection.Clockwise);

        Assert.True(YawSchedule.IsSeamlessLoop(720));
        // 末帧 720*59/60=708, +帧增量12 = 720≡0
        Assert.Equal(708.0, last, 3);
        Assert.Equal(0.0, ((last + 720.0 / 60) % 720), 3);
    }

    [Theory]
    [InlineData(360)]
    [InlineData(720)]
    [InlineData(1080)]
    public void Rot360整数倍_均判定无缝(int rotation)
    {
        Assert.True(YawSchedule.IsSeamlessLoop(rotation));
    }
}
