using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Core.Exporting;

/// <summary>FFmpeg 编解码器选择。</summary>
public enum FfmpegCodec
{
    H264Nvenc,
    HevcNvenc,
    Libx264,
}

/// <summary>
/// 构造完成的 FFmpeg 命令（纯数据）。
/// Args 为逐个参数列表（tmpPath 作为独立元素，由执行器用 ProcessStartInfo.ArgumentList 派发，避免空格/引号问题）。
/// StdinPixelFormat: GPU 路径喂 NV12，CPU 回退喂 RGBA（软件投影产物）。
/// </summary>
public sealed record FfmpegCommand(
    string Exe,
    IReadOnlyList<string> Args,
    string StdinPixelFormat,
    string CodecLabel,
    bool IsCpuFallback)
{
    /// <summary>方便日志/测试：以空格连接的参数串（路径含空格时加引号）。</summary>
    public string JoinedArgs => string.Join(' ', Args.Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return arg;
        if (arg.Contains(' ') && !arg.StartsWith('"')) return $"\"{arg}\"";
        return arg;
    }
}

/// <summary>
/// FFmpeg 命令构造器（纯逻辑，可单测）。
/// 集中处理 H.264/H.265 NVENC 与 CPU libx264 回退的命令分支，
/// 保证预设语义与实际编码器一致（P0-3），并服务 CPU 回退（P0-1）。
/// 码率统一来自 ExportPrecheck.EstimateBitrate。
/// </summary>
public static class FfmpegCommandBuilder
{
    /// <summary>
    /// GPU 路径：stdin NV12（GPU 投影+颜色转换产物回读）→ h264_nvenc / hevc_nvenc 硬件编码。
    /// resolvedPreset=Size 且 hevcAvailable 时用 hevc_nvenc，否则 h264_nvenc。
    /// </summary>
    public static FfmpegCommand BuildGpuNvenc(
        string tmpPath, int outW, int outH, int fps, uint bitrate,
        ExportPreset resolvedPreset, bool hevcAvailable)
    {
        bool useHevc = resolvedPreset == ExportPreset.Size && hevcAvailable;
        string codec = useHevc ? "hevc_nvenc" : "h264_nvenc";
        string label = useHevc ? "H.265 NVENC" : "H.264 NVENC";

        var args = CommonInputArgs("nv12", outW, outH, fps);
        args.Add("-c:v"); args.Add(codec);
        args.Add("-b:v"); args.Add(bitrate.ToString());
        args.Add("-movflags"); args.Add("+faststart");
        args.Add(tmpPath);

        return new FfmpegCommand("ffmpeg", args, "nv12", label, IsCpuFallback: false);
    }

    /// <summary>
    /// CPU 回退路径：stdin RGBA（Core EquirectProjection 软件逐帧投影产物）→ libx264 软件编码。
    /// cpuCores 控制 libx264 -threads（GPU 路径不传，PRD #5：仅回退时生效）。
    /// </summary>
    public static FfmpegCommand BuildCpuFallback(
        string tmpPath, int outW, int outH, int fps, uint bitrate, int cpuCores)
    {
        var args = CommonInputArgs("rgba", outW, outH, fps);
        args.Add("-c:v"); args.Add("libx264");
        args.Add("-preset"); args.Add("medium");
        args.Add("-threads"); args.Add(cpuCores.ToString());
        args.Add("-b:v"); args.Add(bitrate.ToString());
        args.Add("-pix_fmt"); args.Add("yuv420p"); // PRD：H.264 yuv420p
        args.Add("-movflags"); args.Add("+faststart");
        args.Add(tmpPath);

        return new FfmpegCommand("ffmpeg", args, "rgba", "libx264", IsCpuFallback: true);
    }

    /// <summary>构造 rawvideo stdin 输入参数（-y -f rawvideo -pixel_format X -video_size WxH -framerate fps -i -）。</summary>
    private static List<string> CommonInputArgs(string pixelFormat, int outW, int outH, int fps) => new()
    {
        "-y",
        "-f", "rawvideo",
        "-pixel_format", pixelFormat,
        "-video_size", $"{outW}x{outH}",
        "-framerate", fps.ToString(),
        "-i", "-",
    };
}
