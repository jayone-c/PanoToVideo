using PanoToVideo.Core.Projection;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using PanoToVideo.Render.Encoding;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Format = Vortice.DXGI.Format;
using static Vortice.Direct3D11.D3D11;

// ===== 阶段0 探针 · GPU shader 几何验证 =====
// 验证：D3D11 设备 + ERP 上传 + equirect->perspective shader 渲染单帧，
//       与 py360convert 参考帧（PSNR ≥ 40dB）及 Core EquirectRenderer 双重对照。
// 结论：GPU shader 几何全部通过（PSNR 49-54dB）。

Console.WriteLine("=== 阶段1 · DeviceProbe 真实化验证 (ADR Q4) ===");

// ADR Q4: DeviceProbe 对每个非软件适配器做 MF 编码器激活探测，区分真实 GPU 与虚拟镜像适配器
using var deviceProbe = new DeviceProbe();
var probeResult = deviceProbe.Probe();
Console.WriteLine($"合格设备数: {probeResult.Eligible.Count}");
foreach (var e in probeResult.Eligible)
    Console.WriteLine($"  - {e.Candidate.Description}  VRAM={e.Candidate.DedicatedVideoMemoryBytes / 1024 / 1024}MB  Luid=0x{e.Candidate.Luid:X16}  编码器={e.EncoderName ?? "?"}");

if (probeResult.Preferred == null)
    throw new InvalidOperationException("DeviceProbe 未找到合格设备");
var preferred = probeResult.Preferred;
Console.WriteLine($"\n首选设备: {preferred.Candidate.Description}  Luid=0x{preferred.Candidate.Luid:X16}  编码器={preferred.EncoderName}");

// 用首选适配器创建 D3D11 设备
FeatureLevel[] featureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
D3D11CreateDevice(preferred.Adapter, DriverType.Unknown,
    DeviceCreationFlags.BgraSupport, featureLevels,
    out ID3D11Device device, out FeatureLevel featureLevel, out ID3D11DeviceContext context).CheckError();
Console.WriteLine($"D3D11 设备创建成功，FeatureLevel={featureLevel}");

// ADR Q1 验证：MFCreateDXGIDeviceManager 能否挂 D3D11 设备（零拷贝编码前提）
var mfMount = MfDeviceProbe.ProbeDeviceMount(device);
Console.WriteLine($"\n[ADR Q1] MF 设备挂载: {(mfMount.CanMountDevice ? "成功" : "失败")}  Token={mfMount.ResetToken}  {mfMount.Error ?? ""}");

