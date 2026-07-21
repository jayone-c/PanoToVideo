using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Tests.Precheck;

/// <summary>
/// 导出预检 TDD 测试。
/// 契约：开发规划 §阶段2任务5、§8。
/// 码率基准 1080×1920@60：H.264 16Mbps / H.265 8Mbps；其他按像素×帧率换算；预估 ×1.15。
/// </summary>
public class ExportPrecheckTests
{
    [Fact]
    public void 总帧数_时长乘FPS()
    {
        Assert.Equal(1800, ExportPrecheck.TotalFrames(30, 60));
        Assert.Equal(300, ExportPrecheck.TotalFrames(10, 30));
    }

    [Fact]
    public void 码率_基准尺寸返回基准值()
    {
        Assert.Equal(16_000_000.0, ExportPrecheck.EstimateBitrate(ExportPreset.Compatibility, 1080, 1920, 60));
        Assert.Equal(8_000_000.0, ExportPrecheck.EstimateBitrate(ExportPreset.Size, 1080, 1920, 60));
    }

    [Fact]
    public void 码率_非基准尺寸按像素帧率换算()
    {
        // 1080×1080@30 vs 1080×1920@60: (1080*1080*30)/(1080*1920*60) = 0.28125
        var rate = ExportPrecheck.EstimateBitrate(ExportPreset.Compatibility, 1080, 1080, 30);
        Assert.Equal(16_000_000.0 * 0.28125, rate, 1);
    }

    [Fact]
    public void 预估体积_码率乘时长乘1p15除8()
    {
        var bytes = ExportPrecheck.EstimateBytes(ExportPreset.Compatibility, 1080, 1920, 60, 30);
        // 16e6 bps * 30s * 1.15 / 8 = 69_000_000
        Assert.Equal(69_000_000L, bytes);
    }

    [Fact]
    public void 检查_空间充足_通过并返回预估()
    {
        var est = ExportPrecheck.EstimateBytes(ExportPreset.Compatibility, 1080, 1920, 60, 30);

        var result = ExportPrecheck.Check(ExportPreset.Compatibility, 1080, 1920, 60, 30, est * 2);

        Assert.True(result.CanProceed);
        Assert.Equal(est, result.EstimatedBytes);
        Assert.Equal(1800, result.TotalFrames);
    }

    [Fact]
    public void 检查_空间不足_拒绝并含预估与可用()
    {
        var est = ExportPrecheck.EstimateBytes(ExportPreset.Compatibility, 1080, 1920, 60, 30);

        var result = ExportPrecheck.Check(ExportPreset.Compatibility, 1080, 1920, 60, 30, est / 2);

        Assert.False(result.CanProceed);
        Assert.Contains(est.ToString(), result.Reason);
        Assert.Contains((est / 2).ToString(), result.Reason);
    }
}
