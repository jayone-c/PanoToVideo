using System.Text.Json;
using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;

namespace PanoToVideo.Core.Settings;

/// <summary>
/// 配置记忆存储（开发规划阶段3任务1）。
/// 序列化 AppSettings 到 JSON，读写 AppSettings.json；
/// 加载时校验（RenderParametersValidator + Preset 枚举），无效字段回退默认。
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

    /// <summary>保存配置（创建目录与文件）。</summary>
    public void Save(AppSettings settings)
    {
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
        if (dto.RenderParameters != null && _validator.Validate(dto.RenderParameters).IsValid)
            parameters = dto.RenderParameters;

        // Preset 枚举校验：无效回退默认
        var preset = ExportPreset.Compatibility;
        if (Enum.IsDefined(typeof(ExportPreset), dto.Preset))
            preset = (ExportPreset)dto.Preset;

        return new AppSettings(parameters, preset);
    }

    /// <summary>序列化 DTO（Preset 用 int 避免 JSON 枚举字符串问题）。</summary>
    private sealed class AppSettingsDto
    {
        public RenderParameters? RenderParameters { get; set; }
        public int Preset { get; set; }
    }
}
