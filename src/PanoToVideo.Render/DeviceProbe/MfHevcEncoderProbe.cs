using PanoToVideo.Core.Precheck;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace PanoToVideo.Render.DeviceProbe;

/// <summary>
/// IHevcEncoderProbe 的 MF 实现（开发规划阶段3任务3）。
/// 枚举 H.265(HEVC) 硬件编码器，存在即可用。
/// 不支持时 PresetResolver 退回 H.264 并提示。
/// </summary>
public sealed class MfHevcEncoderProbe : IHevcEncoderProbe, IDisposable
{
    private bool _mfStarted;

    public bool IsAvailable()
    {
        MFStartup(useLightVersion: true);
        _mfStarted = true;
        try
        {
            var collection = MFTEnumEx(
                TransformCategoryGuids.VideoEncoder,
                (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagAsyncmft),
                new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.NV12 },
                new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H265 });
            using (collection)
            {
                foreach (var activate in collection)
                {
                    using (activate)
                    {
                        // 能激活即视为可用
                        using var transform = activate.ActivateObject<IMFTransform>();
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_mfStarted)
        {
            MFShutdown();
            _mfStarted = false;
        }
    }
}
