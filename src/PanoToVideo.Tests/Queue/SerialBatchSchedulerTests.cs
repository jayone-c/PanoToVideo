using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Queue;
using PanoToVideo.Core.Validation;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.Tests.Queue;

/// <summary>
/// 串行批量调度器 TDD 测试（开发规划阶段2任务3、任务7）。
/// 一次一个 GPU 任务；暂停=当前完成后停止下一项；取消当前任务中断编码；
/// 失败单项不阻塞队列；支持重试失败项。
/// </summary>
public class SerialBatchSchedulerTests
{
    private static readonly RenderParameters Params = RenderParameters.Default();
    private const string OutDir = "/out";
    private const long PlentyDisk = 10L * 1024 * 1024 * 1024;

    /// <summary>可控的执行器工厂：按序号决定成功/失败/取消。</summary>
    private sealed class FakeExecutorFactory
    {
        public List<int> ExecutedIndices { get; } = new();
        public Dictionary<int, bool> FailIndices { get; } = new();
        public List<string> CleanedPaths { get; } = new();
        public int CreatedCount { get; private set; }

        public IExportExecutor CreateFor(QueueItem item)
        {
            CreatedCount++;
            return new FakeExecutor(this, item);
        }

        private sealed class FakeExecutor : IExportExecutor
        {
            private readonly FakeExecutorFactory _f;
            private readonly QueueItem _item;
            private int _idx;
            public FakeExecutor(FakeExecutorFactory f, QueueItem item) { _f = f; _item = item; }

            public ExportExecutionResult Execute(
                string tmpPath, ImageInfo imageInfo, RenderParameters parameters, ExportPreset preset,
                CancellationToken cancellationToken = default,
                IProgress<ExportProgress>? progress = null)
            {
                _idx = _f.ExecutedIndices.Count;
                _f.ExecutedIndices.Add(_idx);
                // 取消令牌已取消 -> 抛 OperationCanceledException（走调度器取消分支）
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();
                // 模拟失败
                if (_f.FailIndices.TryGetValue(_idx, out var fail) && fail)
                    return new ExportExecutionResult(false, "执行失败模拟", TimeSpan.Zero, 0);
                // 成功：上报进度
                progress?.Report(new ExportProgress(parameters.TotalFrames - 1, parameters.TotalFrames, 60, 60, TimeSpan.FromSeconds(1)));
                return new ExportExecutionResult(true, null, TimeSpan.FromSeconds(1), 60.0);
            }

            public void AtomicMove(string tmpPath, string finalPath) { }
            public void Cleanup(string tmpPath) => _f.CleanedPaths.Add(tmpPath);
        }
    }

    private static QueueItem MakeItem(int i) => new($"scene{i}_equirectangular_8192x4096.jpg", 8192, 4096);
    private static ImageInfo MakeImage(QueueItem item) => new(8192, 4096, false, item.SourceFileName);

    private sealed class TestableScheduler : SerialBatchScheduler
    {
        private readonly FakeExecutorFactory _factory;
        public TestableScheduler(FakeExecutorFactory factory) : base(
            (item, erpRgba, w, h) => factory.CreateFor(item),
            erpLoader: item => (new byte[8192 * 4096 * 4], 8192, 4096))
        { _factory = factory; }
    }

    [Fact]
    public async Task 多项串行执行_全部完成()
    {
        var factory = new FakeExecutorFactory();
        var scheduler = new TestableScheduler(factory);
        var items = new[] { MakeItem(0), MakeItem(1), MakeItem(2) };

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        Assert.All(items, i => Assert.Equal(TaskStatus.Completed, i.Status));
        Assert.Equal(3, factory.ExecutedIndices.Count);
    }

    [Fact]
    public async Task 已在等待导出状态的任务_应直接开始而非被跳过()
    {
        var factory = new FakeExecutorFactory();
        var scheduler = new TestableScheduler(factory);
        var item = MakeItem(0);
        item.TransitionTo(TaskStatus.Pending); // UI 入队校验通过后的真实状态

        await scheduler.RunAsync([item], Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        Assert.Equal(TaskStatus.Completed, item.Status);
        Assert.Single(factory.ExecutedIndices);
    }

    [Fact]
    public async Task 单项失败_不阻塞队列_后续继续()
    {
        var factory = new FakeExecutorFactory();
        factory.FailIndices[0] = true; // 第一项失败
        var scheduler = new TestableScheduler(factory);
        var items = new[] { MakeItem(0), MakeItem(1), MakeItem(2) };

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        Assert.Equal(TaskStatus.Failed, items[0].Status);
        Assert.NotNull(items[0].ErrorMessage);
        Assert.Equal(TaskStatus.Completed, items[1].Status);
        Assert.Equal(TaskStatus.Completed, items[2].Status);
        Assert.Equal(3, factory.ExecutedIndices.Count); // 三项都执行了
        Assert.NotEmpty(factory.CleanedPaths); // 失败项清理了临时文件
    }

    [Fact]
    public async Task 取消令牌传播_当前任务转已取消_清理临时文件()
    {
        var factory = new FakeExecutorFactory();
        var scheduler = new TestableScheduler(factory);
        var items = new[] { MakeItem(0), MakeItem(1) };
        // 预取消令牌：第0项收到取消异常 -> 已取消，后续不启动
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, cts.Token);

        Assert.Equal(TaskStatus.Cancelled, items[0].Status);
        Assert.Equal(TaskStatus.PendingValidation, items[1].Status); // 后续未启动
        Assert.NotEmpty(factory.CleanedPaths); // 取消项清理临时文件
    }

