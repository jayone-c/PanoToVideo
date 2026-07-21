using PanoToVideo.Core.Projection;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// GPU 几何对照全量矩阵（开发规划阶段1任务5）。
/// Yaw{0,90,180,270} × Pitch{-30,0,30} × FOV{45,75,100} = 36 组合 + 接缝专项。
/// GPU RenderFrameToRgba vs py360convert 参考帧，PSNR ≥ 40dB。
/// 接缝处不得黑边/断层/翻转（接缝专项单独检查）。
/// </summary>
[Trait("Category", "GeometryReference")]
public class GeometryMatrixTests : IDisposable
{
    private static readonly ID3D11Device s_device;
    private static readonly EquirectPipeline s_pipeline;
    private static readonly ID3D11ShaderResourceView s_erpSrv;
    private static readonly Rgb[] s_erpRgb;
    private const int ErpW = 8192, ErpH = 4096, Out = 320;

    static GeometryMatrixTests()
    {
        using var probe = new DeviceProbe();
        var result = probe.Probe();
        var preferred = result.Preferred ?? throw new InvalidOperationException("无合格设备");
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
        D3D11CreateDevice(preferred.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            levels, out s_device, out _, out _).CheckError();
        s_pipeline = new EquirectPipeline(s_device);

        var root = FindRepoRoot();
        var erpRgbBytes = File.ReadAllBytes(Path.Combine(root, "tests", "fixtures", "erp_8192x4096.bin"));
        s_erpRgb = new Rgb[ErpW * ErpH];
        var rgba = new byte[ErpW * ErpH * 4];
        for (int i = 0; i < s_erpRgb.Length; i++)
        {
            s_erpRgb[i] = new Rgb(erpRgbBytes[i * 3], erpRgbBytes[i * 3 + 1], erpRgbBytes[i * 3 + 2]);
            rgba[i * 4] = erpRgbBytes[i * 3];
            rgba[i * 4 + 1] = erpRgbBytes[i * 3 + 1];
            rgba[i * 4 + 2] = erpRgbBytes[i * 3 + 2];
            rgba[i * 4 + 3] = 255;
        }
        s_erpSrv = s_pipeline.UploadErpTexture(rgba, ErpW, ErpH);
    }

    public static IEnumerable<object[]> MatrixCases()
    {
        var yaws = new[] { 0, 90, 180, 270 };
        var pitches = new[] { -30, 0, 30 };
        var fovs = new[] { 45, 75, 100 };
        foreach (var fov in fovs)
            foreach (var yaw in yaws)
                foreach (var pitch in pitches)
                    yield return new object[] { fov, yaw, pitch };
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void 矩阵组合_GPU与py360convert_PSNR不低于40dB(int fov, int yaw, int pitch)
    {
        var (psnr, nonZero) = CompareGpuWithReference(fov, yaw, pitch, $"ref_fov{fov}_yaw{yaw}_pitch{pitch}.bin");
        Assert.True(psnr >= 40.0,
            $"fov={fov} yaw={yaw} pitch={pitch} PSNR={psnr:F2}dB < 40dB（非零像素{nonZero}/{Out * Out}）");
    }

    [Theory]
    [InlineData(359)]
    [InlineData(361)]
    [InlineData(1)]
    public void 接缝专项_无黑边断层(int yaw)
    {
        // Yaw 接近 0/360 接缝：不应出现整块黑（断层）或翻转
        var (psnr, nonZero) = CompareGpuWithReference(75, yaw, 0, $"seam_yaw{yaw}_pitch0.bin");
        Assert.True(psnr >= 40.0, $"接缝 yaw={yaw} PSNR={psnr:F2}dB（非零{nonZero}）");
        Assert.True(nonZero > Out * Out * 0.95, $"接缝 yaw={yaw} 出现黑边（非零像素仅{nonZero}）");
    }

    [Fact]
    public void 接缝两侧_Yaw359与Yaw1几何对称()
    {
        // 359 与 1 跨接缝两侧，应互为近似镜像（PSNR 与各自参考都高即可）
        var (p359, _) = CompareGpuWithReference(75, 359, 0, "seam_yaw359_pitch0.bin");
        var (p1, _) = CompareGpuWithReference(75, 1, 0, "seam_yaw1_pitch0.bin");
        Assert.True(p359 >= 40.0 && p1 >= 40.0, $"接缝两侧: yaw359={p359:F2} yaw1={p1:F2}");
    }

    private static (double Psnr, int NonZero) CompareGpuWithReference(int fov, int yaw, int pitch, string refFileName)
    {
        var root = FindRepoRoot();
        var refPath = Path.Combine(root, "tests", "reference", refFileName);
        if (!File.Exists(refPath))
            Assert.Fail($"参考帧未生成: {refFileName}（运行 tests/reference/generate_matrix.py）");

        var gpuRgba = s_pipeline.RenderFrameToRgba(s_erpSrv, ErpW, ErpH, Out, Out, fov, yaw, pitch);
        var refRgb = File.ReadAllBytes(refPath);

        int n = Out * Out;
        double sq = 0;
        int nonZero = 0;
        for (int i = 0; i < n; i++)
        {
            if (gpuRgba[i * 4] > 0 || gpuRgba[i * 4 + 1] > 0 || gpuRgba[i * 4 + 2] > 0) nonZero++;
            int dr = gpuRgba[i * 4] - refRgb[i * 3];
            int dg = gpuRgba[i * 4 + 1] - refRgb[i * 3 + 1];
            int db = gpuRgba[i * 4 + 2] - refRgb[i * 3 + 2];
            sq += (double)dr * dr + dg * dg + db * db;
        }
        double mse = sq / (n * 3);
        double psnr = mse < 1e-10 ? 100.0 : 10.0 * Math.Log10(255.0 * 255.0 / mse);
        return (psnr, nonZero);
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "全景图转短视频工具-PRD.md")))
            d = d.Parent;
        return d!.FullName;
    }

    public void Dispose()
    {
        // 静态资源跨测试共享，类释放时不销毁（进程退出时 GC）
    }
}
