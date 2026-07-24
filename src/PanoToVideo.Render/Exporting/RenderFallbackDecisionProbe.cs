using PanoToVideo.Core.Exporting;
using PanoToVideo.Render.DeviceProbe;

namespace PanoToVideo.Render.Exporting;

/// <summary>
/// IFallbackDecisionProbe 的 Render 层实现（P0-1：GPU 可用性探测）。
/// 组合 CachedDeviceProbe（GPU + H.264 硬件编码器激活验证）与 MfHevcEncoderProbe（H.265 编码器枚举），
/// 产出 GpuAvailability 快照供 Core FallbackDecider 做纯逻辑决策。
/// 设备探测结果缓存复用（批量任务不重复探测）。
/// </summary>
public sealed class RenderFallbackDecisionProbe : IFallbackDecisionProbe
{
    private readonly CachedDeviceProbe _deviceProbe;
    private readonly MfHevcEncoderProbe _hevcProbe;
    // 静态缓存：批量任务复用 GPU 可用性快照，避免每项重新探测（DeviceProbe ~0.4s/项 + HEVC 枚举）
    private static GpuAvailability? s_cached;

    /// <summary>默认构造：使用静态缓存的设备探测与 HEVC 探测。</summary>
    public RenderFallbackDecisionProbe()
        : this(new CachedDeviceProbe(), new MfHevcEncoderProbe())
    {
    }

    /// <summary>测试/注入用构造：可传入自定义探测实现。</summary>
    public RenderFallbackDecisionProbe(CachedDeviceProbe deviceProbe, MfHevcEncoderProbe hevcProbe)
    {
        _deviceProbe = deviceProbe;
        _hevcProbe = hevcProbe;
    }

    public GpuAvailability Probe()
    {
        if (s_cached != null) return s_cached;

        DeviceProbeResult devices;
        try
        {
            devices = _deviceProbe.Probe();
        }
        catch (Exception ex)
        {
            s_cached = new GpuAvailability(
                HasGpu: false, HasH264Encoder: false, HasHevcEncoder: false,
                PreferredDeviceDescription: null,
                FallbackReason: $"GPU 设备探测失败: {ex.GetType().Name}: {ex.Message}");
            return s_cached;
        }

        var preferred = devices.Preferred;
        if (preferred == null)
        {
            s_cached = new GpuAvailability(
                HasGpu: false, HasH264Encoder: false, HasHevcEncoder: false,
                PreferredDeviceDescription: null,
                FallbackReason: "无合格 GPU 设备或无法激活 H.264 硬件编码器");
            return s_cached;
        }

        bool hasHevc;
        try
        {
            hasHevc = _hevcProbe.IsAvailable();
        }
        catch
        {
            hasHevc = false;
        }

        // MF 可激活不代表当前 FFmpeg 构建也带有对应 NVENC 编码器；生产路径需要两者均满足。
        var hasH264Nvenc = FfmpegCapabilityProbe.HasEncoder("h264_nvenc");
        hasHevc &= FfmpegCapabilityProbe.HasEncoder("hevc_nvenc");

        s_cached = new GpuAvailability(
            HasGpu: true,
            HasH264Encoder: hasH264Nvenc,
            HasHevcEncoder: hasHevc,
            PreferredDeviceDescription: preferred.Candidate.Description,
            FallbackReason: hasH264Nvenc ? null : "当前 FFmpeg 未提供 h264_nvenc 编码器",
            DedicatedVideoMemoryBytes: preferred.Candidate.DedicatedVideoMemoryBytes > long.MaxValue
                ? long.MaxValue
                : (long)preferred.Candidate.DedicatedVideoMemoryBytes);
        return s_cached;
    }

    /// <summary>重置静态缓存（测试用：强制下次 Probe 重新探测）。</summary>
    internal static void ResetCache() => s_cached = null;
}
