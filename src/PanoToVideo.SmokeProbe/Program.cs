using PanoToVideo.Core.Projection;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

// ===== 阶段0 探针 · GPU shader 几何验证 =====
// 验证：D3D11 设备 + ERP 上传 + equirect->perspective shader 渲染单帧，
//       与 py360convert 参考帧（PSNR ≥ 40dB）及 Core EquirectRenderer 双重对照。
// 结论：GPU shader 几何全部通过（PSNR 49-54dB）。

Console.WriteLine("=== 阶段0 探针 · GPU shader 几何验证 ===");

// 1. 枚举适配器并选 NVIDIA 4090D
var adapters = DxgiDeviceEnumerator.Enumerate();
Console.WriteLine("DXGI 适配器:");
foreach (var (_, c) in adapters)
    Console.WriteLine($"  - {c.Description}  VRAM={c.DedicatedVideoMemoryBytes / 1024 / 1024}MB  Software={c.IsSoftware}  Luid=0x{c.Luid:X16}");

var selected = adapters.FirstOrDefault(x => x.Candidate.Description.Contains("NVIDIA"));
if (selected.Adapter == null)
    throw new InvalidOperationException("未找到 NVIDIA 适配器");
Console.WriteLine($"\n选定渲染设备: {selected.Candidate.Description}  Luid=0x{selected.Candidate.Luid:X16}");

// 2. 创建 D3D11 设备（绑定到选定适配器）
FeatureLevel[] featureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
D3D11CreateDevice(selected.Adapter.QueryInterface<IDXGIAdapter>(), DriverType.Unknown,
    DeviceCreationFlags.BgraSupport, featureLevels,
    out ID3D11Device device, out FeatureLevel featureLevel, out ID3D11DeviceContext context).CheckError();
Console.WriteLine($"D3D11 设备创建成功，FeatureLevel={featureLevel}");

using (selected.Adapter)
using (device)
using (context)
{
    // 3. 读 ERP .bin (RGB 8192×4096) -> 补 alpha -> RGBA
    string repoRoot = FindRepoRoot();
    var erpRgb = File.ReadAllBytes(Path.Combine(repoRoot, "tests", "fixtures", "erp_8192x4096.bin"));
    const int erpW = 8192, erpH = 4096;
    var erpRgba = new byte[erpW * erpH * 4];
    for (int i = 0; i < erpW * erpH; i++)
    {
        erpRgba[i * 4] = erpRgb[i * 3];
        erpRgba[i * 4 + 1] = erpRgb[i * 3 + 1];
        erpRgba[i * 4 + 2] = erpRgb[i * 3 + 2];
        erpRgba[i * 4 + 3] = 255;
    }

    using var pipeline = new EquirectPipeline(device);
    using var srv = pipeline.UploadErpTexture(erpRgba, erpW, erpH);

    // 4. 多视角渲染并与 py360convert + Core 双重对照
    const int ow = 320, oh = 320;
    var cases = new[] { (0, 0), (90, 0), (180, 0), (270, 0), (0, 30), (0, -30), (45, 15) };

    var erpRgbStruct = new Rgb[erpW * erpH];
    for (int i = 0; i < erpRgbStruct.Length; i++)
        erpRgbStruct[i] = new Rgb(erpRgb[i * 3], erpRgb[i * 3 + 1], erpRgb[i * 3 + 2]);

    Console.WriteLine("\n视角      GPU-vs-py360convert    GPU-vs-Core");
    Console.WriteLine(new string('-', 50));
    bool allPass = true;
    foreach (var (yaw, pitch) in cases)
    {
        var gpuRgba = pipeline.RenderFrameToRgba(srv, erpW, erpH, ow, oh, 75, yaw, pitch);
        var refRgb = File.ReadAllBytes(Path.Combine(repoRoot, "tests", "reference", $"ref_{yaw}_{pitch}.bin"));
        double psnrRef = PsnrRgb(gpuRgba, refRgb);

        var coreFrame = EquirectRenderer.RenderFrame(erpRgbStruct, erpW, erpH, ow, oh, 75, yaw, pitch);
        var coreRgb = new byte[ow * oh * 3];
        for (int i = 0; i < coreFrame.Length; i++)
        {
            coreRgb[i * 3] = coreFrame[i].R;
            coreRgb[i * 3 + 1] = coreFrame[i].G;
            coreRgb[i * 3 + 2] = coreFrame[i].B;
        }
        double psnrCore = PsnrRgb(gpuRgba, coreRgb);

        bool pass = psnrRef >= 40.0;
        allPass &= pass;
        Console.WriteLine($"yaw={yaw,3} p={pitch,3}   {psnrRef,8:F2} dB         {psnrCore,8:F2} dB   {(pass ? "PASS" : "FAIL")}");
    }

    Console.WriteLine($"\n结论: {(allPass ? "GPU shader 几何全部通过 (PSNR ≥ 40dB vs py360convert)" : "存在未通过视角，需排查")}");
}

static double PsnrRgb(byte[] rgba, byte[] rgb)
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

static string FindRepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "全景图转短视频工具-PRD.md")))
        d = d.Parent;
    return d!.FullName;
}
