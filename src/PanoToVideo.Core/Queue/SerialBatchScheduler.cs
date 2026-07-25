using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Validation;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;

namespace PanoToVideo.Core.Queue;

/// <summary>
/// 串行批量调度器（开发规划阶段2任务3、任务7）。
/// 一次一个 GPU 任务（防显存/编码会话争抢）；
/// 暂停=当前完成后停止启动下一项；取消当前任务=中断编码；
/// 失败单项不阻塞队列；支持重试失败项。
/// </summary>
public class SerialBatchScheduler
{
    private readonly Func<QueueItem, byte[], int, int, IExportExecutor> _executorFactory;
    private readonly Func<QueueItem, (byte[] Rgba, int W, int H)>? _erpLoader;
    // P0-5：IErpLoader 按需解码重载，与旧 Func<> 重载二选一。非 null 时 RunItemAsync 用 using 自动 Dispose。
    private readonly IErpLoader? _erpLoaderInterface;
    private volatile bool _paused;
    // M11: volatile 保证 CancelCurrent 跨线程读到最新值（RunItemAsync 后台线程写，UI 线程读）
    private volatile CancellationTokenSource? _currentTaskCts;

    public bool IsPaused => _paused;

    public SerialBatchScheduler(
        Func<QueueItem, byte[], int, int, IExportExecutor> executorFactory,
        Func<QueueItem, (byte[] Rgba, int W, int H)> erpLoader)
    {
        _executorFactory = executorFactory;
        _erpLoader = erpLoader;
        _erpLoaderInterface = null;
    }

    /// <summary>
    /// P0-5 按需解码重载：传入 IErpLoader 后，RunItemAsync 对每项 Load 一次、
    /// 项结束后 Dispose 一次，避免 100 张批量输入常驻 RGBA 内存。
    /// </summary>
    public SerialBatchScheduler(
        Func<QueueItem, byte[], int, int, IExportExecutor> executorFactory,
        IErpLoader erpLoader)
    {
        _executorFactory = executorFactory;
        _erpLoader = null;
        _erpLoaderInterface = erpLoader;
    }

    /// <summary>暂停队列：当前任务完成后停止启动下一项。</summary>
    public void Pause() => _paused = true;

    /// <summary>恢复队列（继续后续项需再次 RunAsync）。</summary>
    public void Resume() => _paused = false;

    /// <summary>取消当前正在执行的任务（中断编码）。</summary>
    public void CancelCurrent() => _currentTaskCts?.Cancel();

