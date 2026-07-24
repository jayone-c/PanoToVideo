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
        Assert.Equal(Environment.ProcessorCount, p.CpuCores);
        Assert.Equal(0.0, p.StartYaw);
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

    [Theory]
    [InlineData(-1.0)]
    [InlineData(360.0)]
    public void Validate_起始方位越界_无效(double startYaw)
    {
        var p = RenderParameters.Default() with { StartYaw = startYaw };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("起始方位"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(359.0)]
    public void Validate_起始方位边界_有效(double startYaw)
    {
        var p = RenderParameters.Default() with { StartYaw = startYaw };
        Assert.True(_sut.Validate(p).IsValid);
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

    [Fact]
    public void Validate_CpuCores默认值_有效()
    {
        var p = RenderParameters.Default();
        var result = _sut.Validate(p);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_CpuCores非正_无效(int cores)
    {
        var p = RenderParameters.Default() with { CpuCores = cores };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("CPU 核心数"));
    }

    [Fact]
    public void Validate_CpuCores超出上限_无效()
    {
        var p = RenderParameters.Default() with { CpuCores = Environment.ProcessorCount + 1 };
        var result = _sut.Validate(p);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("CPU 核心数"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Validate_CpuCores边界与有效值_有效(int cores)
    {
        // 仅当本机核心数 >= cores 时有效；跳过核心数不足的环境
        if (cores > Environment.ProcessorCount)
            return;

        var p = RenderParameters.Default() with { CpuCores = cores };
        var result = _sut.Validate(p);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_显式maxCores重载_按给定上限校验()
    {
        var p = RenderParameters.Default() with { CpuCores = 4 };
        // maxCores=2 时 4 越界
        var result = _sut.Validate(p, maxCores: 2);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("[1, 2]"));

        // maxCores=8 时 4 有效
        var result2 = _sut.Validate(p, maxCores: 8);
        Assert.True(result2.IsValid);
    }
}
