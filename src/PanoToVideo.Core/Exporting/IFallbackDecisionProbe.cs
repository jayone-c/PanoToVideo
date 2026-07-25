namespace PanoToVideo.Core.Exporting;

/// <summary>FFmpeg 硬件编码器族。投影仍使用同一条 D3D11 GPU 路径，编码按设备厂商选择对应实现。</summary>
public enum HardwareEncoderKind
{
    Nvenc,
    Amf,
    Qsv,
}

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
    long DedicatedVideoMemoryBytes = 0,
    HardwareEncoderKind? H264HardwareEncoder = null,
    HardwareEncoderKind? HevcHardwareEncoder = null);

/// <summary>
/// GPU 可用性探测抽象（Render 层实现：组合 DeviceProbe + MfHevcEncoderProbe）。
/// Core 决策逻辑依赖此接口，便于单测注入 fake。
/// </summary>
public interface IFallbackDecisionProbe
{
    /// <summary>探测当前环境 GPU 与硬件编码器可用性。</summary>
    GpuAvailability Probe();
}
