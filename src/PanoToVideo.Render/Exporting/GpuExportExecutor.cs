using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Projection;
using PanoToVideo.Core.Validation;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using PanoToVideo.Render.Encoding;
using DeviceProbeImpl = PanoToVideo.Render.DeviceProbe.DeviceProbe;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using static Vortice.Direct3D11.D3D11;

namespace PanoToVideo.Render.Exporting;

/// <summary>
/// IExportExecutor 的 GPU 实现：DeviceProbe 选设备 + ERP(已解码RGBA)上传 +
/// 逐帧 RenderFrameToNv12 零拷贝编码 MP4（开发规划阶段1任务4）。
/// JPEG/PNG 解码由 App 层（WIC/BitmapImage）完成后注入 RGBA 字节，Render 不依赖图像库。
/// </summary>
public sealed class GpuExportExecutor : IExportExecutor
{
    private readonly byte[] _erpRgba;
    private readonly int _erpW;
    private readonly int _erpH;
    private readonly uint _bitrate;
    private DeviceEntry? _device;

    public GpuExportExecutor(byte[] erpRgba, int erpW, int erpH, RenderParameters parameters, ExportPreset preset)
    {
        _erpRgba = erpRgba;
        _erpW = erpW;
        _erpH = erpH;
        _bitrate = (uint)ExportPrecheck.EstimateBitrate(preset, parameters.Width, parameters.Height, parameters.Fps);
    }

    public ExportExecutionResult Execute(string tmpPath, ImageInfo imageInfo, RenderParameters parameters, ExportPreset preset)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int totalFrames = parameters.TotalFrames;
        try
        {
            // 确保临时文件目录存在（MFCreateSinkWriterFromURL 要求路径可写）
            Directory.CreateDirectory(Path.GetDirectoryName(tmpPath)!);

            // 1. DeviceProbe 选首选设备
            using var probe = new DeviceProbeImpl();
            var probeResult = probe.Probe();
            _device = probeResult.Preferred ?? throw new InvalidOperationException("无合格 GPU 设备");

            // 2. 创建 D3D11 设备
            FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
            D3D11CreateDevice(_device.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                levels, out ID3D11Device device, out _, out _).CheckError();
            using (device)
            {
                using var pipeline = new EquirectPipeline(device);
                using var srv = pipeline.UploadErpTexture(_erpRgba, _erpW, _erpH);

                // 3. H.264 编码器（零拷贝）
                using var encoder = new MfH264Encoder(device, tmpPath,
                    parameters.Width, parameters.Height, parameters.Fps, _bitrate);

                // 4. 逐帧：YawSchedule -> RenderFrameToNv12 -> 零拷贝编码
                for (int i = 0; i < totalFrames; i++)
                {
                    double yaw = YawSchedule.YawAt(i, totalFrames, parameters.RotationDegrees, parameters.Direction);
                    using var nv12 = pipeline.RenderFrameToNv12(srv, _erpW, _erpH,
                        parameters.Width, parameters.Height, parameters.HorizontalFov, yaw, parameters.Pitch);
                    encoder.SubmitFrame(nv12, i);
                }
                encoder.Finalize();
            }
            sw.Stop();
            double avgFps = totalFrames > 0 ? totalFrames / sw.Elapsed.TotalSeconds : 0;
            return new ExportExecutionResult(true, null, sw.Elapsed, avgFps);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExportExecutionResult(false, $"{ex.GetType().Name}: {ex.Message}", sw.Elapsed, 0);
        }
    }

    public void AtomicMove(string tmpPath, string finalPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(tmpPath, finalPath);
    }

    public void Cleanup(string tmpPath)
    {
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* 清理失败不阻塞 */ }
    }

    /// <summary>暴露选定的设备/编码器名（供日志显示真实设备，非"GPU加速"文案）。</summary>
    public (string Device, string Encoder)? GetDeviceInfo() => _device == null
        ? null
        : (_device.Candidate.Description, _device.EncoderName ?? "H.264");
}
