using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Validation;

namespace PanoToVideo.Tests.Exporting;

public class FallbackExportExecutorTests
{
    private sealed class FakeExecutor(ExportExecutionResult result) : IExportExecutor
    {
        public int ExecuteCount { get; private set; }
        public int CleanupCount { get; private set; }
        public ExportExecutionResult Execute(string tmpPath, ImageInfo imageInfo, RenderParameters parameters, ExportPreset preset, CancellationToken cancellationToken = default, IProgress<ExportProgress>? progress = null)
        {
            ExecuteCount++;
            return result;
        }
        public void AtomicMove(string tmpPath, string finalPath) { }
        public void Cleanup(string tmpPath) => CleanupCount++;
    }

    [Fact]
    public void 硬件导出失败_未取消时_自动改走CPU并成功()
    {
        var gpu = new FakeExecutor(new ExportExecutionResult(false, "NVENC 初始化失败", TimeSpan.FromSeconds(1), 0));
        var cpu = new FakeExecutor(new ExportExecutionResult(true, null, TimeSpan.FromSeconds(3), 42)
        {
            ProjectionDevice = "CPU", EncoderName = "libx264", UsedCpuFallback = true,
        });
        var sut = new FallbackExportExecutor(gpu, cpu);

        var result = sut.Execute("out.tmp.mp4", new ImageInfo(8000, 4000, false, "pano.jpg"), RenderParameters.Default(), ExportPreset.Compatibility);

        Assert.True(result.Success);
        Assert.Equal(1, gpu.ExecuteCount);
        Assert.Equal(1, gpu.CleanupCount);
        Assert.Equal(1, cpu.ExecuteCount);
        Assert.True(result.UsedCpuFallback);
        Assert.Equal("libx264（硬件编码失败后回退）", result.EncoderName);
        Assert.Equal(TimeSpan.FromSeconds(4), result.Elapsed);
    }

    [Fact]
    public void 用户取消后_不触发CPU回退()
    {
        var gpu = new FakeExecutor(new ExportExecutionResult(false, "已取消", TimeSpan.Zero, 0));
        var cpu = new FakeExecutor(new ExportExecutionResult(true, null, TimeSpan.Zero, 0));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = new FallbackExportExecutor(gpu, cpu).Execute("out.tmp.mp4", new ImageInfo(8000, 4000, false, "pano.jpg"), RenderParameters.Default(), ExportPreset.Compatibility, cts.Token);

        Assert.False(result.Success);
        Assert.Equal(0, cpu.ExecuteCount);
        Assert.Equal(0, gpu.CleanupCount);
    }
}
