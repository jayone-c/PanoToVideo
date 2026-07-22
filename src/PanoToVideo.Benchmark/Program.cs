using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Validation;
using PanoToVideo.Render.DeviceProbe;
using PanoToVideo.Render.Exporting;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using static Vortice.Direct3D11.D3D11;

// ===== 阶段4 · RTX 4090 D 性能基准验收（PRD 成功标准2，Q5）=====
// 基准任务：输入ERP -> 1080×1920、30秒、60FPS、360°、75°FOV、H.264
// 验收：端到端 ≥60 输出 FPS（纯渲染 ≤30 秒），日志证明投影与编码实际设备。
// 用法：dotnet run -c Release [7680|xuanguan1|chashi]  默认 7680

string imageArg = args.Length > 0 ? args[0] : "7680";
string repoRoot = FindRepoRoot();
string erpPath = imageArg switch
{
    "xuanguan1" => Path.Combine(repoRoot, "720", "玄关1.jpg"),
    "chashi" => Path.Combine(repoRoot, "720", "茶室空间.jpg"),
    _ => Path.Combine(repoRoot, "tests", "fixtures", "erp_7680x3840.jpg"),
};
string imageLabel = imageArg == "7680" ? "7680x3840(缩放)" : $"{imageArg}(6000x3000真实)";

Console.WriteLine($"=== 阶段4 · RTX 4090 D 性能基准验收 [{imageLabel}] ===");

// 1. 解码 ERP
Console.WriteLine($"1. 解码 {imageLabel}...");
var decodeSw = Stopwatch.StartNew();
var (erpRgba, erpW, erpH) = DecodeImage(erpPath);
decodeSw.Stop();
Console.WriteLine($"   尺寸 {erpW}x{erpH}, 解码耗时 {decodeSw.Elapsed.TotalSeconds:F2}s");

// 2. 设备探测
Console.WriteLine("2. 设备探测...");
var probeSw = Stopwatch.StartNew();
using var probe = new DeviceProbe();
var probeResult = probe.Probe();
probeSw.Stop();
var preferred = probeResult.Preferred ?? throw new InvalidOperationException("无合格 GPU 设备");
Console.WriteLine($"   首选: {preferred.Candidate.Description} | {preferred.EncoderName} | Luid=0x{preferred.Candidate.Luid:X16}");
Console.WriteLine($"   探测耗时 {probeSw.Elapsed.TotalSeconds:F2}s");

// 3. PRD 基准参数
var parameters = new RenderParameters(
    DurationSeconds: 30, RotationDegrees: 360, Fps: 60,
    HorizontalFov: 75.0, Width: 1080, Height: 1920, Pitch: 0.0,
    Direction: RotationDirection.Clockwise, AsteroidIntro: false);
int totalFrames = parameters.TotalFrames; // 1800
Console.WriteLine($"3. 基准任务: {erpW}x{erpH} -> {parameters.Width}x{parameters.Height}, {parameters.DurationSeconds}s@{parameters.Fps}FPS = {totalFrames}帧, 360°/{parameters.HorizontalFov}°FOV, H.264");

// 4. 导出（全链路计时）
string outDir = Path.Combine(repoRoot, "benchmark_exports");
if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
Directory.CreateDirectory(outDir);
long avail = new DriveInfo(Path.GetPathRoot(outDir)!).AvailableFreeSpace;

var executor = new FfmpegNvencExecutor(erpRgba, erpW, erpH, parameters, ExportPreset.Compatibility);
var orchestrator = new SingleImageExportOrchestrator();

var progress = new ProgressAdapter<ExportProgress>(p =>
{
    if ((p.FrameIndex + 1) % 300 == 0)
        Console.WriteLine($"   进度 {p.FrameIndex + 1}/{p.TotalFrames} 投影{p.ProjectionFps:F0}fps 编码{p.EncodingFps:F0}fps 已耗时{p.Elapsed.TotalSeconds:F1}s");
});

var sw = Stopwatch.StartNew();
var result = orchestrator.Export(
    new ImageInfo(erpW, erpH, false, erpPath), parameters, ExportPreset.Compatibility,
    outDir, avail, Directory.GetFiles(outDir), executor, default, progress);