// ADR Q1 设备探测：枚举 H.264 硬件编码器（验证 NVENC 可用）
var encoderCount = MfDeviceProbe.EnumerateH264HardwareEncoders();
Console.WriteLine($"[ADR Q1] H.264 硬件编码器数量: {encoderCount}");

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

    // ADR Q3 验证：GPU BGRA->NV12/Rec.709 limited 颜色转换
    // 4 色块测试纹理（白/红/绿/蓝），期望 BT.709 limited Y/Cb/Cr
    var testRgba = new byte[] { 255,255,255,255,  255,0,0,255,  0,255,0,255,  0,0,255,255 };
    using var testSrv = pipeline.UploadErpTexture(testRgba, 4, 1);
    var (yPlane, uvPlane) = pipeline.ConvertBgraToYuv(testSrv, 4, 1);
    int[][] exp = { new[]{235,128,128}, new[]{63,102,240}, new[]{173,42,26}, new[]{32,240,118} };
    string[] names = { "白", "红", "绿", "蓝" };
    Console.WriteLine("\n[ADR Q3] BGRA->NV12 Rec.709 limited 颜色转换:");
    bool q3Pass = true;
    for (int i = 0; i < 4; i++)
    {
        int Y = yPlane[i], Cb = uvPlane[i * 2], Cr = uvPlane[i * 2 + 1];
        bool ok = Math.Abs(Y - exp[i][0]) <= 2 && Math.Abs(Cb - exp[i][1]) <= 2 && Math.Abs(Cr - exp[i][2]) <= 2;
        q3Pass &= ok;
        Console.WriteLine($"  {names[i]}: Y={Y}(期{exp[i][0]}) Cb={Cb}(期{exp[i][1]}) Cr={Cr}(期{exp[i][2]}) {(ok ? "OK" : "FAIL")}");
    }
    Console.WriteLine($"[ADR Q3] 颜色转换: {(q3Pass ? "通过" : "失败")}");

    // ADR Q2 前提验证：NV12 平面纹理 RTV（Y R8 + UV R8G8 plane）+ MFCreateDXGISurfaceBuffer 零拷贝 buffer
    Console.WriteLine("\n[ADR Q2] NV12 平面纹理 RTV + 零拷贝 buffer:");
    var nv12Desc = new Texture2DDescription
    {
        Width = 8, Height = 4, MipLevels = 1, ArraySize = 1,
        Format = Format.NV12, SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget,
    };
    using var nv12Tex = device.CreateTexture2D(nv12Desc);
    using var yRtv2 = device.CreateRenderTargetView(nv12Tex, new RenderTargetViewDescription
    {
        Format = Format.R8_UNorm, ViewDimension = RenderTargetViewDimension.Texture2D,
        Texture2D = new Texture2DRenderTargetView { MipSlice = 0 },
    });
    using var uvRtv2 = device.CreateRenderTargetView(nv12Tex, new RenderTargetViewDescription
    {
        Format = Format.R8G8_UNorm, ViewDimension = RenderTargetViewDimension.Texture2D,
        Texture2D = new Texture2DRenderTargetView { MipSlice = 0 },
    });
    context.ClearRenderTargetView(yRtv2, new Color4(235f / 255f, 0, 0, 1));
    context.ClearRenderTargetView(uvRtv2, new Color4(128f / 255f, 128f / 255f, 0, 1));
    var nv12StagingDesc = nv12Desc;
    nv12StagingDesc.Usage = ResourceUsage.Staging;
    nv12StagingDesc.BindFlags = BindFlags.None;
    nv12StagingDesc.CPUAccessFlags = CpuAccessFlags.Read;
    using var nv12Staging = device.CreateTexture2D(nv12StagingDesc);
    context.CopyResource(nv12Staging, nv12Tex);
    var m2 = context.Map(nv12Staging, 0, MapMode.Read);
    try
    {
        int yPitch = (int)m2.RowPitch;
        int yVal = Marshal.ReadByte(m2.DataPointer, 0);
        int uvCb = Marshal.ReadByte(m2.DataPointer, 4 * yPitch);
        int uvCr = Marshal.ReadByte(m2.DataPointer, 4 * yPitch + 1);
        bool nv12Ok = yVal == 235 && uvCb == 128 && uvCr == 128;
        Console.WriteLine($"  NV12平面RTV: Y[0]={yVal}(期235) UV.Cb={uvCb}(期128) UV.Cr={uvCr}(期128) {(nv12Ok ? "OK" : "FAIL")}");
    }
    finally { context.Unmap(nv12Staging, 0); }
    var sbuf = MfDeviceProbe.ProbeSurfaceBuffer(nv12Tex);
    Console.WriteLine($"  MFCreateDXGISurfaceBuffer零拷贝: {(sbuf.Success ? "成功" : "失败")} {sbuf.Error ?? ""}");

    // ADR Q2 端到端：渲染多帧 -> NV12 -> 零拷贝编码 MP4
    Console.WriteLine("\n[ADR Q2] 端到端零拷贝编码:");
    string mp4Path = Path.Combine(repoRoot, "smoke_output.mp4");
    if (File.Exists(mp4Path)) File.Delete(mp4Path);
    const int encW = 320, encH = 320, encFps = 30, encFrames = 30; // 1秒
    uint encBitrate = 4_000_000;
    try
    {
        using var encoder = new MfH264Encoder(device, mp4Path, encW, encH, encFps, encBitrate);
        for (int i = 0; i < encFrames; i++)
        {
            double yaw = 360.0 * i / encFrames;
            using var nv12Frame = pipeline.RenderFrameToNv12(srv, erpW, erpH, encW, encH, 75, yaw, 0);
            encoder.SubmitFrame(nv12Frame, i);
        }
        encoder.Finalize();
        var fi = new FileInfo(mp4Path);
        Console.WriteLine($"  编码完成: {encFrames}帧 -> {mp4Path} ({fi.Length} 字节)");
        Console.WriteLine($"  Q2端到端: {(fi.Length > 0 ? "成功" : "失败(空文件)")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Q2端到端失败: {ex.GetType().Name}: {ex.Message}");
    }

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
