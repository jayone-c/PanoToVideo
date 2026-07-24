using PanoToVideo.Core.Validation;

namespace PanoToVideo.Tests.Validation;

/// <summary>
/// ERP 输入校验的 TDD 测试（红阶段先行）。
/// 契约来源：PRD「输入与校验」+ 开发规划 §8 输入契约 + §0 差异。
/// 规则：宽高比 2.0 ± 1%；宽在 [6000, 16384]；损坏文件优先拒绝。
/// </summary>
public class EquirectValidatorTests
{
    private readonly EquirectValidator _sut = new();

    [Fact]
    public void Validate_标准8192x4096_通过()
    {
        var info = new ImageInfo(8192, 4096, IsCorrupt: false, SourcePath: "a.jpg");

        var result = _sut.Validate(info);

        Assert.True(result.IsValid);
        Assert.Equal(8192, result.Width);
        Assert.Equal(4096, result.Height);
        Assert.Equal(2.0, result.Ratio, 3);
    }

    [Fact]
    public void Validate_6000x3000_通过()
    {
        var info = new ImageInfo(6000, 3000, IsCorrupt: false, SourcePath: "b.jpg");

        var result = _sut.Validate(info);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_16000x8000_通过()
    {
        var info = new ImageInfo(16000, 8000, IsCorrupt: false, SourcePath: "c.jpg");

        var result = _sut.Validate(info);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_比例超上限2p048_拒绝并含实际比例()
    {
        var info = new ImageInfo(8192, 4000, IsCorrupt: false, SourcePath: "d.jpg");

        var result = _sut.Validate(info);

        Assert.False(result.IsValid);
        Assert.Contains(result.Ratio.ToString("F3"), result.Reason);
    }

    [Fact]
    public void Validate_比例超下限1p953_拒绝()
    {
        var info = new ImageInfo(8000, 4096, IsCorrupt: false, SourcePath: "e.jpg");

        var result = _sut.Validate(info);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_比例在容差下界附近_通过()
    {
        // 下界 1.98；1.98 * 4096 = 8110.08，故最小可通过的宽为 8111（8111/4096 = 1.9802）
        var info = new ImageInfo(8111, 4096, IsCorrupt: false, SourcePath: "f.jpg");

        var result = _sut.Validate(info);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_比例在容差上界附近_通过()
    {
        // 2.02 * 4096 = 8273.92；取 8273/4096 = 2.0198，落在 [1.98, 2.02]
        var info = new ImageInfo(8273, 4096, IsCorrupt: false, SourcePath: "g.jpg");

        var result = _sut.Validate(info);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_低于5000x2500_拒绝并含尺寸范围()
    {
        var info = new ImageInfo(4096, 2048, IsCorrupt: false, SourcePath: "h.jpg");

        var result = _sut.Validate(info);

        Assert.False(result.IsValid);
        Assert.Contains("5000x2500", result.Reason);
    }

    [Fact]
    public void Validate_宽大于16384_拒绝并含尺寸范围()
    {
        var info = new ImageInfo(20000, 10000, IsCorrupt: false, SourcePath: "i.jpg");

        var result = _sut.Validate(info);

        Assert.False(result.IsValid);
        Assert.Contains("16384", result.Reason);
    }

    [Fact]
    public void Validate_损坏文件_优先拒绝并提示无法解码()
    {
        var info = new ImageInfo(0, 0, IsCorrupt: true, SourcePath: "corrupt.jpg");

        var result = _sut.Validate(info);

        Assert.False(result.IsValid);
        Assert.Contains("解码", result.Reason);
    }
}
