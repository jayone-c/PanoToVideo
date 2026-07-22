using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// 小行星开场调度 TDD 测试（开发规划 §1.5、阶段3任务4）。
/// AsteroidSchedule：0.8秒过渡参数。关闭时第0帧即透视；启用时 t<0.8s 从小行星->透视插值，t≥0.8s 后正常旋转。
/// 第0帧=纯小行星；过渡末(t=0.8)无突跳（连续衔接旋转段）。
/// </summary>
public class AsteroidScheduleTests
{
    private const int Fps = 60;
    private const int TotalFrames = 180; // 3秒@60FPS
    private const double TransitionSeconds = 0.8;
    private const double Epsilon = 1e-6;

    [Fact]
    public void 关闭时_所有帧asteroidWeight为零_即透视投影()
    {
        // 关闭：第0帧即普通透视（weight=0），全程不进入小行星
        Assert.Equal(0.0, AsteroidSchedule.WeightAt(0, Fps, enableAsteroid: false), Epsilon);
        Assert.Equal(0.0, AsteroidSchedule.WeightAt(50, Fps, false), Epsilon);
        Assert.Equal(0.0, AsteroidSchedule.WeightAt(TotalFrames - 1, Fps, false), Epsilon);
    }

    [Fact]
    public void 启用时_第0帧为纯小行星_weight为1()
    {
        Assert.Equal(1.0, AsteroidSchedule.WeightAt(0, Fps, enableAsteroid: true), Epsilon);
    }

    [Fact]
    public void 启用时_过渡末帧接近零_无突跳进入旋转()
    {
        // 0.8s = 48帧@60FPS：第47帧（t<0.8）接近0，第48帧（t≥0.8）正好0
        int transitionEndFrame = (int)(TransitionSeconds * Fps); // 48
        double atEndMinus1 = AsteroidSchedule.WeightAt(transitionEndFrame - 1, Fps, true);
        double atEnd = AsteroidSchedule.WeightAt(transitionEndFrame, Fps, true);

        Assert.True(atEndMinus1 > 0 && atEndMinus1 < 0.05, $"过渡末前帧应接近0，实际{atEndMinus1}");
        Assert.Equal(0.0, atEnd, Epsilon); // 过渡结束正好0
        // 无突跳：末前帧与末帧差距小（<0.05）
        Assert.True(Math.Abs(atEndMinus1 - atEnd) < 0.05);
    }

    [Fact]
    public void 启用时_过渡段单调递减_from1to0()
    {
        double prev = AsteroidSchedule.WeightAt(0, Fps, true);
        Assert.Equal(1.0, prev, Epsilon);

        // 过渡段每帧递减
        for (int f = 1; f < 48; f += 4)
        {
            double cur = AsteroidSchedule.WeightAt(f, Fps, true);
            Assert.True(cur < prev, $"帧{f}应递减，{cur} >= {prev}");
            prev = cur;
        }
    }

    [Fact]
    public void 启用时_过渡结束后全程weight为零_正常旋转()
    {
        for (int f = 48; f < TotalFrames; f += 10)
            Assert.Equal(0.0, AsteroidSchedule.WeightAt(f, Fps, true), Epsilon);
    }

    [Fact]
    public void 启用时_过渡段进度连续_无跳变()
    {
        // 相邻帧 weight 差距应平滑（<0.1，过渡48帧从1到0，每帧约0.02）
        for (int f = 1; f < 48; f++)
        {
            double a = AsteroidSchedule.WeightAt(f - 1, Fps, true);
            double b = AsteroidSchedule.WeightAt(f, Fps, true);
            Assert.True(Math.Abs(b - a) < 0.1, $"帧{f-1}->{f} 跳变 {Math.Abs(b - a)}");
        }
    }

    [Fact]
    public void 过渡时长固定0p8秒()
    {
        Assert.Equal(0.8, AsteroidSchedule.TransitionSeconds, Epsilon);
    }

    [Fact]
    public void 小行星起始Yaw与用户起始Yaw一致_过渡后无水平跳变()
    {
        // 小行星过渡期间 Yaw 应保持起始(0)，过渡后才进入旋转动画
        // 第0帧 Yaw=0，过渡末帧(48) Yaw 应与"若从0帧开始旋转的第48帧"衔接
        double yawAtTransitionEnd = AsteroidSchedule.RotationStartFrame(Fps);
        Assert.Equal(48, yawAtTransitionEnd); // 旋转从第48帧开始
    }
}
