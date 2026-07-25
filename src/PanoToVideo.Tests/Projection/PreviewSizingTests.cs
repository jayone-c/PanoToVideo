using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

public class PreviewSizingTests
{
    [Fact]
    public void 横屏预览_保持横屏比例()
    {
        var (width, height) = PreviewSizing.Fit(1920, 1080);
        Assert.Equal((320, 180), (width, height));
    }

    [Fact]
    public void 横屏加宽预览_保持横屏比例()
    {
        var (width, height) = PreviewSizing.Fit(1920, 1080, maxSide: 384);
        Assert.Equal((384, 216), (width, height));
    }

    [Fact]
    public void 竖屏预览_保持竖屏比例()
    {
        var (width, height) = PreviewSizing.Fit(1080, 1920);
        Assert.Equal((180, 320), (width, height));
    }
}
