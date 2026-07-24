using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Core.Settings;

/// <summary>
/// 应用配置：渲染参数 + 输出预设 + 体验开关（开发规划阶段3任务1）。
/// 持久化最近一次有效配置到 AppSettings.json。
/// OpenAfterExport：单图完成后打开该 MP4，批量完成时打开输出目录。
/// RememberSettings：是否持久化配置；关闭时每次修改不写文件。
/// </summary>
public sealed record AppSettings(
    RenderParameters RenderParameters,
    ExportPreset Preset,
    bool OpenAfterExport = true,
    bool RememberSettings = true)
{
    public static AppSettings Default() => new(RenderParameters.Default(), ExportPreset.Compatibility);
}
