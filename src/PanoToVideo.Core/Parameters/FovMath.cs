namespace PanoToVideo.Core.Parameters;

/// <summary>
/// FOV 几何推导。水平 FOV 与输出宽高比推导垂直 FOV（开发规划 §1.4 语义修正）。
/// 推导：tan(vFov/2) = tan(hFov/2) × (height/width)
/// </summary>
public static class FovMath
{
    public static double VerticalFov(double horizontalFovDeg, int width, int height)
    {
        if (width <= 0)
            return double.NaN;

        var hRad = horizontalFovDeg * 0.5 * Math.PI / 180.0;
        var aspect = (double)height / width;
        return 2.0 * Math.Atan(Math.Tan(hRad) * aspect) * 180.0 / Math.PI;
    }
}
