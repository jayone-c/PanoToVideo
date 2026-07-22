namespace PanoToVideo.Core.Precheck;

/// <summary>
/// H.265 硬件编码器可用性探测（开发规划阶段3任务3）。
/// 注入实现便于单测；真实实现用 MF 枚举 HEVC 硬件编码器。
/// </summary>
public interface IHevcEncoderProbe
{
    bool IsAvailable();
}

/// <summary>预设解析结果。</summary>
public sealed record PresetResolveResult(ExportPreset Preset, string? FallbackReason);

/// <summary>
/// 输出预设解析（开发规划阶段3任务3、PRD 验收门槛）。
/// H.265(体积优先)在所选编码器不支持时退回 H.264(兼容优先)并提示，不伪称硬件加速。
/// </summary>
public sealed class PresetResolver
{
    private readonly IHevcEncoderProbe _hevcProbe;

    public PresetResolver(IHevcEncoderProbe hevcProbe) => _hevcProbe = hevcProbe;

    public PresetResolveResult Resolve(ExportPreset requested)
    {
        if (requested == ExportPreset.Size)
        {
            // 体积优先 = H.265，探测硬件编码器可用性
            if (!_hevcProbe.IsAvailable())
            {
                return new PresetResolveResult(
                    ExportPreset.Compatibility,
                    "H.265 硬件编码器不支持，已退回 H.264");
            }
        }
        // H.264（兼容优先）始终可用，无需退回
        return new PresetResolveResult(requested, null);
    }
}
