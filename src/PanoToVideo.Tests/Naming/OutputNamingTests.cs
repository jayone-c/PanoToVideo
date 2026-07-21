using PanoToVideo.Core.Naming;

namespace PanoToVideo.Tests.Naming;

/// <summary>
/// 输出命名 TDD 测试。
/// 契约：开发规划 §阶段2任务4、§8。
/// </summary>
public class OutputNamingTests
{
    [Fact]
    public void 标准命名格式正确()
    {
        var name = OutputNaming.BuildFileName("玄关1", 1080, 1920, 30, 360);
        Assert.Equal("玄关1_1080x1920_30s_360deg.mp4", name);
    }

    [Fact]
    public void 横屏命名格式正确()
    {
        var name = OutputNaming.BuildFileName("茶室", 1920, 1080, 10, 720);
        Assert.Equal("茶室_1920x1080_10s_720deg.mp4", name);
    }

    [Fact]
    public void CombineExportsDir_拼接exports子目录()
    {
        var dir = OutputNaming.CombineExportsDir("/base");
        Assert.Equal(Path.Combine("/base", "exports"), dir);
    }

    [Fact]
    public void 无重名_返回原名路径()
    {
        var path = OutputNaming.ResolveUniquePath("/out/exports", "玄关1_1080x1920_30s_360deg.mp4", Array.Empty<string>());
        Assert.Equal(Path.Combine("/out/exports", "玄关1_1080x1920_30s_360deg.mp4"), path);
    }

    [Fact]
    public void 存在重名_追加_1()
    {
        var existing = new[] { "玄关1_1080x1920_30s_360deg.mp4" };
        var path = OutputNaming.ResolveUniquePath("/out/exports", "玄关1_1080x1920_30s_360deg.mp4", existing);
        Assert.Equal(Path.Combine("/out/exports", "玄关1_1080x1920_30s_360deg_1.mp4"), path);
    }

    [Fact]
    public void 存在原名和_1_追加_2()
    {
        var existing = new[]
        {
            "玄关1_1080x1920_30s_360deg.mp4",
            "玄关1_1080x1920_30s_360deg_1.mp4",
        };
        var path = OutputNaming.ResolveUniquePath("/out/exports", "玄关1_1080x1920_30s_360deg.mp4", existing);
        Assert.Equal(Path.Combine("/out/exports", "玄关1_1080x1920_30s_360deg_2.mp4"), path);
    }

    [Fact]
    public void 序号1空缺_用最小可用序号1()
    {
        var existing = new[]
        {
            "玄关1_1080x1920_30s_360deg.mp4",
            "玄关1_1080x1920_30s_360deg_2.mp4", // _1 空缺
        };
        var path = OutputNaming.ResolveUniquePath("/out/exports", "玄关1_1080x1920_30s_360deg.mp4", existing);
        Assert.Equal(Path.Combine("/out/exports", "玄关1_1080x1920_30s_360deg_1.mp4"), path);
    }

    [Fact]
    public void 重名大小写不敏感()
    {
        var existing = new[] { "玄关1_1080x1920_30s_360deg.MP4" };
        var path = OutputNaming.ResolveUniquePath("/out/exports", "玄关1_1080x1920_30s_360deg.mp4", existing);
        Assert.Equal(Path.Combine("/out/exports", "玄关1_1080x1920_30s_360deg_1.mp4"), path);
    }
}
