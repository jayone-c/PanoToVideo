using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Tests.Precheck;

/// <summary>
/// 无缝循环提示 TDD 测试（开发规划阶段3任务5、PRD #20）。
/// 旋转度数非 360° 整数倍时提示"非无缝循环"。
/// </summary>
public class SeamlessLoopAdvisorTests
{
    [Theory]
    [InlineData(360)]
    [InlineData(720)]
    [InlineData(1080)]
    public void Rot360整数倍_无缝_无提示(int rotation)
    {
        var result = SeamlessLoopAdvisor.Advise(rotation);
        Assert.True(result.IsSeamless);
        Assert.Null(result.WarningMessage);
    }

    [Theory]
    [InlineData(180)]
    [InlineData(400)]
    [InlineData(540)]
    [InlineData(90)]
    public void 非360整数倍_非无缝_含提示(int rotation)
    {
        var result = SeamlessLoopAdvisor.Advise(rotation);
        Assert.False(result.IsSeamless);
        Assert.NotNull(result.WarningMessage);
        Assert.Contains("无缝", result.WarningMessage!);
    }

    [Fact]
    public void 提示文案_明确说明非无缝循环()
    {
        var result = SeamlessLoopAdvisor.Advise(180);
        Assert.Contains("非无缝循环", result.WarningMessage!);
    }

    [Fact]
    public void 旋转度数为0_非无缝_提示()
    {
        // 0度：无旋转，不应判为无缝
        var result = SeamlessLoopAdvisor.Advise(0);
        Assert.False(result.IsSeamless);
        Assert.NotNull(result.WarningMessage);
    }

    [Fact]
    public void 提示包含实际旋转度数_便于用户理解()
    {
        var result = SeamlessLoopAdvisor.Advise(400);
        Assert.Contains("400", result.WarningMessage!);
    }
}
