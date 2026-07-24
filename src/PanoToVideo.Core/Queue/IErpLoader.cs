namespace PanoToVideo.Core.Queue;

/// <summary>
/// 已加载的 ERP 资源（P0-5：按需解码，用完即释放）。
/// 实现 IDisposable：调度器在每项完成/失败/取消后自动 Dispose，
/// 100 张批量输入不再常驻 RGBA 内存。
/// </summary>
public sealed class LoadedErp : IDisposable
{
    public byte[] Rgba { get; }
    public int Width { get; }
    public int Height { get; }
    private readonly Action? _release;
    private bool _disposed;

    public LoadedErp(byte[] rgba, int width, int height, Action? release = null)
    {
        Rgba = rgba;
        Width = width;
        Height = height;
        _release = release;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _release?.Invoke();
    }
}

/// <summary>
/// ERP 按需加载器抽象（P0-5：开发规划 §阶段2任务4）。
/// Core 仅定义契约，IO 实现放 App（每项按需解码、用完释放）。
/// 调度器对每项 Load 一次、项结束后 Dispose 一次。
/// </summary>
public interface IErpLoader
{
    /// <summary>加载队列项对应的 ERP 为 RGBA（调用方负责 Dispose 返回值）。</summary>
    LoadedErp Load(QueueItem item);
}
