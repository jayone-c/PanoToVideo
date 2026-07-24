using System.Diagnostics;
using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Projection;
using PanoToVideo.Core.Validation;

namespace PanoToVideo.Render.Exporting;

/// <summary>
/// IExportExecutor 的 CPU 回退实现（P0-1：PRD #5 必须提供 CPU 回退）。
/// 逐帧复用 Core 的 EquirectProjection+EquirectRenderer（已 PSNR 验证）做软件投影，
/// RGBA 帧 → FFmpeg stdin rawvideo → libx264 软件编码 MP4。
/// 命令委托 Core FfmpegCommandBuilder.BuildCpuFallback（-threads {cpuCores}）。
/// 性能远低于 GPU 路径（PRD 仅要求"提供回退"），日志显式标注 CPU 回退。
/// </summary>
public sealed class FfmpegCpuFallbackExecutor : IExportExecutor
{
    private readonly byte[] _erpRgba;
    private readonly int _erpW;
    private readonly int _erpH;
    private readonly uint _bitrate;

    public FfmpegCpuFallbackExecutor(byte[] erpRgba, int erpW, int erpH, RenderParameters parameters, ExportPreset preset)
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

            // 1. 一次性把源 ERP RGBA 字节转 Rgb[]（丢弃 alpha，供 EquirectRenderer 采样）
            var erpRgb = RgbaBytesToRgb(_erpRgba, _erpW, _erpH);

            // 2. 构造 FFmpeg 命令（Core 纯逻辑：libx264 -threads {cpuCores}）
            var cmd = FfmpegCommandBuilder.BuildCpuFallback(
                tmpPath, parameters.Width, parameters.Height, parameters.Fps, _bitrate, parameters.CpuCores);

            // 3. 启动 FFmpeg 子进程（stdin rawvideo rgba -> libx264 MP4）
            var ffInfo = new ProcessStartInfo(cmd.Exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in cmd.Args)
                ffInfo.ArgumentList.Add(arg);
            ffInfo.RedirectStandardInput = true;
            ffInfo.RedirectStandardError = true;

            var ff = Process.Start(ffInfo) ?? throw new InvalidOperationException("FFmpeg 启动失败");
            string? ffErr = null;
            int ffExitCode = -1;
            // H2 修复沿用：异步读 stderr 避免管道死锁
            var errTask = Task.Run(() => ff.StandardError.ReadToEnd());
            try
            {
                // 4. 逐帧：CPU 软件投影 RGBA -> 管道喂 FFmpeg
                int frameBytes = parameters.Width * parameters.Height * 4;
                var rgbaFrame = new byte[frameBytes];
                double projSec = 0;
                for (int i = 0; i < totalFrames; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ff.HasExited)
                    {
                        var err = errTask.IsCompleted ? errTask.Result : "";
                        throw new InvalidOperationException($"FFmpeg 提前退出（码 {ff.ExitCode}）: {err}");
                    }

                    double yaw = YawSchedule.YawAt(i, totalFrames, parameters.RotationDegrees, parameters.Direction, parameters.StartYaw);

                    var ps = Stopwatch.StartNew();
                    var frame = EquirectRenderer.RenderFrame(
                        erpRgb, _erpW, _erpH,
                        parameters.Width, parameters.Height, parameters.HorizontalFov, yaw, parameters.Pitch);
                    RgbToRgba(frame, rgbaFrame);
                    ps.Stop();
                    projSec += ps.Elapsed.TotalSeconds;

                    ff.StandardInput.BaseStream.Write(rgbaFrame, 0, rgbaFrame.Length);

                    progress?.Report(new ExportProgress(
                        i, totalFrames,
                        projSec > 0 ? (i + 1) / projSec : 0,
                        0, // CPU 路径投影与编码同线程，无独立编码 FPS
                        sw.Elapsed));
                }
                ff.StandardInput.Close();
                ff.WaitForExit();
                ffErr = errTask.IsCompleted ? errTask.Result : "";
                ffExitCode = ff.ExitCode;
            }
            finally
            {
                // H1 修复沿用：异常/取消路径确保 FFmpeg 进程被 Kill + 等待 + 释放句柄
                try { ff.StandardInput?.Close(); } catch { }
                try { if (!ff.HasExited) ff.Kill(entireProcessTree: true); } catch { }
                try { ff.WaitForExit(2000); } catch { }
                ff.Dispose();
            }
            if (ffExitCode != 0)
                throw new InvalidOperationException($"FFmpeg 退出码 {ffExitCode}: {ffErr}");

            sw.Stop();
            double avgFps = totalFrames / sw.Elapsed.TotalSeconds;
            // P0-1/PRD #5：上报真实设备/编码器/回退标志，不得伪称硬件加速
            return new ExportExecutionResult(true, null, sw.Elapsed, avgFps)
            {
                ProjectionDevice = "CPU",
                EncoderName = "libx264",
                UsedCpuFallback = true,
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ExportExecutionResult(false, "已取消", sw.Elapsed, 0) { ProjectionDevice = "CPU", EncoderName = "libx264", UsedCpuFallback = true };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExportExecutionResult(false, $"{ex.GetType().Name}: {ex.Message}", sw.Elapsed, 0) { ProjectionDevice = "CPU", EncoderName = "libx264", UsedCpuFallback = true };
        }
    }

    /// <summary>源 ERP RGBA 字节（4 字节/像素）转 Rgb[]（3 字节/像素，丢弃 alpha）。</summary>
    private static Rgb[] RgbaBytesToRgb(byte[] rgba, int w, int h)
    {
        var rgb = new Rgb[w * h];
        for (int i = 0; i < w * h; i++)
        {
            rgb[i] = new Rgb(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);
        }
        return rgb;
    }

    /// <summary>Rgb[]（3 字节/像素）转 RGBA 字节（4 字节/像素，alpha=255），供 FFmpeg stdin rawvideo rgba。</summary>
    private static void RgbToRgba(ReadOnlySpan<Rgb> src, Span<byte> dst)
    {
        for (int i = 0; i < src.Length; i++)
        {
            dst[i * 4] = src[i].R;
            dst[i * 4 + 1] = src[i].G;
            dst[i * 4 + 2] = src[i].B;
            dst[i * 4 + 3] = 255;
        }
    }

    public void AtomicMove(string tmpPath, string finalPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Move(tmpPath, finalPath, overwrite: true);
    }

    public void Cleanup(string tmpPath)
    {
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
    }
}
