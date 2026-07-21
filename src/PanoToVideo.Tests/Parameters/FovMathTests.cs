using PanoToVideo.Core.Parameters;

namespace PanoToVideo.Tests.Parameters;

/// <summary>
/// FOV 数学 TDD 测试。
/// 契约：水平 FOV 与输出宽高比推导垂直 FOV（开发规划 §1.4）。
/// 推导：tan(vFov/2) = tan(hFov/2) × (height/width)
/// </summary>
public class FovMathTests
{
    [Fact]
    public void VerticalFov_竖屏垂直FOV大于水平()
    {
        // 1080×1920 竖屏，aspect=1920/1080≈1.778
        var v = FovMath.VerticalFov(75.0, 1080, 1920);
        Assert.True(v > 75.0, "竖屏垂直 FOV 应大于水平 FOV");
        Assert.True(v < 180.0);
    }

    [Fact]
    public void VerticalFov_横屏垂直FOV小于水平()
    {
        // 1920×1080 横屏，aspect=1080/1920=0.5625
        var v = FovMath.VerticalFov(75.0, 1920, 1080);
        Assert.True(v < 75.0, "横屏垂直 FOV 应小于水平 FOV");
    }

    [Fact]
    public void VerticalFov_正方形输出等于水平()
    {
        var v = FovMath.VerticalFov(75.0, 1080, 1080);
        Assert.Equal(75.0, v, 3);
    }

    [Fact]
    public void VerticalFov_横屏具体值匹配推导公式()
    {
        // hFov=75, 1920×1080, aspect=0.5625
        // tan(37.5°)≈0.767327 × 0.5625 ≈0.431622 → atan≈23.3416° → ×2≈46.683°
        var v = FovMath.VerticalFov(75.0, 1920, 1080);
        Assert.Equal(46.68, v, 1);
    }
}
