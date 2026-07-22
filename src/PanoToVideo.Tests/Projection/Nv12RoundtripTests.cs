using System.Diagnostics;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using static Vortice.Direct3D11.D3D11;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// NV12 平面写入端到端回归测试（阶段4修复）。
/// GPU RGBA 原帧 -> NV12 平面(Y全分辨率+UV半高2×2下采样) -> 回读 -> FFmpeg 编解码 -> RGBA
/// 对比 PSNR ≥ 30dB（H.264 编码压缩 + NV12 色度下采样容差）。
/// 防止"帧率达标但画面损坏"回归（NV12 双平面视口/下采样错误）。
/// </summary>
[Trait("Category", "GeometryReference")]
public class Nv12RoundtripTests
{
    private static readonly ID3D11Device s_device;
    private static readonly EquirectPipeline s_pipeline;
    private static readonly ID3D11ShaderResourceView s_erpSrv;
    private const int ErpW = 8192, ErpH = 4096, Out = 320;

    static Nv12RoundtripTests()
    {
        using var probe = new DeviceProbe();
        var preferred = probe.Probe().Preferred ?? throw new InvalidOperationException("无合格设备");
        D3D11CreateDevice(preferred.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out s_device, out _, out _).CheckError();
        s_pipeline = new EquirectPipeline(s_device);

        var root = FindRepoRoot();
        var rgb = File.ReadAllBytes(Path.Combine(root, "tests", "fixtures", "erp_8192x4096.bin"));
        var rgba = new byte[ErpW * ErpH * 4];
        for (int i = 0; i < ErpW * ErpH; i++)
        {
            rgba[i * 4] = rgb[i * 3]; rgba[i * 4 + 1] = rgb[i * 3 + 1];
            rgba[i * 4 + 2] = rgb[i * 3 + 2]; rgba[i * 4 + 3] = 255;
        }
        s_erpSrv = s_pipeline.UploadErpTexture(rgba, ErpW, ErpH);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 0)]
    [InlineData(0, 30)]
    public void NV12回读经FFmpeg解码与GPU原帧PSNR达标(int yaw, int pitch)
    {
        var root = FindRepoRoot();
        var tmpDir = Path.Combine(root, "tests", "diagnose", $"nv12_{yaw}_{pitch}");
        Directory.CreateDirectory(tmpDir);

        // 1. GPU RGBA 原帧（正确参考）
        var gpuRgba = s_pipeline.RenderFrameToRgba(s_erpSrv, ErpW, ErpH, Out, Out, 75, yaw, pitch);

        // 2. NV12 平面渲染 + 回读（Y全分辨率 + UV半高）
        using var nv12 = s_pipeline.RenderFrameToNv12(s_erpSrv, ErpW, ErpH, Out, Out, 75, yaw, pitch);
        var nv12Bytes = s_pipeline.ReadNv12ToBytes(nv12, Out, Out);
        Assert.Equal(Out * Out * 3 / 2, nv12Bytes.Length); // Y(Out*Out) + UV(Out*Out/2)

        // 3. NV12 rawvideo -> FFmpeg h264_nvenc 编码 -> 解码 RGBA
        var rawPath = Path.Combine(tmpDir, "input.nv12");
        var mp4Path = Path.Combine(tmpDir, "out.mp4");
        var rgbaPath = Path.Combine(tmpDir, "out.rgba");
        File.WriteAllBytes(rawPath, nv12Bytes);

        RunFfmpeg($"-y -f rawvideo -pix_fmt nv12 -s {Out}x{Out} -i \"{rawPath}\" -c:v h264_nvenc -f mp4 \"{mp4Path}\"");
        RunFfmpeg($"-y -i \"{mp4Path}\" -pix_fmt rgba -f rawvideo \"{rgbaPath}\"");

        // 4. 对比 GPU RGBA vs 解码 RGBA
        var decoded = File.ReadAllBytes(rgbaPath);
        Assert.Equal(gpuRgba.Length, decoded.Length);
        double psnr = PsnrRgba(gpuRgba, decoded);
        Assert.True(psnr >= 30.0,
            $"yaw={yaw} pitch={pitch} NV12端到端PSNR={psnr:F2}dB < 30dB（NV12平面写入或下采样损坏）");

        try { Directory.Delete(tmpDir, true); } catch { }
    }

    private static void RunFfmpeg(string args)
    {
        var psi = new ProcessStartInfo("ffmpeg", args)
        { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var p = Process.Start(psi) ?? throw new InvalidOperationException("FFmpeg 启动失败");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg 退出码 {p.ExitCode}: {err}");
    }

    private static double PsnrRgba(byte[] a, byte[] b)
    {
        int n = a.Length / 4;
        double sq = 0;
        for (int i = 0; i < n; i++)
        {
            int dr = a[i * 4] - b[i * 4];
            int dg = a[i * 4 + 1] - b[i * 4 + 1];
            int db = a[i * 4 + 2] - b[i * 4 + 2];
            sq += (double)dr * dr + dg * dg + db * db;
        }
        double mse = sq / (n * 3);
        return mse < 1e-10 ? 100.0 : 10.0 * Math.Log10(255.0 * 255.0 / mse);
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "全景图转短视频工具-PRD.md")))
            d = d.Parent;
        return d!.FullName;
    }
}