    [Fact]
    public async Task 暂停队列_当前项完成后停止启动下一项()
    {
        var factory = new FakeExecutorFactory();
        var scheduler = new TestableScheduler(factory);
        var items = new[] { MakeItem(0), MakeItem(1), MakeItem(2) };

        // 在第0项执行时暂停
        scheduler.Pause();
        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        // 暂停时：当前项（第0项）应完成，后续不启动（保持初始 PendingValidation）
        Assert.Equal(TaskStatus.Completed, items[0].Status);
        Assert.Equal(TaskStatus.PendingValidation, items[1].Status);
        Assert.Equal(TaskStatus.PendingValidation, items[2].Status);
    }

    [Fact]
    public async Task 重试失败项_从失败态回待处理再执行()
    {
        var factory = new FakeExecutorFactory();
        factory.FailIndices[0] = true; // 首次失败
        var scheduler = new TestableScheduler(factory);
        var items = new[] { MakeItem(0) };

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);
        Assert.Equal(TaskStatus.Failed, items[0].Status);

        // 重试：失败项回待处理，再次执行成功
        factory.FailIndices.Clear();
        await scheduler.RetryAsync(items[0], Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        Assert.Equal(TaskStatus.Completed, items[0].Status);
    }

    [Fact]
    public async Task 已取消项不可重试()
    {
        var factory = new FakeExecutorFactory();
        var scheduler = new TestableScheduler(factory);
        var item = MakeItem(0);
        item.TransitionTo(TaskStatus.Pending);
        item.TransitionTo(TaskStatus.Cancelled);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scheduler.RetryAsync(item, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default));
    }

    [Fact]
    public async Task 每项进度上报_含投影与编码FPS()
    {
        var factory = new FakeExecutorFactory();
        var scheduler = new TestableScheduler(factory);
        var items = new[] { MakeItem(0) };

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        Assert.True(items[0].Progress.ProjectionFps > 0);
        Assert.True(items[0].Progress.EncodingFps > 0);
        Assert.Equal(Params.TotalFrames, items[0].Progress.TotalFrames);
    }

    /// <summary>可控的 IErpLoader：记录 Load/Dispose 次数与顺序。</summary>
    private sealed class FakeErpLoader : IErpLoader
    {
        public int LoadCount { get; private set; }
        public int DisposeCount { get; private set; }
        public List<QueueItem> LoadedItems { get; } = new();
        public LoadedErp Load(QueueItem item)
        {
            LoadCount++;
            LoadedItems.Add(item);
            return new LoadedErp(new byte[8192 * 4096 * 4], 8192, 4096, release: () => DisposeCount++);
        }
    }

    private sealed class InterfaceScheduler : SerialBatchScheduler
    {
        public InterfaceScheduler(FakeExecutorFactory factory, IErpLoader erpLoader)
            : base((item, erpRgba, w, h) => factory.CreateFor(item), erpLoader) { }
    }

    [Fact]
    public async Task IErpLoader重载_多项串行_每项Load一次Dispose一次()
    {
        var factory = new FakeExecutorFactory();
        var erpLoader = new FakeErpLoader();
        var scheduler = new InterfaceScheduler(factory, erpLoader);
        var items = new[] { MakeItem(0), MakeItem(1), MakeItem(2) };

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        Assert.All(items, i => Assert.Equal(TaskStatus.Completed, i.Status));
        Assert.Equal(3, erpLoader.LoadCount);
        Assert.Equal(3, erpLoader.DisposeCount);
        Assert.Equal(items, erpLoader.LoadedItems);
    }

    [Fact]
    public async Task IErpLoader重载_失败项仍Dispose_不阻塞后续()
    {
        var factory = new FakeExecutorFactory();
        factory.FailIndices[0] = true; // 第一项失败
        var erpLoader = new FakeErpLoader();
        var scheduler = new InterfaceScheduler(factory, erpLoader);
        var items = new[] { MakeItem(0), MakeItem(1) };

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        Assert.Equal(TaskStatus.Failed, items[0].Status);
        Assert.Equal(TaskStatus.Completed, items[1].Status);
        Assert.Equal(2, erpLoader.LoadCount);
        Assert.Equal(2, erpLoader.DisposeCount); // 失败项也释放
    }

    [Fact]
    public async Task IErpLoader重载_取消项仍Dispose()
    {
        var factory = new FakeExecutorFactory();
        var erpLoader = new FakeErpLoader();
        var scheduler = new InterfaceScheduler(factory, erpLoader);
        var items = new[] { MakeItem(0), MakeItem(1) };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, cts.Token);

        Assert.Equal(TaskStatus.Cancelled, items[0].Status);
        Assert.Equal(TaskStatus.PendingValidation, items[1].Status);
        Assert.Equal(1, erpLoader.LoadCount);
        Assert.Equal(1, erpLoader.DisposeCount); // 取消项也释放
    }

    [Fact]
    public async Task IErpLoader重载_暂停时已加载项Dispose()
    {
        var factory = new FakeExecutorFactory();
        var erpLoader = new FakeErpLoader();
        var scheduler = new InterfaceScheduler(factory, erpLoader);
        var items = new[] { MakeItem(0), MakeItem(1) };
        scheduler.Pause();

        await scheduler.RunAsync(items, Params, ExportPreset.Compatibility, OutDir, PlentyDisk, default);

        Assert.Equal(TaskStatus.Completed, items[0].Status);
        Assert.Equal(TaskStatus.PendingValidation, items[1].Status);
        Assert.Equal(1, erpLoader.LoadCount);
        Assert.Equal(1, erpLoader.DisposeCount);
    }
}
