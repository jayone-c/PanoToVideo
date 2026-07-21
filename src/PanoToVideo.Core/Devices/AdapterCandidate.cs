namespace PanoToVideo.Core.Devices;

/// <summary>
/// DXGI 适配器候选（纯数据，不含 GPU 类型，保证可单测）。
/// Luid 为 DXGI Adapter LUID 的 64 位值，用于校验"渲染与编码同适配器"（规划不变式）。
/// HasHardwareEncoder 由 Render 的 MF 探测填充：该 LUID 上能否完成 H.264 NV12 纹理输入编码。
/// </summary>
public sealed record AdapterCandidate(
    long Luid,
    ulong DedicatedVideoMemoryBytes,
    bool IsSoftware,
    bool HasHardwareEncoder,
    string Description);
