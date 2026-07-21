using PanoToVideo.Core.Devices;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace PanoToVideo.Render.DeviceProbe;

/// <summary>
/// DXGI 适配器枚举（开发规划 §7、PRD #2）。
/// 枚举所有适配器并填充 AdapterCandidate 纯数据，供 Core.DeviceSelector 过滤排序。
/// </summary>
/// <remarks>
/// 返回的 (IDXGIAdapter1, AdapterCandidate) 对中，IDXGIAdapter1 是 COM 对象，
/// 调用方负责 Dispose。HasHardwareEncoder 暂占位为 true，
/// 真实 MF H.264 NV12 探测在 Task 10 接入（替换此处）。
/// </remarks>
public static class DxgiDeviceEnumerator
{
    public static List<(IDXGIAdapter1 Adapter, AdapterCandidate Candidate)> Enumerate()
    {
        var result = new List<(IDXGIAdapter1, AdapterCandidate)>();
        using var factory = CreateDXGIFactory2<IDXGIFactory4>(debug: false);

        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            using var adapterRef = adapter;
            var desc = adapterRef.Description1;
            var isSoftware = (desc.Flags & AdapterFlags.Software) != AdapterFlags.None;

            // LUID -> long（LowPart 无符号，HighPart 符号扩展）
            long luidValue = ((long)(uint)desc.Luid.LowPart) | ((long)desc.Luid.HighPart << 32);

            var candidate = new AdapterCandidate(
                Luid: luidValue,
                DedicatedVideoMemoryBytes: desc.DedicatedVideoMemory,
                IsSoftware: isSoftware,
                HasHardwareEncoder: true, // 占位：Task 10 接入真实 MF NV12 探测
                Description: desc.Description);
            result.Add((adapterRef.QueryInterface<IDXGIAdapter1>(), candidate));
        }
        return result;
    }
}
