using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Tests.Precheck;

/// <summary>
/// 输出预设退回 TDD 测试（开发规划阶段3任务3、验收门槛）。
/// PresetResolver：H.265 不可用时退回 H.264 并提示。注入 IHevcEncoderProbe 便于单测。
/// </summary>
public class PresetResolverTests
{
    private sealed class FakeHevcProbe : IHevcEncoderProbe
    {
        public bool Available { get; set; }
        public bool IsAvailable() => Available;
    }

    [Fact]
    public void H265可用_返回Size预设_无退回()
    {
        var probe = new FakeHevcProbe { Available = true };
        var sut = new PresetResolver(probe);

        var result = sut.Resolve(ExportPreset.Size);

        Assert.Equal(ExportPreset.Size, result.Preset);
        Assert.Null(result.FallbackReason);
    }

    [Fact]
    public void H265不可用_退回H264_含退回原因()
    {
        var probe = new FakeHevcProbe { Available = false };
        var sut = new PresetResolver(probe);

        var result = sut.Resolve(ExportPreset.Size);

        Assert.Equal(ExportPreset.Compatibility, result.Preset);
        Assert.NotNull(result.FallbackReason);
        Assert.Contains("H.265", result.FallbackReason!);
        Assert.Contains("H.264", result.FallbackReason!);
    }

    [Fact]
    public void H264预设_无论H265可用性_始终H264()
    {
        var probe = new FakeHevcProbe { Available = false };
        var sut = new PresetResolver(probe);

        var result = sut.Resolve(ExportPreset.Compatibility);

        Assert.Equal(ExportPreset.Compatibility, result.Preset);
        Assert.Null(result.FallbackReason); // H.264 不退回
    }

    [Fact]
    public void H265可用_H264预设_不退回()
    {
        var probe = new FakeHevcProbe { Available = true };
        var sut = new PresetResolver(probe);

        var result = sut.Resolve(ExportPreset.Compatibility);

        Assert.Equal(ExportPreset.Compatibility, result.Preset);
        Assert.Null(result.FallbackReason);
    }

    [Fact]
    public void 退回原因_明确说明硬件编码器不支持()
    {
        var probe = new FakeHevcProbe { Available = false };
        var sut = new PresetResolver(probe);

        var result = sut.Resolve(ExportPreset.Size);

        // PRD: 不伪称硬件加速，必须明确说明原因
        Assert.Contains("不支持", result.FallbackReason!);
    }
}
