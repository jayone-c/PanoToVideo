namespace PanoToVideo.Core.Precheck;

/// <summary>
/// 输出质量预设（PRD 输出、开发规划 §8）。
/// 兼容优先=H.264 16Mbps；体积优先=H.265 8Mbps（1080×1920@60 基准）。
/// </summary>
public enum ExportPreset
{
    Compatibility, // H.264
    Size,          // H.265
}

/// <summary>预设的码率基准与换算常数（开发规划 §8）。</summary>
public static class ExportPresetConstants
{
    public const double CompatibilityBaseBitrate = 16_000_000; // H.264 基准 bps
    public const double SizeBaseBitrate = 8_000_000;           // H.265 基准 bps
    public const int BaseWidth = 1080;
    public const int BaseHeight = 1920;
    public const int BaseFps = 60;
    public const double SizeReserveFactor = 1.15; // 额外预留 15%

    public static double BaseBitrateFor(ExportPreset preset) => preset switch
    {
        ExportPreset.Compatibility => CompatibilityBaseBitrate,
        ExportPreset.Size => SizeBaseBitrate,
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };
}
