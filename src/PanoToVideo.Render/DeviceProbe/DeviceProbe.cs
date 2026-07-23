using PanoToVideo.Core.Devices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using static Vortice.Direct3D11.D3D11;

namespace PanoToVideo.Render.DeviceProbe;

/// <summary>
/// 设备探测：对每个非软件 DXGI 适配器创建 D3D11 设备 + 挂 MF + 枚举 H.264 硬件编码器，
/// 验证可在该适配器上激活编码器（ADR Q4：区分真实 GPU 与虚拟镜像适配器）。
/// 不得仅凭 DedicatedVideoMemory==0 排除（核显可能为 0）。
/// </summary>
public sealed class DeviceProbe : IDisposable
{
    private bool _mfStarted;

    /// <summary>探测所有合格设备：能在适配器上激活 H.264 硬件编码器者为 true。</summary>
    public DeviceProbeResult Probe()
    {
        MediaFactory.MFStartup(useLightVersion: true);
        _mfStarted = true;

        var candidates = new List<(IDXGIAdapter1 Adapter, AdapterCandidate Candidate, string? EncoderName)>();
        bool completed = false;
        try
        {
            using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(debug: false);

            for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
            {
                using var adapterRef = adapter;
                var desc = adapterRef.Description1;
                var isSoftware = (desc.Flags & AdapterFlags.Software) != AdapterFlags.None;
                long luid = ((long)(uint)desc.Luid.LowPart) | ((long)desc.Luid.HighPart << 32);

                string? encoderName = null;
                bool hasEncoder = false;
                if (!isSoftware)
                    (hasEncoder, encoderName) = TryProbeEncoder(adapterRef);

                var candidate = new AdapterCandidate(
                    Luid: luid,
                    DedicatedVideoMemoryBytes: desc.DedicatedVideoMemory,
                    IsSoftware: isSoftware,
                    HasHardwareEncoder: hasEncoder,
                    Description: desc.Description);

                // 保留可激活编码器的适配器 COM 对象（QueryInterface 转移所有权）
                if (hasEncoder)
                    candidates.Add((adapterRef.QueryInterface<IDXGIAdapter1>(), candidate, encoderName));
            }

            var selector = new DeviceSelector();
            var preferred = selector.SelectPreferred(candidates.Select(c => c.Candidate));
            (IDXGIAdapter1 Adapter, AdapterCandidate Candidate, string? EncoderName)? preferredEntry = null;
            if (preferred != null)
                preferredEntry = candidates.First(c => c.Candidate.Luid == preferred.Luid);

            var result = new DeviceProbeResult(
                candidates.Select(c => new DeviceEntry(c.Candidate, c.Adapter, c.EncoderName)).ToList(),
                preferredEntry == null ? null : new DeviceEntry(preferredEntry.Value.Candidate, preferredEntry.Value.Adapter, preferredEntry.Value.EncoderName));
            completed = true;
            return result;
        }
        finally
        {
            // M8 修复：异常路径 MFShutdown + 清理已收集的 candidates（避免泄漏 Adapter COM）
            if (!completed)
            {
                foreach (var c in candidates)
                {
                    try { c.Adapter.Dispose(); } catch { }
                }
                if (_mfStarted)
                {
                    MediaFactory.MFShutdown();
                    _mfStarted = false;
                }
            }
        }
    }

    /// <summary>对适配器创建 D3D11 设备 + 挂 MF + 枚举 H.264 硬件编码器 + ActivateObject 验证。</summary>
    private (bool HasEncoder, string? Name) TryProbeEncoder(IDXGIAdapter1 adapter)
    {
        try
        {
            // 创建 D3D11 设备（绑定该适配器）
            FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
            D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, levels,
                out ID3D11Device device, out _, out _).CheckError();
            using (device)
            {
                // H7 修复：dxgiManager using 释放 COM
                using var dxgiManager = MediaFactory.MFCreateDXGIDeviceManager();
                dxgiManager.ResetDevice(device);

                // 枚举 H.264 硬件编码器（输入 NV12，输出 H.264）
                var collection = MediaFactory.MFTEnumEx(
                    TransformCategoryGuids.VideoEncoder,
                    (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagAsyncmft),
                    new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.NV12 },
                    new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 });
                using (collection)
                {
                    foreach (var activate in collection)
                    {
                        using (activate)
                        {
                            // 取友好名（NVENC/AMF/QSV 等）
                            string name = activate.GetString(TransformAttributeKeys.MftFriendlyNameAttribute);

                            // ActivateObject 验证可在该设备上激活编码器（虚拟镜像适配器会失败）
                            var transform = activate.ActivateObject<IMFTransform>();
                            transform.Dispose();
                            return (true, name);
                        }
                    }
                }
            }
            return (false, null);
        }
        catch
        {
            // 适配器无法创建设备或激活编码器 -> 无可用硬件编码器
            return (false, null);
        }
    }

    public void Dispose()
    {
        if (_mfStarted)
        {
            MediaFactory.MFShutdown();
            _mfStarted = false;
        }
    }
}

/// <summary>单个合格设备：适配器候选 + COM 适配器对象 + 编码器名。</summary>
public sealed record DeviceEntry(AdapterCandidate Candidate, IDXGIAdapter1 Adapter, string? EncoderName);

/// <summary>设备探测结果：合格设备列表 + 首选。</summary>
public sealed record DeviceProbeResult(IReadOnlyList<DeviceEntry> Eligible, DeviceEntry? Preferred);
