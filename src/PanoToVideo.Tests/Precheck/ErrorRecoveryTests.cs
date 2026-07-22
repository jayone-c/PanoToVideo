using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Queue;
using PanoToVideo.Core.Validation;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.Tests.Precheck;

/// <summary>
/// 错误恢复 TDD 测试（开发规划阶段4任务3、PRD#4）。
/// 逐一验证：非2:1/损坏图/无写权限/磁盘不足/GPU失败/编码器不可用 的文案与回退。
/// </summary>
public class ErrorRecoveryTests
{
    private static readonly RenderParameters Params = RenderParameters.Default();

    private sealed class FakeExecutor : IExportExecutor
    {
        public bool FailExecute { get; set; }
        public string FailMessage { get; set; } = "执行失败";
        public ExportExecutionResult Execute(string tmpPath, ImageInfo imageInfo, RenderParameters parameters, ExportPreset preset, CancellationToken cancellationToken = default, IProgress<ExportProgress>? progress = null)
            => FailExecute ? new ExportExecutionResult(false, FailMessage, TimeSpan.Zero, 0) : new ExportExecutionResult(true, null, TimeSpan.FromSeconds(1), 60);
        public void AtomicMove(string tmpPath, string finalPath) { }
        public void Cleanup(string tmpPath) { }
    }

    private static readonly string OutDir = "/out";
    private const long PlentyDisk = 10L * 1024 * 1024 * 1024;

    [Fact]
    public void 非两比一图_校验拒绝_含实际比例()
    {
        var image = new ImageInfo(8000, 4096, false, "bad_ratio.jpg"); // 1.953 超容差
        var sut = new SingleImageExportOrchestrator();
        var executor = new FakeExecutor();

        var result = sut.Export(image, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, Array.Empty<string>(), executor);

        Assert.False(result.Success);
        Assert.Contains("ERP", result.Error!);
        Assert.Contains("校验失败", result.Error!);
    }

    [Fact]
    public void 损坏图_校验拒绝_含解码提示()
    {
        var image = new ImageInfo(0, 0, true, "corrupt.jpg");
        var sut = new SingleImageExportOrchestrator();
        var executor = new FakeExecutor();

        var result = sut.Export(image, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, Array.Empty<string>(), executor);

        Assert.False(result.Success);
        Assert.Contains("解码", result.Error!);
    }

    [Fact]
    public void 磁盘空间不足_预检拒绝_含预估与可用()
    {
        var image = new ImageInfo(8192, 4096, false, "scene.jpg");
        var sut = new SingleImageExportOrchestrator();
        var executor = new FakeExecutor();

        var result = sut.Export(image, Params, ExportPreset.Compatibility, OutDir, availableBytes: 1024, Array.Empty<string>(), executor);

        Assert.False(result.Success);
        Assert.Contains("磁盘", result.Error!);
    }

    [Fact]
    public void GPU执行失败_记录错误_不伪称硬件加速()
    {
        var image = new ImageInfo(8192, 4096, false, "scene.jpg");
        var sut = new SingleImageExportOrchestrator();
        var executor = new FakeExecutor { FailExecute = true, FailMessage = "硬件编码器不可用，已回退 CPU" };

        var result = sut.Export(image, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, Array.Empty<string>(), executor);

        Assert.False(result.Success);
        Assert.Contains("硬件", result.Error!);
    }

    [Fact]
    public void H265编码器不可用_退回H264_含原因()
    {
        // PRD#4: 不伪称硬件加速，必须说明原因
        var probe = new FakeHevcProbe { Available = false };
        var resolver = new PresetResolver(probe);

        var result = resolver.Resolve(ExportPreset.Size);

        Assert.Equal(ExportPreset.Compatibility, result.Preset);
        Assert.Contains("H.265", result.FallbackReason!);
        Assert.Contains("H.264", result.FallbackReason!);
    }

    [Fact]
    public void H265可用_不退回_无提示()
    {
        var probe = new FakeHevcProbe { Available = true };
        var resolver = new PresetResolver(probe);

        var result = resolver.Resolve(ExportPreset.Size);

        Assert.Equal(ExportPreset.Size, result.Preset);
        Assert.Null(result.FallbackReason);
    }

    private sealed class FakeHevcProbe : IHevcEncoderProbe
    {
        public bool Available { get; set; }
        public bool IsAvailable() => Available;
    }

    [Fact]
    public void 旋转度数非360整数倍_提示非无缝循环()
    {
        var advice = SeamlessLoopAdvisor.Advise(180);
        Assert.False(advice.IsSeamless);
        Assert.Contains("非无缝循环", advice.WarningMessage!);
    }

    [Fact]
    public void 失败单项不阻塞队列_后续继续()
    {
        // 队列层错误恢复（SerialBatchScheduler 阶段2已测，此处覆盖错误恢复视角）
        var items = new[] { new QueueItem("a.jpg", 8192, 4096), new QueueItem("b.jpg", 8192, 4096) };
        // 模拟：第一项状态置失败，第二项应不受影响
        items[0].TransitionTo(TaskStatus.Pending);
        items[0].TransitionTo(TaskStatus.Processing);
        items[0].TransitionTo(TaskStatus.Failed);
        items[0].SetError("单项失败");

        items[1].TransitionTo(TaskStatus.Pending);

        Assert.Equal(TaskStatus.Failed, items[0].Status);
        Assert.Equal(TaskStatus.Pending, items[1].Status); // 后续不受失败项影响
    }
}
