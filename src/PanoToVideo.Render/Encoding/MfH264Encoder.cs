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

        // 1. MF DXGI 设备管理器挂 D3D11 设备（ADR Q1）
        var dxgiManager = MediaFactory.MFCreateDXGIDeviceManager();
        dxgiManager.ResetDevice(device);

        // 2. SinkWriter 属性：挂 DXGI 设备管理器（启用硬件加速零拷贝路径）
        var sinkAttrs = MediaFactory.MFCreateAttributes(3);
        sinkAttrs.Set(SinkWriterAttributeKeys.D3DManager, dxgiManager);

        // 3. 输出 MP4 容器
        _sinkWriter = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, sinkAttrs)
            ?? throw new InvalidOperationException($"SinkWriter 创建失败: {outputPath}");

        // 4. 输出流类型：H.264
        var outType = MediaFactory.MFCreateMediaType();
        outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
        outType.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive);
        outType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)(uint)width << 32) | (uint)height);
        outType.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)(uint)fps << 32) | (uint)1);
        outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
        _streamIndex = _sinkWriter.AddStream(outType);

        // 5. 输入类型：NV12（编码器接收的 GPU 输入格式，ADR Q3）
        var inType = MediaFactory.MFCreateMediaType();
        inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        inType.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive);
        inType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)(uint)width << 32) | (uint)height);
        inType.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)(uint)fps << 32) | (uint)1);

        // void 方法：失败时抛 SharpGenException，无需 .Failure 检查
        _sinkWriter.SetInputMediaType(_streamIndex, inType, null);
        _sinkWriter.BeginWriting();
    }

    /// <summary>
    /// 零拷贝提交一帧 NV12 纹理给编码器（ADR Q2）。
    /// NV12 纹理全程留在 GPU，经 MFCreateDXGISurfaceBuffer 包裹为 IMFSample，不经 CPU 回读。
    /// </summary>
    public void SubmitFrame(ID3D11Texture2D nv12Texture, int frameIndex)
    {
        var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, nv12Texture, 0, false);
        buffer.CurrentLength = _width * _height * 3 / 2;

        var sample = MediaFactory.MFCreateSample();
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
