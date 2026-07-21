using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace PanoToVideo.Render.DeviceProbe;

/// <summary>
/// Media Foundation 设备挂载探针（ADR 0001 Q1）。
/// 验证能否通过 MFCreateDXGIDeviceManager 把 D3D11 设备挂给 MF，
/// 这是零拷贝编码（D3D11 纹理 -> MF 编码器不经 CPU 回读）的前提。
/// </summary>
public static class MfDeviceProbe
{
    /// <summary>
    /// 尝试创建 MF DXGI 设备管理器并挂载 D3D11 设备。
    /// 返回是否成功、reset token、错误。
    /// </summary>
    public static (bool CanMountDevice, uint ResetToken, string? Error) ProbeDeviceMount(ID3D11Device device)
    {
        MediaFactory.MFStartup(useLightVersion: true);
        try
        {
            var manager = MediaFactory.MFCreateDXGIDeviceManager();
            uint token = manager.ResetToken;
            // ResetDevice 把 D3D11 设备注册到 MF 设备管理器（ADR Q1）
            manager.ResetDevice(device);
            return (true, token, null);
        }
        catch (Exception ex)
        {
            return (false, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            MediaFactory.MFShutdown();
        }
    }

    /// <summary>
    /// 枚举 H.264 硬件编码器（输入 NV12，输出 H.264）。
    /// 验证系统存在可用的硬件 H.264 编码器（ADR Q1 设备探测 + HasHardwareEncoder 真实化）。
    /// </summary>
    public static uint EnumerateH264HardwareEncoders()
    {
        MediaFactory.MFStartup(useLightVersion: true);
        try
        {
            var inputType = new RegisterTypeInfo
            {
                GuidMajorType = MediaTypeGuids.Video,
                GuidSubtype = VideoFormatGuids.NV12,
            };
            var outputType = new RegisterTypeInfo
            {
                GuidMajorType = MediaTypeGuids.Video,
                GuidSubtype = VideoFormatGuids.H264,
            };

            MediaFactory.MFTEnumEx(
                TransformCategoryGuids.VideoEncoder,
                (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagAsyncmft),
                inputType,
                outputType,
                out IntPtr pActivate,
                out uint count);

            if (pActivate != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pActivate);
            return count;
        }
        finally
        {
            MediaFactory.MFShutdown();
        }
    }
}

