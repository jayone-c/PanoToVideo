using PanoToVideo.Core.Projection;
using PanoToVideo.Render.D3D11;
using PanoToVideo.Render.DeviceProbe;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using Format = Vortice.DXGI.Format;

namespace PanoToVideo.Tests.Projection;

/// <summary>
/// NV12 UV 平面边缘色度正确性 TDD 测试（代码审查 M1/M2）。
/// M1: UV 视口应半宽半高(outW/2, outH/2)，非全宽半高。
/// M2: colorconvert 应独立 Clamp sampler，BGRA 右边缘不应经 Wrap 从左边缘串色。
/// 红阶段：构造左半绿/右半红测试图，右边缘 UV Cr 应为红对应(≈240)，
///         若 M2 串色则 Cr 被左边缘绿色污染偏低；若 M1 视口错则 UV 越界。
/// </summary>
[Trait("Category", "GeometryReference")]
public class Nv12EdgeChromaTests : IDisposable
{
    private static readonly ID3D11Device s_device;
    private static readonly EquirectPipeline s_pipeline;

    static Nv12EdgeChromaTests()
    {
        using var probe = new DeviceProbe();
        var preferred = probe.Probe().Preferred ?? throw new InvalidOperationException("无合格设备");
        D3D11CreateDevice(preferred.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out s_device, out _, out _).CheckError();
        s_pipeline = new EquirectPipeline(s_device);
    }

    /// <summary>构造左半绿右半红的测试图（宽偶数），右边缘是纯红。</summary>
    private static ID3D11ShaderResourceView CreateHalfHalfSrv(int w, int h, out byte[] rgba)
    {
        rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            if (x < w / 2) { rgba[i] = 0; rgba[i + 1] = 255; rgba[i + 2] = 0; }   // 左半绿
            else { rgba[i] = 255; rgba[i + 1] = 0; rgba[i + 2] = 0; }              // 右半红
            rgba[i + 3] = 255;
        }
        return s_pipeline.UploadErpTexture(rgba, w, h);
    }

    [Fact]
    public void 右边缘UV色度不被左边缘串色_红色对应Cr高()
    {
        // 左半绿/右半红测试图，右边缘纯红
        const int w = 64, h = 64;
        var srv = CreateHalfHalfSrv(w, h, out _);

        // 用 ConvertBgraToYuv（双 RTV 验证路径，含 colorconvert shader）
        var (yPlane, uvPlane) = s_pipeline.ConvertBgraToYuv(srv, w, h);

        // UV 平面半高：uvPlane 每像素 2 字节(Cb,Cr)，宽 w，高 h/2
        // 检查右边缘列(x=w-1)的 Cr 应为红色对应(~240)，不应被左边缘绿色污染(绿色 Cr≈26)
        // UV 是 4:2:0，水平也半宽？--ConvertBgraToYuv 当前用双 RTV 全分辨率，UV plane 是 w*(h) 个像素?
        // 实际 ConvertBgraToYuv 用 PSMain 双输出全分辨率（非 PSUv 半高），UV plane 维度 = w*h*2
        // 取右边缘列 x=w-1 的 Cr（uvPlane[(y*w + x)*2 + 1]）
        int rightCr = uvPlane[((h / 2) * w + (w - 1)) * 2 + 1];
        int leftCr = uvPlane[((h / 2) * w + 0) * 2 + 1];

        // 红色 Cr ≈ 240（Rec.709 limited），绿色 Cr ≈ 26
        Assert.True(rightCr > 200, $"右边缘 Cr={rightCr} 应为红色(>200)，疑似被左边缘绿色串色(M2)");
        Assert.True(leftCr < 60, $"左边缘 Cr={leftCr} 应为绿色(<60)");
    }

    [Fact]
    public void NV12平面回读右边缘Cr正确_不被左边缘串色()
    {
        // 经 RenderFrameToNv12 + ReadNv12ToBytes（真 NV12 布局，UV 半高半宽）
        const int w = 64, h = 64;
        var srv = CreateHalfHalfSrv(w, h, out _);
        using var nv12 = s_pipeline.RenderFrameToNv12(srv, w, h, w, h, 75, 0, 0);
        var nv12Bytes = s_pipeline.ReadNv12ToBytes(nv12, w, h);

        // NV12 布局：Y(w*h) + UV(w * h/2)，UV 半高，每行 w 字节(CbCr 交错)
        // UV 行宽 = w（CbCr 交错，每像素 2 字节但水平下采样后列数 = w/2）
        // 取右边缘 UV Cr（最右一个色度块的 Cr）
        int ySize = w * h;
        int uvRows = h / 2;
        int uvCols = w / 2;
        // 最右列色度块的 Cr
        int rightCr = nv12Bytes[ySize + (uvRows / 2) * w + (uvCols - 1) * 2 + 1];
        int leftCr = nv12Bytes[ySize + (uvRows / 2) * w + 0 * 2 + 1];

        Assert.True(rightCr > 200, $"NV12右边缘 Cr={rightCr} 应为红色(>200)，疑似串色(M1/M2)");
        Assert.True(leftCr < 60, $"NV12左边缘 Cr={leftCr} 应为绿色(<60)");
    }

    public void Dispose() { }
}
