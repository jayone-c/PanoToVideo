using PanoToVideo.Core.Devices;

namespace PanoToVideo.Render.DeviceProbe;

/// <summary>
/// 设备探测缓存（阶段4优化1）。
/// 单例缓存 DeviceProbe 结果，避免批量任务每项重新探测（0.4s/项 × 100 = 40s 开销）。
/// 设备在运行期不变，探测一次即可复用。
/// </summary>
public sealed class CachedDeviceProbe : IDisposable
{
    private DeviceProbeResult? _cached;
    private bool _disposed;
    private readonly object _lock = new();

    /// <summary>探测（首次）或返回缓存结果。线程安全。</summary>
    public DeviceProbeResult Probe()
    {
        lock (_lock)
        {
            if (_cached != null) return _cached;
            using var probe = new DeviceProbe();
            _cached = probe.Probe();
            return _cached;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            // 缓存的 Adapter COM 对象随进程退出释放，无需显式 Dispose
            _disposed = true;
        }
    }
}
