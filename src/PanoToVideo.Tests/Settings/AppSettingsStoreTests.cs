using PanoToVideo.Core.Parameters;
using PanoToVideo.Core.Precheck;
using PanoToVideo.Core.Settings;

namespace PanoToVideo.Tests.Settings;

/// <summary>
/// 配置记忆 TDD 测试（开发规划阶段3任务1）。
/// AppSettingsStore：序列化 RenderParameters + ExportPreset 到 JSON，
/// 读写 %LocalAppData%/PanoToVideo/AppSettings.json；加载校验，无效回退默认。
/// </summary>
public class AppSettingsStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _settingsPath;

    public AppSettingsStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pano_settings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _settingsPath = Path.Combine(_testDir, "AppSettings.json");
    }

    [Fact]
    public void 保存后加载_字段往返一致()
    {
        var store = new AppSettingsStore(_settingsPath);
        var saved = new AppSettings(
            RenderParameters: new RenderParameters(15, 720, 30, 90.0, 1920, 1080, -10.0,
                RotationDirection.Counterclockwise, true, 1),
            Preset: ExportPreset.Size,
            OpenAfterExport: false,
            RememberSettings: true);

        store.Save(saved);
        var loaded = store.Load();

        Assert.Equal(saved.RenderParameters.DurationSeconds, loaded.RenderParameters.DurationSeconds);
        Assert.Equal(saved.RenderParameters.RotationDegrees, loaded.RenderParameters.RotationDegrees);
        Assert.Equal(saved.RenderParameters.Fps, loaded.RenderParameters.Fps);
        Assert.Equal(saved.RenderParameters.HorizontalFov, loaded.RenderParameters.HorizontalFov);
        Assert.Equal(saved.RenderParameters.Width, loaded.RenderParameters.Width);
        Assert.Equal(saved.RenderParameters.Height, loaded.RenderParameters.Height);
        Assert.Equal(saved.RenderParameters.Pitch, loaded.RenderParameters.Pitch);
        Assert.Equal(saved.RenderParameters.Direction, loaded.RenderParameters.Direction);
        Assert.Equal(saved.RenderParameters.AsteroidIntro, loaded.RenderParameters.AsteroidIntro);
        Assert.Equal(saved.RenderParameters.CpuCores, loaded.RenderParameters.CpuCores);
        Assert.Equal(saved.Preset, loaded.Preset);
        Assert.Equal(saved.OpenAfterExport, loaded.OpenAfterExport);
        Assert.Equal(saved.RememberSettings, loaded.RememberSettings);
    }

    [Fact]
    public void 文件不存在_返回默认配置()
    {
        var store = new AppSettingsStore(_settingsPath);
        File.Delete(_settingsPath); // 确保不存在

        var loaded = store.Load();

        Assert.Equal(RenderParameters.Default(), loaded.RenderParameters);
        Assert.Equal(ExportPreset.Compatibility, loaded.Preset);
    }

    [Fact]
    public void 无效配置_回退默认()
    {
        var store = new AppSettingsStore(_settingsPath);
        // 写入无效配置（时长0、FPS非法、FOV越界）
        File.WriteAllText(_settingsPath,
            """{"RenderParameters":{"DurationSeconds":0,"RotationDegrees":0,"Fps":15,"HorizontalFov":200,"Width":1081,"Height":1921,"Pitch":90,"Direction":0,"AsteroidIntro":true},"Preset":1}""");

        var loaded = store.Load();

        Assert.Equal(RenderParameters.Default(), loaded.RenderParameters);
    }

    [Fact]
    public void 损坏JSON_回退默认()
    {
        var store = new AppSettingsStore(_settingsPath);
        File.WriteAllText(_settingsPath, "{ 这是损坏的 JSON ]");

        var loaded = store.Load();

        Assert.Equal(RenderParameters.Default(), loaded.RenderParameters);
        Assert.Equal(ExportPreset.Compatibility, loaded.Preset);
    }

    [Fact]
    public void 保存_创建目录与文件()
    {
        var nestedPath = Path.Combine(_testDir, "nested", "deep", "AppSettings.json");
        var store = new AppSettingsStore(nestedPath);

        store.Save(new AppSettings(RenderParameters.Default(), ExportPreset.Compatibility));

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void 加载仅有效部分_无效字段回退()
    {
        var store = new AppSettingsStore(_settingsPath);
        // 有效参数但 Preset 越界 -> Preset 回退默认，参数保留
        File.WriteAllText(_settingsPath,
            """{"RenderParameters":{"DurationSeconds":10,"RotationDegrees":360,"Fps":60,"HorizontalFov":75,"Width":1080,"Height":1920,"Pitch":0,"Direction":1,"AsteroidIntro":false},"Preset":99}""");

        var loaded = store.Load();

        Assert.Equal(10, loaded.RenderParameters.DurationSeconds); // 有效参数保留
        Assert.Equal(ExportPreset.Compatibility, loaded.Preset); // 无效Preset回退
    }

    [Fact]
    public void 旧JSON缺CpuCores_回退默认核心数()
    {
        var store = new AppSettingsStore(_settingsPath);
        // 旧版本 JSON 无 CpuCores 字段，其余参数有效
        File.WriteAllText(_settingsPath,
            """{"RenderParameters":{"DurationSeconds":10,"RotationDegrees":360,"Fps":60,"HorizontalFov":75,"Width":1080,"Height":1920,"Pitch":0,"Direction":1,"AsteroidIntro":false},"Preset":0}""");

        var loaded = store.Load();

        Assert.Equal(10, loaded.RenderParameters.DurationSeconds); // 有效参数保留
        Assert.Equal(Environment.ProcessorCount, loaded.RenderParameters.CpuCores); // 缺字段回退自动值
    }

    [Fact]
    public void RememberSettings为false_Save不写文件()
    {
        var store = new AppSettingsStore(_settingsPath);
        // 预先写入一个文件，验证关闭记忆后被移除
        store.Save(new AppSettings(RenderParameters.Default(), ExportPreset.Compatibility));
        Assert.True(File.Exists(_settingsPath));

        var noRemember = new AppSettings(RenderParameters.Default(), ExportPreset.Compatibility, OpenAfterExport: true, RememberSettings: false);
        store.Save(noRemember);

        Assert.False(File.Exists(_settingsPath)); // 不记忆配置：文件被移除

        // 再次加载回退默认
        var loaded = store.Load();
        Assert.Equal(RenderParameters.Default(), loaded.RenderParameters);
    }

    [Fact]
    public void 默认配置_体验开关为true()
    {
        var defaults = AppSettings.Default();

        Assert.True(defaults.OpenAfterExport);
        Assert.True(defaults.RememberSettings);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }
}
