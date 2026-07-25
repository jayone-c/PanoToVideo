using System.Diagnostics;

namespace PanoToVideo.Render.Exporting;

/// <summary>当前机器上 FFmpeg 命令行工具的可用状态。</summary>
public sealed record FfmpegAvailability(bool IsAvailable, string? Version = null, string? Reason = null)
{
    /// <summary>可直接展示给用户的中文安装或故障提示。</summary>
    public string UserMessage => IsAvailable
        ? $"FFmpeg 已就绪{(string.IsNullOrWhiteSpace(Version) ? "" : $"（{Version}）")}。"
        : $"未检测到可用的 FFmpeg。请安装 FFmpeg，并将 ffmpeg.exe 所在目录添加到系统 PATH 后重新打开本程序。"
          + (string.IsNullOrWhiteSpace(Reason) ? "" : $" 原因：{Reason}");

    public static FfmpegAvailability Missing(string? reason = null) => new(false, null, reason);
}

/// <summary>探测当前 FFmpeg 构建实际暴露的编码器，避免只凭 Media Foundation 误判 NVENC 可用。</summary>
public static class FfmpegCapabilityProbe
{
    private static readonly Lazy<FfmpegAvailability> s_availability = new(ReadAvailability);
    private static readonly Lazy<IReadOnlySet<string>> s_encoders = new(ReadEncoders);

    /// <summary>FFmpeg 是否可启动。该结果会在进程生命周期内缓存。</summary>
    public static FfmpegAvailability Availability => s_availability.Value;

    public static bool HasEncoder(string encoder) => Availability.IsAvailable && s_encoders.Value.Contains(encoder);

    private static FfmpegAvailability ReadAvailability()
    {
        try
        {
            using var process = Start("-hide_banner -version");
            if (process == null) return FfmpegAvailability.Missing("无法启动 ffmpeg.exe。");

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return FfmpegAvailability.Missing("FFmpeg 启动检查超时。");
            }
            if (process.ExitCode != 0)
                return FfmpegAvailability.Missing($"FFmpeg 返回错误代码 {process.ExitCode}。");

            var version = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();
            return new FfmpegAvailability(true, version);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return FfmpegAvailability.Missing("系统 PATH 中未找到 ffmpeg.exe。");
        }
        catch (Exception ex)
        {
            return FfmpegAvailability.Missing($"FFmpeg 启动失败（{ex.GetType().Name}）。");
        }
    }

    private static IReadOnlySet<string> ReadEncoders()
    {
        if (!Availability.IsAvailable) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var process = Start("-hide_banner -encoders");
            if (process == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].Length == 6 && parts[0].All(c => char.IsLetter(c) || c == '.'))
                    encoders.Add(parts[1]);
            }
            return encoders;
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Process? Start(string arguments) => Process.Start(new ProcessStartInfo("ffmpeg", arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    });
}
