namespace PanoToVideo.Core.Devices;

/// <summary>
/// 设备选择规则（开发规划 §7、PRD #2）。
/// 纯逻辑：过滤软件适配器与无硬件编码器者；不单凭 DedicatedVideoMemory==0 排除（核显可能为 0）；
/// 按显存降序、稳定排序选首选。
/// 实际 DXGI 枚举与 MF 探测在 Render 完成，填充 AdapterCandidate 后交本规则。
/// </summary>
public sealed class DeviceSelector
{
    /// <summary>过滤并按显存降序返回合格候选（稳定排序：显存相同者保持输入相对顺序）。</summary>
    public IReadOnlyList<AdapterCandidate> SelectEligible(IEnumerable<AdapterCandidate> candidates)
    {
        return candidates
            .Where(c => !c.IsSoftware)
            .Where(c => c.HasHardwareEncoder)
            .OrderByDescending(c => c.DedicatedVideoMemoryBytes)
            .ToList();
    }

    /// <summary>返回首选适配器，无合格者返回 null。</summary>
    public AdapterCandidate? SelectPreferred(IEnumerable<AdapterCandidate> candidates)
    {
        var eligible = SelectEligible(candidates);
        return eligible.Count == 0 ? null : eligible[0];
    }
}
