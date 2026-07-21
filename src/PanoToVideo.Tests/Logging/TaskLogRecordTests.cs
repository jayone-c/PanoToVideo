using PanoToVideo.Core.Logging;
using PanoToVideo.Core.Parameters;

namespace PanoToVideo.Tests.Logging;

/// <summary>
/// 任务日志模型 TDD 测试。
/// 契约：PRD 输出、开发规划 §阶段2任务8。CPU 回退必须显式标注。
/// </summary>
public class TaskLogRecordTests
{
    [Fact]
    public void GPU成功记录_设备编码器齐全且非回退()
    {
        var rec = new TaskLogRecord(
            RenderParameters.Default(),
            ProjectionDevice: "RTX 4090 D",
            EncoderName: "H.264 NVENC",
            UsedCpuFallback: false,
            Elapsed: TimeSpan.FromSeconds(28.5),
            AverageFps: 63.2,
            OutputPath: "/out/exports/a.mp4",
            ErrorMessage: null);

        Assert.False(rec.UsedCpuFallback);
        Assert.Null(rec.ErrorMessage);
        Assert.Equal("RTX 4090 D", rec.ProjectionDevice);
        Assert.Equal("H.264 NVENC", rec.EncoderName);
    }

    [Fact]
    public void CPU回退记录_标记为真并含错误详情()
    {
        var rec = new TaskLogRecord(
            RenderParameters.Default(),
            ProjectionDevice: "CPU",
            EncoderName: "libx264",
            UsedCpuFallback: true,
            Elapsed: TimeSpan.FromSeconds(300),
            AverageFps: 6.0,
            OutputPath: "/out/exports/a.mp4",
            ErrorMessage: "硬件编码器不可用，已回退 CPU");

        Assert.True(rec.UsedCpuFallback);
        Assert.NotNull(rec.ErrorMessage);
        Assert.Contains("硬件", rec.ErrorMessage);
    }
}
