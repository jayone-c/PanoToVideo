namespace PanoToVideo.Core.Parameters;

/// <summary>
/// 单图参数校验器。规则对齐 PRD 单图参数表与开发规划 §1.4。
/// CpuCores 仅在 CPU 回退模式生效，但此处统一校验范围合法性。
/// </summary>
public sealed class RenderParametersValidator
{
    private static readonly HashSet<int> SupportedFps = new() { 24, 25, 30, 50, 60 };

    /// <summary>用本机 ProcessorCount 作为 CpuCores 上限校验。</summary>
    public ParameterValidationResult Validate(RenderParameters p) =>
        Validate(p, Environment.ProcessorCount);

    /// <summary>显式指定 CpuCores 上限校验（便于单测，maxCores&le;0 时回退本机 ProcessorCount）。</summary>
    public ParameterValidationResult Validate(RenderParameters p, int maxCores)
    {
        var errors = new List<string>();

        if (p.DurationSeconds <= 0)
            errors.Add($"视频长度必须为正整数，实际 {p.DurationSeconds}");
        if (p.RotationDegrees <= 0)
            errors.Add($"旋转度数必须为正整数，实际 {p.RotationDegrees}");
        if (!SupportedFps.Contains(p.Fps))
            errors.Add($"帧速率必须是 24/25/30/50/60，实际 {p.Fps}");
        if (p.HorizontalFov < 30.0 || p.HorizontalFov > 110.0)
            errors.Add($"水平 FOV 必须在 [30, 110]，实际 {p.HorizontalFov}");
        if (p.Width <= 0 || p.Width % 2 != 0)
            errors.Add($"视频宽度必须为正偶数，实际 {p.Width}");
        if (p.Height <= 0 || p.Height % 2 != 0)
            errors.Add($"视频高度必须为正偶数，实际 {p.Height}");
        if (p.Pitch < -85.0 || p.Pitch > 85.0)
            errors.Add($"俯仰角必须在 [-85, 85]，实际 {p.Pitch}");
        if (p.StartYaw < 0.0 || p.StartYaw >= 360.0)
            errors.Add($"起始方位必须在 [0, 360) 内，实际 {p.StartYaw}");
        if (p.RotationTempo is { Enabled: true } tempo && !tempo.IsUsableFor(p.DurationSeconds))
            errors.Add("镜头节奏需满足：开始时间、两段平滑过渡和慢转维持时间之和不能超过视频时长；慢转速度为 10%–90%。");

        var effectiveMax = maxCores > 0 ? maxCores : Environment.ProcessorCount;
        if (p.CpuCores < 1 || p.CpuCores > effectiveMax)
            errors.Add($"CPU 核心数必须在 [1, {effectiveMax}]，实际 {p.CpuCores}");

        return errors.Count == 0
            ? ParameterValidationResult.Ok()
            : ParameterValidationResult.Fail(errors);
    }
}
