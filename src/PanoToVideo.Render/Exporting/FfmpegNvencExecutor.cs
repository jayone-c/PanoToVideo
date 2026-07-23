using System.Diagnostics;
using System.Runtime.InteropServices;
using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Projection;
using PanoToVideo.Core.Validation;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using DeviceProbeImpl = PanoToVideo.Render.DeviceProbe.DeviceProbe;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace PanoToVideo.Render.Exporting;

/// <summary>
/// IExportExecutor 的 FFmpeg h264_nvenc 实现（阶段4性能达标路径）。
/// GPU 投影 NV12 帧回读 -> stdin 管道喂 FFmpeg h264_nvenc 硬件编码。
/// 非 Vortice MF 编码（绕过 SinkWriter 硬件崩溃），但 h264_nvenc 是硬件编码，稳定且快。
/// </summary>
public sealed class FfmpegNvencExecutor : IExportExecutor
{
    private readonly byte[] _erpRgba;
    private readonly int _erpW;
    private readonly int _erpH;
    private readonly uint _bitrate;
    private DeviceEntry? _device;
    // 静态缓存：批量任务复用设备探测结果，避免每项重新探测（0.4s/项）
    private static readonly CachedDeviceProbe s_cachedProbe = new();

    public FfmpegNvencExecutor(byte[] erpRgba, int erpW, int erpH, RenderParameters parameters, ExportPreset preset)
    {
        _erpRgba = erpRgba;
        _erpW = erpW;
        _erpH = erpH;
        _bitrate = (uint)ExportPrecheck.EstimateBitrate(preset, parameters.Width, parameters.Height, parameters.Fps);
    }

