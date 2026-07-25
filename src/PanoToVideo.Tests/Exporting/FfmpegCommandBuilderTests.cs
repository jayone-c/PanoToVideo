using PanoToVideo.Core.Exporting;
using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Tests.Exporting;

/// <summary>
/// FFmpeg 命令构造器纯逻辑 TDD 测试。
/// 覆盖 H.264/H.265 NVENC 与 CPU libx264 回退分支（P0-3 + P0-1）。
/// </summary>
public class FfmpegCommandBuilderTests
{
    [Fact]
    public void BuildGpuHardware_AMF与QSV_使用对应硬件编码器()
    {
        var amf = FfmpegCommandBuilder.BuildGpuHardware(Tmp, W, H, Fps, Bitrate, ExportPreset.Compatibility, false, HardwareEncoderKind.Amf);
        var qsv = FfmpegCommandBuilder.BuildGpuHardware(Tmp, W, H, Fps, Bitrate, ExportPreset.Size, true, HardwareEncoderKind.Qsv);

        Assert.Contains("h264_amf", amf.Args);
        Assert.Equal("H.264 AMF", amf.CodecLabel);
        Assert.Contains("hevc_qsv", qsv.Args);
        Assert.Equal("H.265 QSV", qsv.CodecLabel);
    }
    private const string Tmp = "/tmp/out.tmp.mp4";
    private const int W = 1080;
    private const int H = 1920;
    private const int Fps = 60;
    private const uint Bitrate = 16_000_000;

    [Fact]
    public void BuildGpuNvenc_兼容预设_使用h264_nvenc()
    {
        var cmd = FfmpegCommandBuilder.BuildGpuNvenc(Tmp, W, H, Fps, Bitrate, ExportPreset.Compatibility, hevcAvailable: true);

        Assert.Equal("ffmpeg", cmd.Exe);
        Assert.Equal("nv12", cmd.StdinPixelFormat);
        Assert.Equal("H.264 NVENC", cmd.CodecLabel);
        Assert.False(cmd.IsCpuFallback);
        // -c:v 紧跟 h264_nvenc（用 JoinedArgs 子串断言，避免 IReadOnlyList 无 IndexOf）
        Assert.Contains("-c:v h264_nvenc", cmd.JoinedArgs);
    }

    [Fact]
    public void BuildGpuNvenc_体积预设_HEVC可用_使用hevc_nvenc()
    {
        var cmd = FfmpegCommandBuilder.BuildGpuNvenc(Tmp, W, H, Fps, Bitrate, ExportPreset.Size, hevcAvailable: true);

        Assert.Equal("H.265 NVENC", cmd.CodecLabel);
        Assert.Contains("hevc_nvenc", cmd.Args);
        Assert.DoesNotContain("h264_nvenc", cmd.Args);
    }

    [Fact]
    public void BuildGpuNvenc_体积预设_HEVC不可用_退回h264_nvenc()
    {
        var cmd = FfmpegCommandBuilder.BuildGpuNvenc(Tmp, W, H, Fps, Bitrate, ExportPreset.Size, hevcAvailable: false);

        Assert.Equal("H.264 NVENC", cmd.CodecLabel);
        Assert.Contains("h264_nvenc", cmd.Args);
        Assert.DoesNotContain("hevc_nvenc", cmd.Args);
    }

    [Fact]
    public void BuildGpuNvenc_含码率与faststart与NV12输入()
    {
        var cmd = FfmpegCommandBuilder.BuildGpuNvenc(Tmp, W, H, Fps, Bitrate, ExportPreset.Compatibility, false);
        var joined = cmd.JoinedArgs;

        Assert.Contains("-b:v 16000000", joined);
        Assert.Contains("-movflags +faststart", joined);
        Assert.Contains("-pixel_format nv12", joined);
        Assert.Contains($"-video_size {W}x{H}", joined);
        Assert.Contains($"-framerate {Fps}", joined);
        Assert.Contains("-i -", joined);
        Assert.Contains("-y", cmd.Args);
    }

    [Fact]
    public void BuildGpuNvenc_临时路径作为末尾参数()
    {
        var cmd = FfmpegCommandBuilder.BuildGpuNvenc(Tmp, W, H, Fps, Bitrate, ExportPreset.Compatibility, false);

        Assert.Equal(Tmp, cmd.Args[^1]);
    }

    [Fact]
    public void BuildCpuFallback_使用libx264与threads与RGBA输入()
    {
        var cmd = FfmpegCommandBuilder.BuildCpuFallback(Tmp, W, H, Fps, Bitrate, cpuCores: 8);
        var joined = cmd.JoinedArgs;

        Assert.Equal("rgba", cmd.StdinPixelFormat);
        Assert.Equal("libx264", cmd.CodecLabel);
        Assert.True(cmd.IsCpuFallback);
        Assert.Contains("-c:v libx264", joined);
        Assert.Contains("-threads 8", joined);
        Assert.Contains("-pixel_format rgba", joined);
        Assert.Contains("-pix_fmt yuv420p", joined); // PRD: H.264 yuv420p
        Assert.Contains("-movflags +faststart", joined);
        Assert.Contains("-b:v 16000000", joined);
    }

    [Fact]
    public void BuildCpuFallback_线程数随cpuCores变化()
    {
        var cmd4 = FfmpegCommandBuilder.BuildCpuFallback(Tmp, W, H, Fps, Bitrate, cpuCores: 4);
        var cmd16 = FfmpegCommandBuilder.BuildCpuFallback(Tmp, W, H, Fps, Bitrate, cpuCores: 16);

        Assert.Contains("4", cmd4.Args);
        Assert.Contains("-threads", cmd4.Args);
        Assert.Contains("16", cmd16.Args);
    }

    [Fact]
    public void 命令码率与ExportPrecheck估算一致()
    {
        // 命令构造器接收的 bitrate 应来自 ExportPrecheck.EstimateBitrate（执行器层传入）
        var expected = (uint)ExportPrecheck.EstimateBitrate(ExportPreset.Compatibility, W, H, Fps);
        var cmd = FfmpegCommandBuilder.BuildGpuNvenc(Tmp, W, H, Fps, expected, ExportPreset.Compatibility, false);

        Assert.Contains(expected.ToString(), cmd.Args);
    }

    [Fact]
    public void 路径含空格_JoinedArgs加引号()
    {
        var spaced = "/tmp/my dir/out.tmp.mp4";
        var cmd = FfmpegCommandBuilder.BuildGpuNvenc(spaced, W, H, Fps, Bitrate, ExportPreset.Compatibility, false);

        Assert.Contains("\"/tmp/my dir/out.tmp.mp4\"", cmd.JoinedArgs);
    }
}