sw.Stop();

// 阶段拆分诊断：GpuExportExecutor 内部已分阶段，但 orchestrator 含校验/预检/命名。
// 用 executor 单独测纯导出（不含 orchestrator 开销）定位瓶颈
Console.WriteLine($"\n[诊断] orchestrator 总耗时 {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"[诊断] 导出结果 Success={result.Success} avgFps(log)={result.Log?.AverageFps ?? 0:F1}");

Console.WriteLine($"\n4. 导出结果: {(result.Success ? "成功" : "失败 " + result.Error)}");
if (!result.Success) return;

Console.WriteLine($"   输出: {result.OutputPath}");
Console.WriteLine($"   大小: {new FileInfo(result.OutputPath!).Length / 1024.0 / 1024.0:F2} MB");
Console.WriteLine($"   总耗时: {sw.Elapsed.TotalSeconds:F2}s");
double avgFps = totalFrames / sw.Elapsed.TotalSeconds;
Console.WriteLine($"   平均FPS: {avgFps:F1}");

// 5. 验收判定（PRD 成功标准2）
bool fpsOk = avgFps >= 60.0;
bool renderTimeOk = sw.Elapsed.TotalSeconds <= 30.0;
Console.WriteLine($"\n=== 验收判定 ===");
Console.WriteLine($"   平均FPS >= 60: {avgFps:F1} {(fpsOk ? "通过 ✓" : "未达标 ✗")}");
Console.WriteLine($"   纯渲染 <= 30s: {sw.Elapsed.TotalSeconds:F1}s {(renderTimeOk ? "通过 ✓" : "未达标 ✗")}");
Console.WriteLine($"   {(fpsOk && renderTimeOk ? "✓ 性能基准达标" : "✗ 性能未达标，不得标记完成")}");

// 6. ffprobe 校验
Console.WriteLine($"\n5. ffprobe 校验:");
var psi = new ProcessStartInfo("ffprobe",
    $"-v error -show_entries stream=codec_name,codec_type,width,height,r_frame_rate,duration,nb_frames -show_entries format=duration -of default=noprint_wrappers=1 \"{result.OutputPath}\"")
{ RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
var p = Process.Start(psi)!;
var ffout = p.StandardOutput.ReadToEnd();
p.WaitForExit();
Console.WriteLine(ffout.Trim().Replace("\n", "\n   "));
bool ffOk = ffout.Contains("codec_name=h264") && ffout.Contains("width=1080") && ffout.Contains("height=1920")
    && ffout.Contains("r_frame_rate=60/1") && ffout.Contains("duration=30") && !ffout.Contains("codec_type=audio");
Console.WriteLine($"   ffprobe校验: {(ffOk ? "通过(H.264/1080x1920/60FPS/30s/无音频) ✓" : "未通过 ✗")}");

static (byte[] rgba, int w, int h) DecodeImage(string path)
{
    using var bmp = new Bitmap(path);
    var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
    try
    {
        var rgba = new byte[bmp.Width * bmp.Height * 4];
        for (int y = 0; y < bmp.Height; y++)
        {
            var row = new byte[bmp.Width * 4];
            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, bmp.Width * 4);
            for (int x = 0; x < bmp.Width; x++)
            {
                rgba[(y * bmp.Width + x) * 4] = row[x * 4 + 2];
                rgba[(y * bmp.Width + x) * 4 + 1] = row[x * 4 + 1];
                rgba[(y * bmp.Width + x) * 4 + 2] = row[x * 4];
                rgba[(y * bmp.Width + x) * 4 + 3] = 255;
            }
        }
        return (rgba, bmp.Width, bmp.Height);
    }
    finally { bmp.UnlockBits(data); }
}

static string FindRepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "全景图转短视频工具-PRD.md")))
        d = d.Parent;
    return d!.FullName;
}

sealed class ProgressAdapter<T> : IProgress<T>
{
    private readonly Action<T> _cb;
    public ProgressAdapter(Action<T> cb) => _cb = cb;
    public void Report(T value) => _cb(value);
}