    public ExportExecutionResult Execute(
        string tmpPath, ImageInfo imageInfo, RenderParameters parameters, ExportPreset preset,
        CancellationToken cancellationToken = default,
        IProgress<ExportProgress>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        int totalFrames = parameters.TotalFrames;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tmpPath)!);

            // 1. DeviceProbe 选首选设备（缓存复用，避免每项重新探测）
            _device = s_cachedProbe.Probe().Preferred ?? throw new InvalidOperationException("无合格 GPU 设备");

            // 2. 创建 D3D11 设备
            D3D11CreateDevice(_device.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
                out ID3D11Device device, out _, out _).CheckError();
            using (device)
            {
                using var pipeline = new EquirectPipeline(device);
                using var srv = pipeline.UploadErpTexture(_erpRgba, _erpW, _erpH);

                // 3. 启动 FFmpeg h264_nvenc 子进程（stdin rawvideo NV12 -> H.264 MP4）
                var ffmpeg = new ProcessStartInfo("ffmpeg",
                    $"-y -f rawvideo -pixel_format nv12 -video_size {parameters.Width}x{parameters.Height} " +
                    $"-framerate {parameters.Fps} -i - -c:v h264_nvenc -b:v {_bitrate} -movflags +faststart \"{tmpPath}\"")
                {
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var ff = Process.Start(ffmpeg) ?? throw new InvalidOperationException("FFmpeg 启动失败");
                string? ffErr = null;
                // H2 修复：异步读 stderr 避免管道死锁（同步 ReadToEnd 会因 stderr 缓冲满阻塞 FFmpeg 读 stdin）
                var errTask = Task.Run(() => ff.StandardError.ReadToEnd());
                try
                {
                    // 4. 逐帧：GPU 投影 NV12 -> 回读 -> 管道喂 FFmpeg
                    double projSec = 0, readbackSec = 0;
                    var nv12Bytes = new byte[parameters.Width * parameters.Height * 3 / 2];
                    for (int i = 0; i < totalFrames; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // FFmpeg 提前崩溃（管道断裂）则退出
                        if (ff.HasExited)
                        {
                            var err = errTask.IsCompleted ? errTask.Result : "";
                            throw new InvalidOperationException($"FFmpeg 提前退出（码 {ff.ExitCode}）: {err}");
                        }
                        double yaw = YawSchedule.YawAt(i, totalFrames, parameters.RotationDegrees, parameters.Direction);

                        var ps = Stopwatch.StartNew();
                        using var nv12 = pipeline.RenderFrameToNv12(srv, _erpW, _erpH,
                            parameters.Width, parameters.Height, parameters.HorizontalFov, yaw, parameters.Pitch);
                        ps.Stop();
                        projSec += ps.Elapsed.TotalSeconds;

                        var rs = Stopwatch.StartNew();
                        ReadBackNv12(device.ImmediateContext, nv12, nv12Bytes, parameters.Width, parameters.Height);
                        rs.Stop();
                        readbackSec += rs.Elapsed.TotalSeconds;

                        ff.StandardInput.BaseStream.Write(nv12Bytes, 0, nv12Bytes.Length);

                        progress?.Report(new ExportProgress(
                            i, totalFrames,
                            projSec > 0 ? (i + 1) / projSec : 0,
                            readbackSec > 0 ? (i + 1) / readbackSec : 0,
                            sw.Elapsed));
                    }
                    ff.StandardInput.Close();
                    ff.WaitForExit();
                    ffErr = errTask.IsCompleted ? errTask.Result : "";
                }
                finally
                {
                    // H1 修复：异常/取消路径确保 FFmpeg 进程被 Kill + 等待 + 释放句柄
                    try { ff.StandardInput?.Close(); } catch { }
                    try { if (!ff.HasExited) ff.Kill(entireProcessTree: true); } catch { }
                    try { ff.WaitForExit(2000); } catch { }
                    ff.Dispose();
                }
                if (ff.ExitCode != 0)
                    throw new InvalidOperationException($"FFmpeg 退出码 {ff.ExitCode}: {ffErr}");
            }
            sw.Stop();
            double avgFps = totalFrames / sw.Elapsed.TotalSeconds;
            return new ExportExecutionResult(true, null, sw.Elapsed, avgFps);
        }
        catch (OperationCanceledException) { sw.Stop(); return new ExportExecutionResult(false, "已取消", sw.Elapsed, 0); }
        catch (Exception ex) { sw.Stop(); return new ExportExecutionResult(false, $"{ex.GetType().Name}: {ex.Message}", sw.Elapsed, 0); }
    }

    /// <summary>回读 NV12 平面纹理为连续字节（Y plane + UV plane，FFmpeg NV12 布局）。</summary>
    private static void ReadBackNv12(ID3D11DeviceContext ctx, ID3D11Texture2D nv12, byte[] dst, int w, int h)
    {
        var desc = nv12.Description;
        var stagingDesc = new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1,
            Format = desc.Format, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging, BindFlags = BindFlags.None, CPUAccessFlags = CpuAccessFlags.Read,
        };
        using var staging = ctx.Device.CreateTexture2D(stagingDesc);
        ctx.CopyResource(staging, nv12);
        var mapped = ctx.Map(staging, 0, MapMode.Read);
        try
        {
            int yPitch = (int)mapped.RowPitch;
            int ySize = w * h;
            // Y plane
            for (int y = 0; y < h; y++)
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * yPitch), dst, y * w, w);
            // UV plane（高度 h/2）
            IntPtr uvPtr = IntPtr.Add(mapped.DataPointer, yPitch * h);
            int uvPitch = yPitch; // NV12 UV 行间距同 Y
            for (int y = 0; y < h / 2; y++)
                Marshal.Copy(IntPtr.Add(uvPtr, y * uvPitch), dst, ySize + y * w, w);
        }
        finally { ctx.Unmap(staging, 0); }
    }

    public void AtomicMove(string tmpPath, string finalPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        // H13 修复：用 File.Move(overwrite:true) 原子覆盖，避免 Delete 后 Move 前崩溃丢数据
        File.Move(tmpPath, finalPath, overwrite: true);
    }

    public void Cleanup(string tmpPath)
    {
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
    }
}
