using PanoToVideo.Render.Exporting;

namespace PanoToVideo.Tests.Exporting;

public class FfmpegAvailabilityTests
{
    [Fact]
    public void Missing_返回明确的安装与PATH指引()
    {
        var availability = FfmpegAvailability.Missing("系统 PATH 中未找到 ffmpeg.exe。");

        Assert.False(availability.IsAvailable);
        Assert.Contains("安装 FFmpeg", availability.UserMessage);
        Assert.Contains("ffmpeg.exe", availability.UserMessage);
        Assert.Contains("PATH", availability.UserMessage);
    }
}
