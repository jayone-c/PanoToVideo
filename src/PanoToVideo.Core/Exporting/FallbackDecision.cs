using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Core.Exporting;

/// <summary>
/// 导出后端（GPU NVENC 硬件编码 / CPU 软件回退）。
/// </summary>
public enum ExportBackend
{
    GpuNvenc,
    CpuFallback,
}

/// <summary>
/// 回退决策结果：选择的后端 + UI/日志用的设备/编码器标签 + 回退标志 + 原因。
/// PRD #5：CPU 回退必须显式标注，不得伪称硬件加速。
/// </summary>
public sealed record FallbackDecision(
    ExportBackend Backend,
    string ProjectionDeviceLabel,
    string EncoderLabel,
    bool UsedCpuFallback,
    string? Reason,
    HardwareEncoderKind? HardwareEncoder = null);

/// <summary>
/// 回退决策纯逻辑（开发规划 §7、PRD #5）。
/// 输入 GPU 可用性快照 + PresetResolver 已解析的预设，输出后端选择与标签。
/// H.265 可用性由 PresetResolver 提前判定（Size 预设仅在 HEVC 可用时保留），
/// 本决策只负责 GPU vs CPU 选择与编码器标签生成，便于脱离 GPU 单测。
/// </summary>
public static class FallbackDecider
{
    /// <summary>
    /// 根据 GPU 可用性与已解析预设决定导出后端。
    /// 无 GPU 或无 H.264 硬件编码器 -> CPU 回退（libx264）。
    /// 否则按 resolvedPreset 选 H.264/H.265 NVENC。
    /// </summary>
    public static FallbackDecision Decide(GpuAvailability gpu, ExportPreset resolvedPreset)
    {
        var h264Encoder = gpu.H264HardwareEncoder ?? (gpu.HasH264Encoder ? HardwareEncoderKind.Nvenc : null);
        if (!gpu.HasGpu || h264Encoder is null)
        {
            return new FallbackDecision(
                ExportBackend.CpuFallback,
                ProjectionDeviceLabel: "CPU",
                EncoderLabel: "libx264",
                UsedCpuFallback: true,
                Reason: gpu.FallbackReason ?? "无合格 GPU 设备或硬件 H.264 编码器，已回退 CPU");
        }

        // PresetResolver 已确保 Size 预设仅在内含 HEVC 可用时保留
        bool useHevc = resolvedPreset == ExportPreset.Size;
        var hardwareEncoder = useHevc ? gpu.HevcHardwareEncoder ?? h264Encoder : h264Encoder;
        string encoderLabel = $"H.{(useHevc ? "265" : "264")} {HardwareEncoderLabel(hardwareEncoder.Value)}";
        return new FallbackDecision(
            ExportBackend.GpuNvenc,
            ProjectionDeviceLabel: gpu.PreferredDeviceDescription ?? "GPU",
            EncoderLabel: encoderLabel,
            UsedCpuFallback: false,
            Reason: null,
            HardwareEncoder: hardwareEncoder);
    }

    public static string HardwareEncoderLabel(HardwareEncoderKind kind) => kind switch
    {
        HardwareEncoderKind.Amf => "AMF",
        HardwareEncoderKind.Qsv => "QSV",
        _ => "NVENC",
    };
}
