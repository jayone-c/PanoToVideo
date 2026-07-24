using System.Text.Json;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Core.Settings;

/// <summary>
/// 配置记忆存储（开发规划阶段3任务1）。
/// 序列化 AppSettings 到 JSON，读写 AppSettings.json；
/// 加载时校验（RenderParametersValidator + Preset 枚举），无效字段回退默认。
/// RememberSettings=false 时不持久化（移除既有文件）。
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    private readonly string _path;
    private readonly RenderParametersValidator _validator = new();

    public AppSettingsStore(string path) => _path = path;

    /// <summary>保存配置（创建目录与文件）。RememberSettings=false 时移除既有文件不写入。</summary>
    public void Save(AppSettings settings)
    {
        if (!settings.RememberSettings)
        {
            // 不记忆配置：移除既有文件，不写入新内容
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json);
    }

    /// <summary>加载配置：文件不存在/损坏/无效时回退默认。</summary>
    public AppSettings Load()
    {
        if (!File.Exists(_path))
            return AppSettings.Default();

        AppSettingsDto? dto;
        try
        {
            var json = File.ReadAllText(_path);
            dto = JsonSerializer.Deserialize<AppSettingsDto>(json, JsonOptions);
        }
        catch
        {
            // 损坏 JSON 回退默认
            return AppSettings.Default();
        }

        if (dto == null)
            return AppSettings.Default();

        // 参数校验：无效回退默认参数
        RenderParameters parameters = RenderParameters.Default();
        if (dto.RenderParameters != null)
        {
            // 旧 JSON 缺 CpuCores 字段时反序列化为 0，修正为自动值再校验
            var p = dto.RenderParameters.CpuCores == 0
                ? dto.RenderParameters with { CpuCores = Environment.ProcessorCount }
                : dto.RenderParameters;
            if (_validator.Validate(p).IsValid)
                parameters = p;
        }

        // Preset 枚举校验：无效回退默认
        var preset = ExportPreset.Compatibility;
        if (Enum.IsDefined(typeof(ExportPreset), dto.Preset))
            preset = (ExportPreset)dto.Preset;

        // 体验开关：缺字段时 DTO 默认 true
        var openAfterExport = dto.OpenAfterExport;
        var rememberSettings = dto.RememberSettings;

        return new AppSettings(parameters, preset, openAfterExport, rememberSettings);
    }

    /// <summary>序列化 DTO（Preset 用 int 避免 JSON 枚举字符串问题）。</summary>
    private sealed class AppSettingsDto
    {
        public RenderParameters? RenderParameters { get; set; }
        public int Preset { get; set; }
        public bool OpenAfterExport { get; set; } = true;
        public bool RememberSettings { get; set; } = true;
    }
}
