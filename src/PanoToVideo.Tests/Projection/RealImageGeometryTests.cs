using PanoToVideo.Core.Projection;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// 真实 6000x3000 ERP 图几何对照（阶段4补测，真实纹理质量验证）。
/// 输入：720/玄关1.jpg、720/茶室空间.jpg（原生 6000x3000，非缩放）。
/// GPU RenderFrameToRgba vs py360convert 参考帧，PSNR ≥ 40dB。
/// 区别于 GeometryMatrixTests（8192 诊断图），此为真实下载器产物。
/// </summary>
[Trait("Category", "GeometryReference")]
public class RealImageGeometryTests : IDisposable
{
    private static readonly ID3D11Device s_device;
    private static readonly EquirectPipeline s_pipeline;
    private const int Out = 320;

    static RealImageGeometryTests()
    {
        using var probe = new DeviceProbe();
        var preferred = probe.Probe().Preferred ?? throw new InvalidOperationException("无合格设备");
        D3D11CreateDevice(preferred.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out s_device, out _, out _).CheckError();
        s_pipeline = new EquirectPipeline(s_device);
    }

    public static IEnumerable<object[]> RealImageCases()
    {
        string[] images = ["xuanguan1", "chashi"];
        var yaws = new[] { 0, 90, 180, 270 };
        var pitches = new[] { -30, 0, 30 };
        var fovs = new[] { 45, 75, 100 };
        foreach (var img in images)
            foreach (var fov in fovs)
                foreach (var yaw in yaws)
                    foreach (var pitch in pitches)
                        yield return new object[] { img, fov, yaw, pitch };
    }

    [Theory]
    [MemberData(nameof(RealImageCases))]
    public void 真实图矩阵_GPU与py360convert_PSNR不低于40dB(string image, int fov, int yaw, int pitch)
    {
        var srv = UploadErp(image, out int erpW, out int erpH);
        var gpuRgba = s_pipeline.RenderFrameToRgba(srv, erpW, erpH, Out, Out, fov, yaw, pitch);
        var refRgb = File.ReadAllBytes(ReferencePath($"{image}_ref_fov{fov}_yaw{yaw}_pitch{pitch}.bin"));
        double psnr = Psnr(gpuRgba, refRgb);
        Assert.True(psnr >= 40.0,
            $"{image} fov={fov} yaw={yaw} pitch={pitch} PSNR={psnr:F2}dB < 40dB");
    }

    [Theory]
    [InlineData("xuanguan1", 359)]
    [InlineData("xuanguan1", 361)]
    [InlineData("chashi", 359)]
    [InlineData("chashi", 361)]
    public void 真实图接缝专项_无黑边断层(string image, int yaw)
    {
        var srv = UploadErp(image, out int erpW, out int erpH);
        var gpuRgba = s_pipeline.RenderFrameToRgba(srv, erpW, erpH, Out, Out, 75, yaw, 0);
        var refRgb = File.ReadAllBytes(ReferencePath($"{image}_seam_yaw{yaw}_pitch0.bin"));
        double psnr = Psnr(gpuRgba, refRgb);
        int nonZero = 0;
        for (int i = 0; i < Out * Out; i++)
            if (gpuRgba[i * 4] > 0 || gpuRgba[i * 4 + 1] > 0 || gpuRgba[i * 4 + 2] > 0) nonZero++;
        Assert.True(psnr >= 40.0, $"{image} 接缝 yaw={yaw} PSNR={psnr:F2}dB");
        Assert.True(nonZero > Out * Out * 0.95, $"{image} 接缝 yaw={yaw} 黑边（非零{nonZero}）");
    }

    // 缓存已上传的 SRV（每图上传一次）
    private static readonly Dictionary<string, (ID3D11ShaderResourceView srv, int w, int h)> _cache = new();
    private static ID3D11ShaderResourceView UploadErp(string image, out int w, out int h)
    {
        if (_cache.TryGetValue(image, out var v)) { w = v.w; h = v.h; return v.srv; }
        var root = FindRepoRoot();
        var rgb = File.ReadAllBytes(Path.Combine(root, "tests", "fixtures", $"erp_{image}_6000x3000.bin"));
        w = 6000; h = 3000;
        var rgba = new byte[rgb.Length / 3 * 4];
        for (int i = 0; i < rgb.Length / 3; i++)
        {
            rgba[i * 4] = rgb[i * 3]; rgba[i * 4 + 1] = rgb[i * 3 + 1];
            rgba[i * 4 + 2] = rgb[i * 3 + 2]; rgba[i * 4 + 3] = 255;
        }
        var srv = s_pipeline.UploadErpTexture(rgba, w, h);
        _cache[image] = (srv, w, h);
        return srv;
    }

    private static double Psnr(byte[] rgba, byte[] rgb)
    {
        int n = rgb.Length / 3;
        double sq = 0;
        for (int i = 0; i < n; i++)
        {
            int dr = rgba[i * 4] - rgb[i * 3];
            int dg = rgba[i * 4 + 1] - rgb[i * 3 + 1];
            int db = rgba[i * 4 + 2] - rgb[i * 3 + 2];
            sq += (double)dr * dr + dg * dg + db * db;
        }
        double mse = sq / (n * 3);
        return mse < 1e-10 ? 100.0 : 10.0 * Math.Log10(255.0 * 255.0 / mse);
    }

    private static string ReferencePath(string name) => Path.Combine(FindRepoRoot(), "tests", "reference", name);
    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "全景图转短视频工具-PRD.md")))
            d = d.Parent;
        return d!.FullName;
    }

    public void Dispose() { }
}
