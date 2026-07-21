using PanoToVideo.Core.Parameters;

namespace PanoToVideo.Tests.Parameters;

/// <summary>
/// 单图参数校验的 TDD 测试（红阶段先行）。
/// 契约来源：PRD 单图参数表 + 开发规划 §1.4 FOV 语义修正（水平 FOV，垂直由宽高比推导）。
/// </summary>
public class RenderParametersTests
{
    private readonly RenderParametersValidator _sut = new();

    [Fact]
    public void Default_默认值正确()
    {
        var p = RenderParameters.Default();

        Assert.Equal(30, p.DurationSeconds);
        Assert.Equal(360, p.RotationDegrees);
        Assert.Equal(60, p.Fps);
        Assert.Equal(75.0, p.HorizontalFov);
        Assert.Equal(1080, p.Width);
        Assert.Equal(1920, p.Height);
        Assert.Equal(0.0, p.Pitch);
        Assert.Equal(RotationDirection.Clockwise, p.Direction);
        Assert.False(p.AsteroidIntro);
    }

    [Fact]
    public void Validate_默认参数_有效()
    {
        var result = _sut.Validate(RenderParameters.Default());
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_视频长度非正_无效(int duration)
    {
        var p = RenderParameters.Default() with { DurationSeconds = duration };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("长度"));
    }

    [Fact]
    public void Validate_旋转度数为0_无效()
    {
        var p = RenderParameters.Default() with { RotationDegrees = 0 };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("旋转"));
    }

    [Fact]
    public void Validate_旋转度数大于360_有效()
    {
        var p = RenderParameters.Default() with { RotationDegrees = 720 };
        var result = _sut.Validate(p);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(15)]
    public void Validate_帧速率不在集合_无效(int fps)
    {
        var p = RenderParameters.Default() with { Fps = fps };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("帧速率"));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(30)]
    [InlineData(50)]
    [InlineData(60)]
    public void Validate_帧速率在集合_有效(int fps)
    {
        var p = RenderParameters.Default() with { Fps = fps };
        var result = _sut.Validate(p);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(29.9)]
    [InlineData(110.1)]
    public void Validate_FOV越界_无效(double fov)
    {
        var p = RenderParameters.Default() with { HorizontalFov = fov };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("FOV"));
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(110.0)]
    public void Validate_FOV边界_有效(double fov)
    {
        var p = RenderParameters.Default() with { HorizontalFov = fov };
        var result = _sut.Validate(p);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(1081, 1920)]
    [InlineData(1080, 1921)]
    public void Validate_宽高奇数_无效(int w, int h)
    {
        var p = RenderParameters.Default() with { Width = w, Height = h };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("偶数"));
    }

    [Theory]
    [InlineData(-86)]
    [InlineData(86)]
    public void Validate_俯仰角越界_无效(double pitch)
    {
        var p = RenderParameters.Default() with { Pitch = pitch };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("俯仰角"));
    }

    [Theory]
    [InlineData(-85)]
    [InlineData(85)]
    public void Validate_俯仰角边界_有效(double pitch)
    {
        var p = RenderParameters.Default() with { Pitch = pitch };
        var result = _sut.Validate(p);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_多个错误_全部收集()
    {
        var p = RenderParameters.Default() with
        {
            DurationSeconds = 0,
            Fps = 15,
            Width = 1081,
        };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }
}
