using System.Diagnostics;

namespace PanoToVideo.Render.Exporting;

/// <summary>探测当前 FFmpeg 构建实际暴露的编码器，避免只凭 Media Foundation 误判 NVENC 可用。</summary>
internal static class FfmpegCapabilityProbe
{
    private static readonly Lazy<IReadOnlySet<string>> s_encoders = new(ReadEncoders);

    public static bool HasEncoder(string encoder) => s_encoders.Value.Contains(encoder);

    private static IReadOnlySet<string> ReadEncoders()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-hide_banner -encoders")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var output = process.StandardOutput.ReadToEnd();
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
}
