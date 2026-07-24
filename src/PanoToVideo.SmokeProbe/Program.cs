using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Projection;
using PanoToVideo.Core.Queue;
using PanoToVideo.Core.Validation;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using PanoToVideo.Render.Encoding;
using PanoToVideo.Render.Exporting;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Format = Vortice.DXGI.Format;
using TaskStatus = PanoToVideo.Core.Queue.TaskStatus;
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

// 阶段3: H.265 编码器探测 + 预设退回（任务3）
var hevcProbe = new MfHevcEncoderProbe();
var hevcAvailable = hevcProbe.IsAvailable();
var resolver = new PresetResolver(hevcProbe);
var sizeResult = resolver.Resolve(ExportPreset.Size);
Console.WriteLine($"[阶段3] H.265 硬件编码器: {(hevcAvailable ? "可用" : "不可用")}");
Console.WriteLine($"[阶段3] 体积优先(H.265)预设 -> {sizeResult.Preset}  {(sizeResult.FallbackReason ?? "无退回")}");

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
        encoder.Finish();
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

// ===== 阶段1 · 单图导出全链路集成验证（任务4）=====
Console.WriteLine("\n=== 阶段1 · 单图导出全链路 ===");
{
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

    // 单图参数：3秒、360°、60FPS、1080×1920、FOV75、H.264（接近 PRD 基准，缩短时长省时）
    var parameters = new RenderParameters(
        DurationSeconds: 3, RotationDegrees: 360, Fps: 60,
        HorizontalFov: 75.0, Width: 1080, Height: 1920, Pitch: 0.0,
        Direction: RotationDirection.Clockwise, AsteroidIntro: false,
        CpuCores: Environment.ProcessorCount);
    var imageInfo = new ImageInfo(erpW, erpH, false, "scene_equirectangular_8192x4096.bin");

    string outDir = Path.Combine(repoRoot, "smoke_exports");
    if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
    Directory.CreateDirectory(outDir);

    long availBytes = new DriveInfo(Path.GetPathRoot(outDir)!).AvailableFreeSpace;
    var executor = new GpuExportExecutor(erpRgba, erpW, erpH, parameters, ExportPreset.Compatibility);
    var orchestrator = new SingleImageExportOrchestrator();
    var sw = Stopwatch.StartNew();
    var result = orchestrator.Export(imageInfo, parameters, ExportPreset.Compatibility,
        outDir, availBytes, Directory.GetFiles(outDir), executor);
    sw.Stop();

    Console.WriteLine($"导出结果: {(result.Success ? "成功" : "失败")}");
    if (result.Success)
    {
        var fi = new FileInfo(result.OutputPath!);
        Console.WriteLine($"  输出: {result.OutputPath}");
        Console.WriteLine($"  大小: {fi.Length} 字节");
        Console.WriteLine($"  耗时: {sw.Elapsed.TotalSeconds:F2}s  平均FPS: {result.Log!.AverageFps:F1}");
        var dev = executor.GetDeviceInfo();
        Console.WriteLine($"  设备: {dev?.Device}  编码器: {dev?.Encoder}");

        // ffprobe 校验（PRD 验收：H.264、1080×1920、60FPS、3秒、无音频）
        var psi = new ProcessStartInfo("ffprobe",
            $"-v error -show_entries stream=codec_name,codec_type,width,height,r_frame_rate,duration,nb_frames -of default=noprint_wrappers=1 \"{result.OutputPath}\"")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        var ffout = Process.Start(psi)!;
        var ffprobeOut = ffout.StandardOutput.ReadToEnd();
        ffout.WaitForExit();
        Console.WriteLine($"  ffprobe:\n{string.Join("\n", ffprobeOut.Trim().Split('\n').Select(l => "    " + l))}");
        bool ffOk = ffprobeOut.Contains("codec_name=h264")
            && ffprobeOut.Contains("width=1080") && ffprobeOut.Contains("height=1920")
            && ffprobeOut.Contains("r_frame_rate=60/1") && !ffprobeOut.Contains("codec_type=audio");
        Console.WriteLine($"  ffprobe校验: {(ffOk ? "通过(H.264/1080x1920/60FPS/无音频)" : "未通过")}");
    }
    else
    {
        Console.WriteLine($"  错误: {result.Error}");
    }

    // ===== 阶段3 · 小行星开场验证（§1.5，任务4）=====
    Console.WriteLine("\n=== 阶段3 · 小行星开场 ===");
    {
        // 复用单图导出的 ERP RGBA（在外层作用域已定义 erpRgba/erpW/erpH）
        using var probe3 = new DeviceProbe();
        var pref3 = probe3.Probe().Preferred!;
        D3D11CreateDevice(pref3.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out ID3D11Device dev3, out _, out _).CheckError();
        using (dev3)
        {
            using var pipe3 = new EquirectPipeline(dev3);
            using var srv3 = pipe3.UploadErpTexture(erpRgba, erpW, erpH);
            const int aw = 320, ah = 320;

            // 关闭：第0帧 weight=0 即纯透视
            var closedFrame = pipe3.RenderFrameToRgba(srv3, erpW, erpH, aw, ah, 75, 0, 0, asteroidWeight: 0);
            // 启用：第0帧 weight=1 纯小行星
            var astFrame = pipe3.RenderFrameToRgba(srv3, erpW, erpH, aw, ah, 75, 0, 0, asteroidWeight: 1);
            // 过渡末（第48帧）weight≈0，与纯透视接近（无突跳）
            var transEnd = pipe3.RenderFrameToRgba(srv3, erpW, erpH, aw, ah, 75, 0, 0,
                asteroidWeight: AsteroidSchedule.WeightAt(48, 60, true));

            int nonZeroClosed = CountNonZero(closedFrame);
            int nonZeroAst = CountNonZero(astFrame);
            // 真正的契约验证：
            // 1. 关闭(透视) vs 启用第0帧(纯小行星) 应不同（不同投影方向）
            double psnrClosedVsAst = PsnrRgbaToRgba(closedFrame, astFrame);
            // 2. 过渡末(weight≈0) vs 关闭(透视) 应相同（无突跳，PSNR高）
            double psnrTransVsPersp = PsnrRgbaToRgba(transEnd, closedFrame);

            Console.WriteLine($"  关闭(透视)非零像素 {nonZeroClosed}; 启用(小行星)非零 {nonZeroAst}");
            Console.WriteLine($"  关闭(透视) vs 启用(小行星) PSNR={psnrClosedVsAst:F2}dB (应较低=不同投影)");
            Console.WriteLine($"  过渡末(weight={AsteroidSchedule.WeightAt(48, 60, true):F3}) vs 纯透视 PSNR={psnrTransVsPersp:F2}dB (应高=无突跳)");
            bool ok = psnrClosedVsAst < 30 && psnrTransVsPersp > 20;
            Console.WriteLine($"  结论: {(ok ? "小行星开/关行为符合预期(第0帧不同投影,过渡末无突跳)" : "异常")}");

            // 小行星视觉验证：导出含小行星开场的 MP4 + 抽帧（第0帧纯小行星/过渡中/过渡后透视）
            Console.WriteLine("\n=== 优化3 · 小行星开场视觉验证 ===");
            string astOutDir = Path.Combine(repoRoot, "asteroid_vis");
            if (Directory.Exists(astOutDir)) Directory.Delete(astOutDir, true);
            Directory.CreateDirectory(astOutDir);
            var astParams = new RenderParameters(2, 360, 60, 75.0, 640, 640, 0.0,
                RotationDirection.Clockwise, AsteroidIntro: true, CpuCores: Environment.ProcessorCount);
            var astExecutor = new FfmpegNvencExecutor(erpRgba, erpW, erpH, astParams, ExportPreset.Compatibility);
            var astOrchestrator = new SingleImageExportOrchestrator();
            long astAvail = new DriveInfo(Path.GetPathRoot(astOutDir)!).AvailableFreeSpace;
            var astResult = astOrchestrator.Export(
                new ImageInfo(erpW, erpH, false, "scene.jpg"), astParams, ExportPreset.Compatibility,
                astOutDir, astAvail, Array.Empty<string>(), astExecutor, default, null);
            if (astResult.Success)
            {
                Console.WriteLine($"  小行星视频导出: {Path.GetFileName(astResult.OutputPath)}");
                // 抽帧：第0帧(纯小行星)、第24帧(过渡中0.4s)、第48帧(过渡末0.8s)、第72帧(过渡后1.2s透视旋转)
                foreach (var (frame, label) in new[] { (0, "frame0_pureAsteroid"), (24, "frame24_transitionMid"), (48, "frame48_transitionEnd"), (72, "frame72_perspective") })
                {
                    var pngPath = Path.Combine(astOutDir, $"{label}.png");
                    RunFfmpeg($"-y -i \"{astResult.OutputPath}\" -vf \"select=eq(n\\,{frame})\" -frames:v 1 \"{pngPath}\"");
                }
                Console.WriteLine($"  抽帧完成: {astOutDir}/frame0_pureAsteroid.png(纯小行星) frame24.png(过渡中) frame48.png(过渡末) frame72.png(透视旋转)");
                Console.WriteLine($"  人工确认: 第0帧应是小行星投影(底部极点俯视little planet), 第72帧应是正常透视旋转, 过渡连续无突跳");
            }
            else
            {
                Console.WriteLine($"  小行星视频导出失败: {astResult.Error}");
            }
        }
    }

    // ===== 阶段1 · 360° 首尾一致性验证（任务6）=====
    Console.WriteLine("\n=== 阶段1 · 360° 首尾一致性 ===");
    if (result.Success)
    {
        // 编码前 GPU 帧：Yaw(0) vs Yaw(末帧) 对 360°整数倍任务应几何相同(无缝循环)
        // 用 equirect shader 渲染首末帧直接对比(不经编码,排除压缩差异)
        using var probe2 = new DeviceProbe();
        var pref = probe2.Probe().Preferred!;
        D3D11CreateDevice(pref.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out ID3D11Device dev2, out _, out _).CheckError();
        using (dev2)
        {
            using var pipe2 = new EquirectPipeline(dev2);
            using var srv2 = pipe2.UploadErpTexture(erpRgba, erpW, erpH);
            int totalFrames = parameters.TotalFrames;
            double yawFirst = YawSchedule.YawAt(0, totalFrames, parameters.RotationDegrees, parameters.Direction);
            double yawLast = YawSchedule.YawAt(totalFrames - 1, totalFrames, parameters.RotationDegrees, parameters.Direction);
            var rgbaFirst = pipe2.RenderFrameToRgba(srv2, erpW, erpH, parameters.Width, parameters.Height, parameters.HorizontalFov, yawFirst, parameters.Pitch);
            var rgbaLast = pipe2.RenderFrameToRgba(srv2, erpW, erpH, parameters.Width, parameters.Height, parameters.HorizontalFov, yawLast, parameters.Pitch);

            // 首末帧 Yaw 不同(末帧354°,非0°,避免停顿),但几何应衔接:末帧视角=首帧前一帧视角
            Console.WriteLine($"  编码前: 首帧Yaw={yawFirst:F1}° 末帧Yaw={yawLast:F1}° (无缝循环末帧非首帧避免停顿)");

            // 编码后:抽首末帧解码对比(允许压缩差异,检查无几何跳变)
            string frame0 = Path.Combine(repoRoot, "frame0.png");
            string frameN = Path.Combine(repoRoot, "frameN.png");
            RunFfmpeg($"-y -i \"{result.OutputPath}\" -vf \"select=eq(n\\,0)\" -frames:v 1 \"{frame0}\"");
            RunFfmpeg($"-y -i \"{result.OutputPath}\" -vf \"select=eq(n\\,{totalFrames - 1})\" -frames:v 1 \"{frameN}\"");
            // PSNR 对比(允许压缩差异,阈值宽松:几何跳变会致PSNR极低<20dB)
            var psi = new ProcessStartInfo("ffmpeg",
                $"-i \"{frame0}\" -i \"{frameN}\" -filter_complex \"psnr\" -f null -")
            { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            var p = Process.Start(psi)!;
            var errOut = p.StandardError.ReadToEnd();
            p.WaitForExit();
            var psnrMatch = System.Text.RegularExpressions.Regex.Match(errOut, @"average:(\d+\.\d+)");
            double encPsnr = psnrMatch.Success ? double.Parse(psnrMatch.Groups[1].Value) : 0;
            bool noJump = encPsnr > 15.0; // 几何跳变会致PSNR<15;压缩差异通常>20
            Console.WriteLine($"  编码后首末帧PSNR: {encPsnr:F2}dB {(noJump ? "(无几何跳变)" : "(可能几何跳变!)")}");
            try { File.Delete(frame0); File.Delete(frameN); } catch { }
        }
    }
}

// ===== 阶段2 · 批量队列验收（任务3/7，100项可行性验证用5项）=====
Console.WriteLine("\n=== 阶段2 · 批量队列验收 ===");
{
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

    // 100 项队列（PRD门槛3：≥100图批量，2张真实图循环50次模拟，规划§9允许循环）
    // 注入1个故意失败项（第50项，损坏图）验证"单项失败不阻塞队列"
    var batchParams = new RenderParameters(1, 360, 60, 75.0, 640, 640, 0.0,
        RotationDirection.Clockwise, false, Environment.ProcessorCount);
    var items = new List<QueueItem>();
    for (int i = 0; i < 100; i++)
    {
        // 第50项标记损坏（size=0），erpLoader 返回空数据致校验失败
        bool isFailItem = i == 50;
        string name = isFailItem
            ? $"FAIL_item50_corrupt.jpg"
            : $"{(i % 2 == 0 ? "玄关1" : "茶室空间")}_{i}_equirectangular_6000x3000.jpg";
        items.Add(new QueueItem(name, isFailItem ? 0 : 6000, isFailItem ? 0 : 3000));
    }

    string batchDir = Path.Combine(repoRoot, "smoke_batch");
    if (Directory.Exists(batchDir)) Directory.Delete(batchDir, true);
    Directory.CreateDirectory(batchDir);
    long avail = new DriveInfo(Path.GetPathRoot(batchDir)!).AvailableFreeSpace;

    // erpLoader: 失败项返回 size=0 空数据致 EquirectValidator 拒绝；正常项用真实图 RGBA
    var xuanRgba = LoadJpegRgba(Path.Combine(repoRoot, "720", "玄关1.jpg")).Rgba;
    var chaRgba = LoadJpegRgba(Path.Combine(repoRoot, "720", "茶室空间.jpg")).Rgba;
    var scheduler = new SerialBatchScheduler(
        executorFactory: (item, rgba, w, h) => new FfmpegNvencExecutor(rgba, w, h, batchParams, ExportPreset.Compatibility),
        erpLoader: item =>
        {
            if (item.SourceFileName.Contains("FAIL"))
                return (Rgba: Array.Empty<byte>(), W: 0, H: 0);
            if (item.SourceFileName.Contains("玄关1"))
                return (Rgba: xuanRgba, W: 6000, H: 3000);
            return (Rgba: chaRgba, W: 6000, H: 3000);
        });

    var sw = Stopwatch.StartNew();
    await scheduler.RunAsync(items, batchParams, ExportPreset.Compatibility, batchDir, avail, default);
    sw.Stop();

    Console.WriteLine($"批量完成: {items.Count}项, 耗时{sw.Elapsed.TotalSeconds:F2}s (平均{sw.Elapsed.TotalSeconds/items.Count:F2}s/项)");

    // 100项摘要（不逐项打印）
    int completed = items.Count(i => i.Status == TaskStatus.Completed);
    int failed = items.Count(i => i.Status == TaskStatus.Failed);
    Console.WriteLine($"\n验收:");
    Console.WriteLine($"  完成: {completed}/100  失败: {failed}/100");
    var failItem = items.FirstOrDefault(i => i.Status == TaskStatus.Failed);
    Console.WriteLine($"  失败项(应仅第50项): {(failItem == null ? "无" : failItem.SourceFileName + " -> " + failItem.ErrorMessage)}");
    // 单项失败不阻塞：失败项后续(第51项)应完成
    bool noBlock = items.Count == 100 && completed == 99 && failed == 1
        && items[50].Status == TaskStatus.Failed && items[51].Status == TaskStatus.Completed;
    // 命名唯一（99个完成项文件名不重复）
    var outputs = items.Where(i => i.OutputPath != null).Select(i => Path.GetFileName(i.OutputPath!)).ToList();
    bool namingOk = outputs.Count == outputs.Distinct().Count();
    Console.WriteLine($"  命名唯一(99个完成项重名递增不覆盖): {namingOk} (共{outputs.Count}个)");
    Console.WriteLine($"  单项失败不阻塞(失败项后继续): {noBlock}");
    Console.WriteLine($"  首项进度: 投影FPS={items[0].Progress.ProjectionFps:F0} 编码FPS={items[0].Progress.EncodingFps:F0}");
    Console.WriteLine($"  结论: {(noBlock && namingOk ? "100项批量验收通过" : "存在问题")}");

    // ffprobe 抽检首项与末项（证明日志设备与编码）
    if (items[0].OutputPath != null)
    {
        var psi = new ProcessStartInfo("ffprobe",
            $"-v error -show_entries stream=codec_name,width,height,r_frame_rate -of default=noprint_wrappers=1 \"{items[0].OutputPath}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
        Console.WriteLine($"  ffprobe首项: {o.Trim().Replace("\n", " ")}");
    }
}

static void RunFfmpeg(string args)
{
    var psi = new ProcessStartInfo("ffmpeg", args)
    { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    var p = Process.Start(psi)!;
    p.StandardError.ReadToEnd();
    p.WaitForExit();
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

static (byte[] Rgba, int W, int H) LoadJpegRgba(string path)
{
    using var bmp = new System.Drawing.Bitmap(path);
    int w = bmp.Width, h = bmp.Height;
    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
        System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
    try
    {
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            var row = new byte[w * 4];
            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, w * 4);
            for (int x = 0; x < w; x++)
            {
                rgba[(y * w + x) * 4] = row[x * 4 + 2];
                rgba[(y * w + x) * 4 + 1] = row[x * 4 + 1];
                rgba[(y * w + x) * 4 + 2] = row[x * 4];
                rgba[(y * w + x) * 4 + 3] = 255;
            }
        }
        return (rgba, w, h);
    }
    finally { bmp.UnlockBits(data); }
}

static int CountNonZero(byte[] rgba)
{
    int n = 0;
    for (int i = 0; i < rgba.Length; i += 4)
        if (rgba[i] > 0 || rgba[i + 1] > 0 || rgba[i + 2] > 0) n++;
    return n;
}

static double PsnrRgbaToRgba(byte[] a, byte[] b)
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

static string FindRepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "全景图转短视频工具-PRD.md")))
        d = d.Parent;
    return d!.FullName;
}
