namespace PanoToVideo.Core.Parameters;

/// <summary>
/// 单图导出参数（PRD 单图参数表）。值对象，不可变。
/// CpuCores 仅在 CPU 回退模式生效（影响 libx264 -threads），GPU 路径忽略。
/// </summary>
public sealed record RenderParameters(
    int DurationSeconds,
    int RotationDegrees,
    int Fps,
    double HorizontalFov,
    int Width,
    int Height,
    double Pitch,
    RotationDirection Direction,
    bool AsteroidIntro,
    int CpuCores,
    double StartYaw = 0.0,
    RotationTempo? RotationTempo = null)
{
    /// <summary>总帧数 = 时长 × FPS。</summary>
    public int TotalFrames => DurationSeconds * Fps;

    public static RenderParameters Default() => new(
        DurationSeconds: 30,
        RotationDegrees: 360,
        Fps: 60,
        HorizontalFov: 75.0,
        Width: 1080,
        Height: 1920,
        Pitch: 0.0,
        Direction: RotationDirection.Clockwise,
        AsteroidIntro: false,
        CpuCores: Environment.ProcessorCount,
        StartYaw: 0.0);
}
