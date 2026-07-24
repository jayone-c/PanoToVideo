namespace PanoToVideo.Core.Exporting;

/// <summary>
/// GPU 可用性快照（纯数据，由 Render 层实现 IFallbackDecisionProbe 填充）。
/// Core 通过此抽象做回退决策，不直接依赖 DXGI/MF。
/// </summary>
public sealed record GpuAvailability(
    bool HasGpu,
    bool HasH264Encoder,
    bool HasHevcEncoder,
    string? PreferredDeviceDescription,
    string? FallbackReason,
    long DedicatedVideoMemoryBytes = 0);

/// <summary>
/// GPU 可用性探测抽象（Render 层实现：组合 DeviceProbe + MfHevcEncoderProbe）。
/// Core 决策逻辑依赖此接口，便于单测注入 fake。
/// </summary>
public interface IFallbackDecisionProbe
{
    /// <summary>探测当前环境 GPU 与硬件编码器可用性。</summary>
    GpuAvailability Probe();
}
