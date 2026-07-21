using PanoToVideo.Core.Projection;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// 投影数学 TDD 测试（核心正确性）。
/// 契约：开发规划 §1.4。对输出像素求 ERP 纹理坐标。
/// 约定：纹理 u/v ∈ [0,1)；v=0 顶行=北极(lat+90)；经度自动环绕（AddressU=Wrap）。
/// </summary>
public class EquirectProjectionTests
{
    private const int W = 1080;
    private const int H = 1920;
    private const double Hfov = 75.0;

    private static (double U, double V) ProjectCenter(double yawDeg, double pitchDeg) =>
        EquirectProjection.ProjectPixel(W / 2, H / 2, W, H, Hfov, yawDeg, pitchDeg);

    [Fact]
    public void 中心像素_默认朝向_纹理中心()
    {
        var (u, v) = ProjectCenter(0, 0);
        Assert.Equal(0.5, u, 3);
        Assert.Equal(0.5, v, 3);
    }

    [Fact]
    public void Yaw90_中心_水平偏移四分之一圈()
    {
        var (u, _) = ProjectCenter(90, 0);
        Assert.Equal(0.75, u, 3); // 0.5 + 90/360 = 0.75
    }

    [Fact]
    public void Yaw180_中心_环绕到纹理起点()
    {
        var (u, _) = ProjectCenter(180, 0);
        // 0.5 + 180/360 = 1.0 -> 环绕到 0.0；偶数尺寸亚像素偏移，容差 1e-3
        Assert.True(u < 1e-3 || u > 1.0 - 1e-3, $"实际 u={u}");
    }

    [Fact]
    public void Yaw360_中心_等同Yaw0()
    {
        var (u, v) = ProjectCenter(360, 0);
        Assert.Equal(0.5, u, 3);
        Assert.Equal(0.5, v, 3);
    }

    [Fact]
    public void Yaw270_中心_水平向左四分之一圈()
    {
        var (u, _) = ProjectCenter(270, 0);
        Assert.Equal(0.25, u, 3); // 0.5 - 90/360 = 0.25
    }

    [Fact]
    public void Pitch90_中心_采样北极顶行()
    {
        var (_, v) = ProjectCenter(0, 90);
        // 偶数尺寸中心像素存在亚像素偏移（~1/W），容差 1e-3
        Assert.True(v < 1e-3, $"向上看应采样到顶行 v≈0，实际 v={v}");
    }

    [Fact]
    public void PitchNeg90_中心_采样南极底行()
    {
        var (_, v) = ProjectCenter(0, -90);
        Assert.True(v > 1.0 - 1e-3, $"向下看应采样到底行 v≈1，实际 v={v}");
    }

    [Fact]
    public void 经度环绕_Yaw450等同Yaw90()
    {
        var (u1, _) = ProjectCenter(90, 0);
        var (u2, _) = ProjectCenter(450, 0); // 90 + 360
        Assert.Equal(u1, u2, 6);
    }

    [Fact]
    public void 中心右侧像素_采样更靠东()
    {
        var (uCenter, _) = EquirectProjection.ProjectPixel(W / 2, H / 2, W, H, Hfov, 0, 0);
        var (uRight, _) = EquirectProjection.ProjectPixel(W / 2 + 100, H / 2, W, H, Hfov, 0, 0);
        Assert.True(uRight > uCenter, "右侧像素应采样更靠东（u 更大）");
    }

    [Fact]
    public void 中心上方像素_采样更靠北()
    {
        var (_, vCenter) = EquirectProjection.ProjectPixel(W / 2, H / 2, W, H, Hfov, 0, 0);
        var (_, vUp) = EquirectProjection.ProjectPixel(W / 2, H / 2 - 100, W, H, Hfov, 0, 0);
        Assert.True(vUp < vCenter, "上方像素应采样更靠北（v 更小）");
    }

    [Fact]
    public void 水平FOV越大_同像素偏移对应的经度增量越大()
    {
        // 同一右侧像素，宽 FOV 应比窄 FOV 覆盖更大经度范围
        var (uNarrow, _) = EquirectProjection.ProjectPixel(W / 2 + 100, H / 2, W, H, 40, 0, 0);
        var (uWide, _) = EquirectProjection.ProjectPixel(W / 2 + 100, H / 2, W, H, 100, 0, 0);
        Assert.True(uWide > uNarrow, "宽 FOV 下同一像素偏移应覆盖更大经度");
    }
}
