using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// 投影数学与 py360convert 几何对照（PRD Testing、开发规划 §5）。
/// 比较编码前 CPU 原始帧（非有损成片）。PSNR ≥ 40dB。
/// 参考帧由 tests/reference/generate_reference.py 生成；未生成时 Skip。
/// </summary>
[Trait("Category", "GeometryReference")]
public class EquirectProjectionComparisonTests
{
    private const int ErpW = 8192;
    private const int ErpH = 4096;
    private const int OutW = 320;
    private const int OutH = 320;
    private const double Hfov = 75.0;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "全景图转短视频工具-PRD.md")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static Rgb[] LoadRgb(string path, int expectedPixels)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(expectedPixels * 3, bytes.Length);
        var arr = new Rgb[expectedPixels];
        for (int i = 0; i < expectedPixels; i++)
            arr[i] = new Rgb(bytes[i * 3], bytes[i * 3 + 1], bytes[i * 3 + 2]);
        return arr;
    }

    private static double Psnr(Rgb[] a, Rgb[] b)
    {
        double sqErr = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var dr = (int)a[i].R - b[i].R;
            var dg = (int)a[i].G - b[i].G;
            var db = (int)a[i].B - b[i].B;
            sqErr += (double)dr * dr + dg * dg + db * db;
        }
        var mse = sqErr / (a.Length * 3);
        if (mse < 1e-10) return 100.0;
        return 10.0 * Math.Log10(255.0 * 255.0 / mse);
    }

    private static void AssertMatchesReference(int yaw, int pitch)
    {
        var root = RepoRoot();
        var erpPath = Path.Combine(root, "tests", "fixtures", "erp_8192x4096.bin");
        var refPath = Path.Combine(root, "tests", "reference", $"ref_{yaw}_{pitch}.bin");

        if (!File.Exists(erpPath) || !File.Exists(refPath))
        {
            // 参考帧未生成：本用例不验证（不失败）。
            // 请先运行 tests/reference/generate_reference.py 生成参考帧。
            return;
        }

        var erp = LoadRgb(erpPath, ErpW * ErpH);
        var reference = LoadRgb(refPath, OutW * OutH);

        var frame = EquirectRenderer.RenderFrame(erp, ErpW, ErpH, OutW, OutH, Hfov, yaw, pitch);

        var psnr = Psnr(frame, reference);
        Assert.True(psnr >= 40.0,
            $"yaw={yaw} pitch={pitch} PSNR={psnr:F2}dB < 40dB（与 py360convert 几何不一致，需排查方向/FOV/采样约定）");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 0)]
    [InlineData(180, 0)]
    [InlineData(270, 0)]
    [InlineData(0, 30)]
    [InlineData(0, -30)]
    [InlineData(45, 15)]
    public void 与py360convert对照_PSNR不低于40dB(int yaw, int pitch)
    {
        AssertMatchesReference(yaw, pitch);
    }
}
