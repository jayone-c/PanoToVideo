using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Tests.Exporting;

/// <summary>
/// 回退决策纯逻辑 TDD 测试（开发规划 §7、PRD #5）。
/// 注入 fake GpuAvailability 覆盖 4 分支，脱离真实 GPU。
/// </summary>
public class FallbackDecisionTests
{
    private static GpuAvailability Gpu(bool hasGpu = true, bool h264 = true, bool hevc = true,
        string? desc = "RTX 4090 D", string? reason = null) =>
        new(hasGpu, h264, hevc, desc, reason);

    [Fact]
    public void 无GPU_回退CPU_libx264()
    {
        var d = FallbackDecider.Decide(Gpu(hasGpu: false), ExportPreset.Compatibility);

        Assert.Equal(ExportBackend.CpuFallback, d.Backend);
        Assert.Equal("CPU", d.ProjectionDeviceLabel);
        Assert.Equal("libx264", d.EncoderLabel);
        Assert.True(d.UsedCpuFallback);
        Assert.NotNull(d.Reason);
    }

    [Fact]
    public void 有GPU但无H264硬件编码器_回退CPU()
    {
        var d = FallbackDecider.Decide(Gpu(h264: false), ExportPreset.Compatibility);

        Assert.Equal(ExportBackend.CpuFallback, d.Backend);
        Assert.True(d.UsedCpuFallback);
        Assert.Equal("libx264", d.EncoderLabel);
        Assert.Contains("H.264", d.Reason ?? "");
    }

    [Fact]
    public void 无GPU_带原因透传()
    {
        var d = FallbackDecider.Decide(
            new GpuAvailability(false, false, false, null, "GPU 初始化失败：DXGI 枚举为空"),
            ExportPreset.Compatibility);

        Assert.Equal(ExportBackend.CpuFallback, d.Backend);
        Assert.Equal("GPU 初始化失败：DXGI 枚举为空", d.Reason);
    }

    [Fact]
    public void GPU可用_兼容预设_H264NVENC()
    {
        var d = FallbackDecider.Decide(Gpu(), ExportPreset.Compatibility);

        Assert.Equal(ExportBackend.GpuNvenc, d.Backend);
        Assert.Equal("RTX 4090 D", d.ProjectionDeviceLabel);
        Assert.Equal("H.264 NVENC", d.EncoderLabel);
        Assert.False(d.UsedCpuFallback);
        Assert.Null(d.Reason);
    }

    [Fact]
    public void GPU可用_体积预设_已解析为H265_H265NVENC()
    {
        // PresetResolver 已确保 Size 仅在 HEVC 可用时保留
        var d = FallbackDecider.Decide(Gpu(hevc: true), ExportPreset.Size);

        Assert.Equal(ExportBackend.GpuNvenc, d.Backend);
        Assert.Equal("H.265 NVENC", d.EncoderLabel);
        Assert.False(d.UsedCpuFallback);
    }

    [Fact]
    public void GPU可用_体积预设_HEVC不可用_PresetResolver已退回H264()
    {
        // PresetResolver 探测 HEVC 不可用 -> 退回 Compatibility，FallbackDecision 看到的是 Compatibility
        var d = FallbackDecider.Decide(Gpu(hevc: false), ExportPreset.Compatibility);

        Assert.Equal(ExportBackend.GpuNvenc, d.Backend);
        Assert.Equal("H.264 NVENC", d.EncoderLabel); // 非 H.265
    }

    [Fact]
    public void GPU可用_设备描述缺失_兜底GPU标签()
    {
        var d = FallbackDecider.Decide(Gpu(desc: null), ExportPreset.Compatibility);

        Assert.Equal("GPU", d.ProjectionDeviceLabel);
    }
}
