namespace PanoToVideo.Core.Projection;

/// <summary>
/// CPU 参考渲染器：用 EquirectProjection 逐像素投影 + 双线性采样 ERP 纹理。
/// 同时作为 GPU shader 的参考实现、CPU 回退的几何核心、py360convert 对照的载体。
/// 经度环绕（AddressU=Wrap），纬度钳制（不环绕）。
/// </summary>
public static class EquirectRenderer
{
    public static Rgb[] RenderFrame(
        ReadOnlySpan<Rgb> erp, int erpW, int erpH,
        int outW, int outH, double hFovDeg, double yawDeg, double pitchDeg)
    {
        var frame = new Rgb[outW * outH];
        for (int py = 0; py < outH; py++)
        for (int px = 0; px < outW; px++)
        {
            var (u, v) = EquirectProjection.ProjectPixel(px, py, outW, outH, hFovDeg, yawDeg, pitchDeg);
            frame[py * outW + px] = SampleBilinear(erp, erpW, erpH, u, v);
        }
        return frame;
    }

    private static Rgb SampleBilinear(ReadOnlySpan<Rgb> erp, int w, int h, double u, double v)
    {
        var fx = u * w - 0.5;
        var fy = v * h - 0.5;
        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;

        // 经度环绕
        var x0w = ((x0 % w) + w) % w;
        var x1w = (((x0 + 1) % w) + w) % w;
        // 纬度钳制
        var y0c = Math.Clamp(y0, 0, h - 1);
        var y1c = Math.Clamp(y0 + 1, 0, h - 1);

        var p00 = erp[y0c * w + x0w];
        var p10 = erp[y0c * w + x1w];
        var p01 = erp[y1c * w + x0w];
        var p11 = erp[y1c * w + x1w];

        return new Rgb(
            Bilinear(p00.R, p10.R, p01.R, p11.R, tx, ty),
            Bilinear(p00.G, p10.G, p01.G, p11.G, tx, ty),
            Bilinear(p00.B, p10.B, p01.B, p11.B, tx, ty));
    }

    private static byte Bilinear(byte v00, byte v10, byte v01, byte v11, double tx, double ty)
    {
        var top = v00 + (v10 - v00) * tx;
        var bot = v01 + (v11 - v01) * tx;
        var val = top + (bot - top) * ty;
        return (byte)Math.Round(Math.Clamp(val, 0, 255));
    }
}
