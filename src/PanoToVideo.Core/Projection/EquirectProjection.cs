using PanoToVideo.Core.Parameters;

namespace PanoToVideo.Core.Projection;

/// <summary>
/// 等距柱状->透视投影数学（开发规划 §1.4）。
/// 对输出像素 (px,py)（左上原点）求 ERP 纹理坐标 (u,v) ∈ [0,1)。
/// 约定：v=0 顶行=北极(lat+90)；经度自动环绕（AddressU=Wrap 语义）。
/// 该纯函数同时作为 GPU shader 的参考实现与 CPU 回退的几何核心。
/// </summary>
public static class EquirectProjection
{
    public static (double U, double V) ProjectPixel(
        int px, int py, int width, int height,
        double horizontalFovDeg, double yawDeg, double pitchDeg)
    {
        // 像素归一化采用端点对齐（与 py360convert/v360 一致）：
        // 像素 i 映射到 [-1,1] 端点，使 FOV 精确覆盖 ±tan(fov/2)。
        var xNdc = width <= 1 ? 0.0 : (double)px / (width - 1) * 2.0 - 1.0;
        var yNdc = height <= 1 ? 0.0 : 1.0 - (double)py / (height - 1) * 2.0;

        var vFovDeg = FovMath.VerticalFov(horizontalFovDeg, width, height);
        var hRad = horizontalFovDeg * 0.5 * Math.PI / 180.0;
        var vRad = vFovDeg * 0.5 * Math.PI / 180.0;

        // 相机本地射线（+Z 朝前，+X 右，+Y 上）
        var dx = xNdc * Math.Tan(hRad);
        var dy = yNdc * Math.Tan(vRad);
        var dz = 1.0;

        // R = RotY(yaw) · RotX(pitch)；先 Pitch 后 Yaw
        var yawRad = yawDeg * Math.PI / 180.0;
        var pitchRad = pitchDeg * Math.PI / 180.0;
        var cp = Math.Cos(pitchRad);
        var sp = Math.Sin(pitchRad);
        var cy = Math.Cos(yawRad);
        var sy = Math.Sin(yawRad);

        // RotX(pitch)
        var ax = dx;
        var ay = dy * cp + dz * sp;
        var az = -dy * sp + dz * cp;

        // RotY(yaw)
        var wx = ax * cy + az * sy;
        var wy = ay;
        var wz = -ax * sy + az * cy;

        // 球面坐标
        var lon = Math.Atan2(wx, wz);                  // 经度 [-π,π]
        var len = Math.Sqrt(wx * wx + wy * wy + wz * wz);
        var lat = Math.Asin(wy / len);                  // 纬度 [-π/2,π/2]，向北为正

        // ERP 纹理坐标：u 经度环绕，v=0 为北极顶行
        var u = Wrap(lon / (2.0 * Math.PI) + 0.5);
        var v = 0.5 - lat / Math.PI;

        return (u, v);
    }

    /// <summary>把任意实数映射到 [0,1)，实现经度环绕。</summary>
    private static double Wrap(double x)
    {
        var f = x - Math.Floor(x);
        return f >= 1.0 ? 0.0 : f;
    }
}
