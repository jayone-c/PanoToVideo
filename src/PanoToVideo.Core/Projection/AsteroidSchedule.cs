namespace PanoToVideo.Core.Projection;

/// <summary>
/// 小行星开场调度（开发规划 §1.5、阶段3任务4）。
/// 启用时前 0.8 秒（固定）从小行星投影过渡到用户透视；之后正常旋转。
/// 关闭时第 0 帧即透视。
/// 小行星是独立球面投影（底部极点向下俯视），非把透视 FOV 设为 180°（避免奇点）。
/// </summary>
public static class AsteroidSchedule
{
    /// <summary>过渡时长固定 0.8 秒（PRD #4）。</summary>
    public const double TransitionSeconds = 0.8;

    /// <summary>
    /// 第 frame 帧的小行星权重 [0,1]。
    /// 关闭时恒 0（即透视）。启用时：第0帧=1（纯小行星），过渡段 smoothstep 递减到0，
    /// 过渡结束(t>=0.8s)后恒0（正常旋转）。smoothstep 保证首尾导数连续，无突跳。
    /// </summary>
    public static double WeightAt(int frame, int fps, bool enableAsteroid)
    {
        if (!enableAsteroid)
            return 0.0;

        int transitionFrames = (int)(TransitionSeconds * fps);
        if (frame >= transitionFrames)
            return 0.0;
        if (frame <= 0)
            return 1.0;

        // 归一化进度 t∈[0,1]：smoothstep 平滑插值
        double t = (double)frame / transitionFrames;
        return 1.0 - Smoothstep(t);
    }

    /// <summary>旋转动画起始帧：小行星启用时从第 0.8s 帧开始（过渡期间不旋转）。
    /// 关闭时从第0帧开始。</summary>
    public static int RotationStartFrame(int fps, bool enableAsteroid)
    {
        if (!enableAsteroid)
            return 0;
        return (int)(TransitionSeconds * fps);
    }

    /// <summary>重载：默认启用（测试便利）。</summary>
    public static int RotationStartFrame(int fps) => RotationStartFrame(fps, enableAsteroid: true);

    /// <summary>smoothstep：t∈[0,1] -> [0,1]，首尾导数为0，保证过渡衔接无突跳。</summary>
    private static double Smoothstep(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}
