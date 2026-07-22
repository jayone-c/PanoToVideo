using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Core.Settings;

/// <summary>
/// 应用配置：渲染参数 + 输出预设（开发规划阶段3任务1）。
/// 持久化最近一次有效配置到 AppSettings.json。
/// </summary>
public sealed record AppSettings(RenderParameters RenderParameters, ExportPreset Preset)
{
    public static AppSettings Default() => new(RenderParameters.Default(), ExportPreset.Compatibility);
}
