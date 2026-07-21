using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Validation;

namespace PanoToVideo.Tests.Exporting;

/// <summary>
/// 单图导出编排 TDD 测试。
/// 用 fake IExportExecutor 验证纯编排逻辑：校验->预检->命名->临时文件->重命名/清理。
/// </summary>
public class SingleImageExportOrchestratorTests
{
    private static readonly RenderParameters Params = RenderParameters.Default();
    private const string OutDir = "/out";
    private const long PlentyDisk = 10L * 1024 * 1024 * 1024;

    private sealed class FakeExecutor : IExportExecutor
    {
        public bool ExecuteShouldFail { get; set; }
        public bool AtomicMoveShouldFail { get; set; }
        public string? LastTmpPath { get; private set; }
        public string? LastFinalPath { get; private set; }
        public bool CleanedUp { get; private set; }
        public bool Executed { get; private set; }
        public CancellationToken? ReceivedToken { get; private set; }
        public bool ProgressReported { get; private set; }

        public ExportExecutionResult Execute(
            string tmpPath, ImageInfo imageInfo, RenderParameters parameters, ExportPreset preset,
            CancellationToken cancellationToken = default,
            IProgress<ExportProgress>? progress = null)
        {
            Executed = true;
            LastTmpPath = tmpPath;
            ReceivedToken = cancellationToken;
            progress?.Report(new ExportProgress(0, parameters.TotalFrames, 60, 60, TimeSpan.Zero));
            ProgressReported = progress != null;
            return ExecuteShouldFail
                ? new ExportExecutionResult(false, "执行失败模拟", TimeSpan.Zero, 0)
                : new ExportExecutionResult(true, null, TimeSpan.FromSeconds(1), 60.0);
        }

        public void AtomicMove(string tmpPath, string finalPath)
        {
            LastFinalPath = finalPath;
            if (AtomicMoveShouldFail) throw new IOException("重命名失败模拟");
        }

        public void Cleanup(string tmpPath) => CleanedUp = true;
    }

    private static ImageInfo ValidImage() => new(8192, 4096, false, "scene_equirectangular_8192x4096.jpg");

    [Fact]
    public void 成功_临时文件重命名为最终路径()
    {
        var executor = new FakeExecutor();
        var sut = new SingleImageExportOrchestrator();

        var result = sut.Export(ValidImage(), Params, ExportPreset.Compatibility,
            OutDir, PlentyDisk, Array.Empty<string>(), executor);

        Assert.True(result.Success);
        Assert.EndsWith(".mp4", result.OutputPath);
        Assert.Contains("exports", result.OutputPath);
        Assert.Contains("scene_equirectangular_8192x4096_1080x1920_30s_360deg.mp4", result.OutputPath!);
        Assert.True(executor.Executed);
        Assert.False(executor.CleanedUp); // 成功不清理
        Assert.NotNull(result.Log);
        Assert.False(result.Log!.UsedCpuFallback);
    }

    [Fact]
    public void 临时文件名含guid与tmp后缀()
    {
        var executor = new FakeExecutor();
        var sut = new SingleImageExportOrchestrator();

        sut.Export(ValidImage(), Params, ExportPreset.Compatibility,
            OutDir, PlentyDisk, Array.Empty<string>(), executor);

        Assert.NotNull(executor.LastTmpPath);
        Assert.Contains(".tmp.mp4", executor.LastTmpPath!);
        // 含 32 位 guid（N 格式）
        Assert.Matches(@".*\.[0-9a-f]{32}\.tmp\.mp4", executor.LastTmpPath!);
    }

    [Fact]
    public void ERP校验失败_不执行导出()
    {
        var executor = new FakeExecutor();
        var sut = new SingleImageExportOrchestrator();
        var badImage = new ImageInfo(8000, 4096, false, "bad.jpg"); // 比例 1.953 超容差

        var result = sut.Export(badImage, Params, ExportPreset.Compatibility,
            OutDir, PlentyDisk, Array.Empty<string>(), executor);

        Assert.False(result.Success);
        Assert.Contains("ERP", result.Error);
        Assert.False(executor.Executed); // 校验失败不执行
    }

    [Fact]
    public void 磁盘空间不足_预检拒绝不执行()
    {
        var executor = new FakeExecutor();
        var sut = new SingleImageExportOrchestrator();

        var result = sut.Export(ValidImage(), Params, ExportPreset.Compatibility,
            OutDir, availableBytes: 1024, Array.Empty<string>(), executor); // 仅 1KB

        Assert.False(result.Success);
        Assert.Contains("磁盘", result.Error);
        Assert.False(executor.Executed);
    }

    [Fact]
    public void 执行失败_清理临时文件不删源图()
    {
        var executor = new FakeExecutor { ExecuteShouldFail = true };
        var sut = new SingleImageExportOrchestrator();

        var result = sut.Export(ValidImage(), Params, ExportPreset.Compatibility,
            OutDir, PlentyDisk, Array.Empty<string>(), executor);

        Assert.False(result.Success);
        Assert.True(executor.CleanedUp); // 执行失败清理临时文件
    }

    [Fact]
    public void 原子重命名失败_清理临时文件()
    {
        var executor = new FakeExecutor { AtomicMoveShouldFail = true };
        var sut = new SingleImageExportOrchestrator();

        var result = sut.Export(ValidImage(), Params, ExportPreset.Compatibility,
            OutDir, PlentyDisk, Array.Empty<string>(), executor);

        Assert.False(result.Success);
        Assert.Contains("原子重命名", result.Error);
        Assert.True(executor.CleanedUp);
    }

    [Fact]
    public void 存在重名_追加递增序号不覆盖()
    {
        var executor = new FakeExecutor();
        var sut = new SingleImageExportOrchestrator();
        var existing = new[] { "scene_equirectangular_8192x4096_1080x1920_30s_360deg.mp4" };

        var result = sut.Export(ValidImage(), Params, ExportPreset.Compatibility,
            OutDir, PlentyDisk, existing, executor);

        Assert.True(result.Success);
        Assert.Contains("_1.mp4", result.OutputPath!);
    }

    [Fact]
    public void 取消令牌透传到执行器()
    {
        var executor = new FakeExecutor();
        var sut = new SingleImageExportOrchestrator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        sut.Export(ValidImage(), Params, ExportPreset.Compatibility,
            OutDir, PlentyDisk, Array.Empty<string>(), executor, cts.Token);

        Assert.True(executor.ReceivedToken?.IsCancellationRequested);
    }

    [Fact]
    public void 进度回调透传到执行器()
    {
        var executor = new FakeExecutor();
        var sut = new SingleImageExportOrchestrator();
        var progresses = new List<ExportProgress>();
        // 用同步 IProgress 实现（Progress<T> 是异步派发，测试同步断言会漏）
        IProgress<ExportProgress> progress = new SyncProgress<ExportProgress>(p => progresses.Add(p));

        sut.Export(ValidImage(), Params, ExportPreset.Compatibility,
            OutDir, PlentyDisk, Array.Empty<string>(), executor, default, progress);

        Assert.True(executor.ProgressReported);
        Assert.NotEmpty(progresses);
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        public SyncProgress(Action<T> callback) => _callback = callback;
        public void Report(T value) => _callback(value);
    }
}
