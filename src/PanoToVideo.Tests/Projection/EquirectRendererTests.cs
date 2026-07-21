using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// CPU 参考渲染器 TDD 测试。
/// EquirectRenderer 用 EquirectProjection 逐像素投影 + 双线性采样 ERP 纹理，
/// 产出完整 perspective 帧。它同时是 GPU shader 的参考实现与 CPU 回退的几何核心。
/// </summary>
public class EquirectRendererTests
{
    [Fact]
    public void 渲染纯色ERP_整帧同色()
    {
        var erp = Enumerable.Repeat(new Rgb(10, 20, 30), 8 * 4).ToArray();

        var frame = EquirectRenderer.RenderFrame(erp, 8, 4, 4, 4, 75, 0, 0);

        Assert.Equal(4 * 4, frame.Length);
        Assert.All(frame, p => Assert.Equal(new Rgb(10, 20, 30), p));
    }

    [Fact]
    public void 渲染输出尺寸正确()
    {
        var erp = Enumerable.Repeat(new Rgb(0, 0, 0), 8 * 4).ToArray();

        var frame = EquirectRenderer.RenderFrame(erp, 8, 4, 6, 3, 75, 0, 0);

        Assert.Equal(6 * 3, frame.Length);
    }

    [Fact]
    public void 经度环绕_yaw180采样接缝不越界()
    {
        // yaw=180 看背面，采样经度 0/360 接缝；不应越界或抛异常
        var erp = Enumerable.Repeat(new Rgb(5, 5, 5), 8 * 4).ToArray();

        var frame = EquirectRenderer.RenderFrame(erp, 8, 4, 4, 4, 75, 180, 0);

        Assert.All(frame, p => Assert.Equal(new Rgb(5, 5, 5), p));
    }

    [Fact]
    public void 极地采样_pitch90不越界()
    {
        // pitch=90 看北极顶行，纬度钳制不应越界
        var erp = Enumerable.Repeat(new Rgb(7, 8, 9), 8 * 4).ToArray();

        var frame = EquirectRenderer.RenderFrame(erp, 8, 4, 4, 4, 75, 0, 90);

        Assert.All(frame, p => Assert.Equal(new Rgb(7, 8, 9), p));
    }
}