    /// <summary>
    /// 串行执行队列。每项：ERP校验 -> 预检 -> 状态转换 -> 执行（进度上报）-> 完成/失败/取消。
    /// 失败单项不阻塞后续。暂停时当前项完成后停止。
    /// </summary>
    public async Task RunAsync(
        IReadOnlyList<QueueItem> items,
        RenderParameters parameters,
        ExportPreset preset,
        string outputDir,
        long availableBytes,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            // UI 入队校验通过后已是 Pending；只有尚未校验的任务才需要转换。
            // 旧逻辑对 Pending 再次调用 TransitionTo(Pending)，非法转换被吞掉后会永久跳过，
            // 进而让上层等待循环持续看到“等待导出”。
            if (item.Status == TaskStatus.PendingValidation)
                item.TransitionTo(TaskStatus.Pending);
            if (item.Status != TaskStatus.Pending)
                continue; // 已完成/失败/取消项不重复执行

            await RunItemAsync(item, parameters, preset, outputDir, availableBytes, cancellationToken);

            // 取消：当前项已转已取消，停止后续（规划任务7）
            if (cancellationToken.IsCancellationRequested) break;
            // 暂停：当前项完成后停止启动下一项（规划任务7）
            if (_paused) break;
        }
    }

    /// <summary>重试单个失败项（失败态 -> 待处理 -> 执行）。</summary>
    public async Task RetryAsync(
        QueueItem item,
        RenderParameters parameters,
        ExportPreset preset,
        string outputDir,
        long availableBytes,
        CancellationToken cancellationToken)
    {
        // 已取消不可重试（TransitionTo 会抛 InvalidOperationException）
        item.TransitionTo(TaskStatus.Pending);
        await RunItemAsync(item, parameters, preset, outputDir, availableBytes, cancellationToken);
    }

    private async Task RunItemAsync(
        QueueItem item,
        RenderParameters parameters,
        ExportPreset preset,
        string outputDir,
        long availableBytes,
        CancellationToken cancellationToken)
    {
        // 调用方负责确保 item 已在 Pending 状态（RunAsync 从 PendingValidation 转入，RetryAsync 从 Failed 转入）
        // 待处理 -> 处理中
        try { item.TransitionTo(TaskStatus.Processing); }
        catch (InvalidOperationException) { return; } // 非法状态（如已完成）跳过

        _currentTaskCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // P0-5：接口模式下 erp 必须存活到 executor 返回后再释放（rgba 在执行期间被引用），
        // 故提升到 try 外，由 finally 显式 Dispose。
        LoadedErp? erpResource = null;
        try
        {
            // 加载 ERP（App 层解码）。P0-5：接口模式下按需加载，用完即释放，避免批量常驻。
            byte[] rgba;
            int w, h;
            if (_erpLoaderInterface is not null)
            {
                erpResource = _erpLoaderInterface.Load(item);
                rgba = erpResource.Rgba;
                w = erpResource.Width;
                h = erpResource.Height;
            }
            else
            {
                (rgba, w, h) = _erpLoader!(item);
            }
            var imageInfo = new ImageInfo(w, h, false, item.SourceFileName);

            var executor = _executorFactory(item, rgba, w, h);
            var orchestrator = new SingleImageExportOrchestrator();

            // 进度回调：更新队列项进度
            var progress = new ProgressAdapter<ExportProgress>(p =>
                item.UpdateProgress(p.FrameIndex + 1, p.TotalFrames, p.ProjectionFps, p.EncodingFps, p.Elapsed));

            // 收集已有文件用于重名检测
            var existing = Directory.Exists(outputDir)
                ? (IReadOnlyCollection<string>)Directory.GetFiles(outputDir, "*.mp4", SearchOption.AllDirectories)
                    .Select(Path.GetFileName).Where(s => s != null).Cast<string>().ToList()
                : Array.Empty<string>();

            // 不传 cancellationToken 给 Task.Run：预取消时仍执行 action，让 orchestrator 内部
            // executor 收到取消令牌抛异常并清理临时文件（Task.Run(action,token) 会跳过 action）
            var result = await Task.Run(() => orchestrator.Export(
                imageInfo, parameters, preset, outputDir, availableBytes, existing,
                executor, _currentTaskCts.Token, progress));

            if (result.Success)
            {
                item.SetOutput(result.OutputPath!, result.Log!.AverageFps, result.Log.EncoderName, result.Log.UsedCpuFallback);
                item.TransitionTo(TaskStatus.Completed);
            }
            else
            {
                item.SetError(result.Error ?? "未知错误");
                item.TransitionTo(_currentTaskCts.IsCancellationRequested ? TaskStatus.Cancelled : TaskStatus.Failed);
            }
        }
        catch (OperationCanceledException)
        {
            item.SetError("已取消");
            item.TransitionTo(TaskStatus.Cancelled);
        }
        catch (Exception ex)
        {
            item.SetError($"{ex.GetType().Name}: {ex.Message}");
            item.TransitionTo(TaskStatus.Failed);
        }
        finally
        {
            erpResource?.Dispose();
            _currentTaskCts?.Dispose();
            _currentTaskCts = null;
        }
    }

    /// <summary>同步 IProgress 适配器（测试同步断言，避免 Progress&lt;T&gt; 异步派发）。</summary>
    private sealed class ProgressAdapter<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        public ProgressAdapter(Action<T> callback) => _callback = callback;
        public void Report(T value) => _callback(value);
    }
}
