using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Validation;
using PanoToVideo.Render.Exporting;

namespace PanoToVideo.Tests.Exporting;

/// <summary>真实 FFmpeg 小尺寸冒烟：确认 CPU 回退不仅停留在命令拼接层。</summary>
public sealed class FfmpegCpuFallbackExecutorTests
{
    [Fact]
    public void Execute_小尺寸ERP_产出可写MP4并标记CPU回退()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"pano-cpu-smoke-{Guid.NewGuid():N}.tmp.mp4");
        var parameters = RenderParameters.Default() with
        {
            DurationSeconds = 1,
            Fps = 24,
            Width = 64,
            Height = 64,
            CpuCores = 1,
        };
        // 2:1 ERP，红/绿两个经度采样点。
        var erp = new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 };
        var executor = new FfmpegCpuFallbackExecutor(erp, 2, 1, parameters, ExportPreset.Compatibility);

        try
        {
            var result = executor.Execute(tmpPath, new ImageInfo(2, 1, false, "smoke.jpg"), parameters, ExportPreset.Compatibility);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(tmpPath));
            Assert.True(new FileInfo(tmpPath).Length > 0);
            Assert.Equal("CPU", result.ProjectionDevice);
            Assert.Equal("libx264", result.EncoderName);
            Assert.True(result.UsedCpuFallback);
        }
        finally
        {
            executor.Cleanup(tmpPath);
        }
    }
}
