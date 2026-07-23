using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Format = Vortice.DXGI.Format;

namespace PanoToVideo.Render.Encoding;

/// <summary>
/// Media Foundation H.264 零拷贝编码器（ADR Q2）。
/// 用 IMFDXGIDeviceManager 挂 D3D11 设备，MFCreateDXGISurfaceBuffer 零拷贝包裹 NV12 纹理喂 IMFTransform。
/// 全程 GPU 纹理 -> MF 编码器不经 CPU 回读（开发规划 §1.3 不变式）。
/// </summary>
public sealed class MfH264Encoder : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly uint _bitrate;
    private readonly long _frameDurationHns;
    private readonly IMFSinkWriter _sinkWriter;
    private readonly int _streamIndex;
    private bool _finalized;

    public MfH264Encoder(
        ID3D11Device device,
        string outputPath,
        int width, int height, int fps, uint bitrate)
    {
        _device = device;
        _width = width;
        _height = height;
        _fps = fps;
        _bitrate = bitrate;
        _frameDurationHns = 10_000_000L / fps; // 100ns 单位

        MediaFactory.MFStartup(useLightVersion: false);
        bool constructed = false;
        try
        {
            // 1. MF DXGI 设备管理器挂 D3D11 设备（ADR Q1）+ H6/M6: 本地 COM using
            using var dxgiManager = MediaFactory.MFCreateDXGIDeviceManager();
            dxgiManager.ResetDevice(device);

            // 2. SinkWriter 属性：挂 DXGI 设备管理器（启用零拷贝输入路径）
            using var sinkAttrs = MediaFactory.MFCreateAttributes(3);
            sinkAttrs.Set(SinkWriterAttributeKeys.D3DManager, dxgiManager);
            // 注：ReadwriteEnableHardwareTransforms 启用 NVENC 在本机致访问违规崩溃(~300帧后),
            //     回退软件编码(稳定但 Finalize 串行编码慢)。硬件路径稳定性待排查。

            // 3. 输出 MP4 容器
            _sinkWriter = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, sinkAttrs)
                ?? throw new InvalidOperationException($"SinkWriter 创建失败: {outputPath}");

            // 4. 输出流类型：H.264
            using var outType = MediaFactory.MFCreateMediaType();
            outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            outType.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive);
            outType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)(uint)width << 32) | (uint)height);
            outType.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)(uint)fps << 32) | (uint)1);
            outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
            _streamIndex = _sinkWriter.AddStream(outType);

            // 5. 输入类型：NV12（编码器接收的 GPU 输入格式，ADR Q3）
            using var inType = MediaFactory.MFCreateMediaType();
            inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
            inType.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive);
            inType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)(uint)width << 32) | (uint)height);
            inType.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)(uint)fps << 32) | (uint)1);

            // void 方法：失败时抛 SharpGenException，无需 .Failure 检查
            _sinkWriter.SetInputMediaType(_streamIndex, inType, null);
            _sinkWriter.BeginWriting();
            constructed = true;
        }
        finally
        {
            // H4 修复：构造失败时配对 MFShutdown（成功则由 Dispose 负责）
            if (!constructed)
            {
                _sinkWriter?.Dispose();
                MediaFactory.MFShutdown();
            }
        }
    }

    /// <summary>
    /// 零拷贝提交一帧 NV12 纹理给编码器（ADR Q2）。
    /// NV12 纹理全程留在 GPU，经 MFCreateDXGISurfaceBuffer 包裹为 IMFSample，不经 CPU 回读。
    /// </summary>
    public void SubmitFrame(ID3D11Texture2D nv12Texture, int frameIndex)
    {
        // H5 修复：buffer/sample using 释放 COM 包装器（MF 内部 AddRef 独立，Dispose 包装器安全）
        using var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, nv12Texture, 0, false);
        buffer.CurrentLength = _width * _height * 3 / 2;

        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = frameIndex * _frameDurationHns;
        sample.SampleDuration = _frameDurationHns;
        _sinkWriter.WriteSample(_streamIndex, sample);
    }

    public void Finalize()
    {
        if (_finalized) return;
        _sinkWriter.Finalize();
        _finalized = true;
    }

    public void Dispose()
    {
        try { Finalize(); } catch { /* finalize 失败不阻塞 Dispose */ }
        _sinkWriter?.Dispose();
        MediaFactory.MFShutdown();
    }
}
