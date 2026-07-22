using System.Runtime.InteropServices;
using PanoToVideo.Core;
using PanoToVideo.Core.Parameters;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Format = Vortice.DXGI.Format;

namespace PanoToVideo.Render.D3D11;

/// <summary>
/// equirect->perspective GPU 渲染管线（开发规划 §1.3、§1.4）。
/// shader 运行时编译（Vortice.D3DCompiler，源码随 Content 复制到 Shaders/）；
/// ERP 图上传为 SRV；逐帧渲染到 R8G8B8A8 RenderTarget 并回读。
/// 数学移植 Core.EquirectProjection，已与 py360convert 对照 PSNR 49-54dB。
/// 关键修复：禁用背面剔除（CCW 大三角形）；SV_Position 像素中心(i+0.5)减0.5对齐端点归一化。
/// </summary>
public sealed class EquirectPipeline : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Buffer _paramsBuffer;
    private readonly ID3D11Buffer _vertexBuffer;
    private readonly ID3D11InputLayout _inputLayout;
    private readonly ID3D11RasterizerState _rasterizerState;
    private readonly ID3D11VertexShader _ccVs;
    private readonly ID3D11PixelShader _ccPs;
    private readonly ID3D11PixelShader _asteroidPs;

    // cbuffer Params：与 equirect/asteroid.hlsl 的 cbuffer 布局一致（8 float = 32 字节）
    // 第7位在 equirect shader 为 pad，在 asteroid shader 为 g_asteroidWeight（equirect 不读）
    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private struct Params
    {
        public float OutW, OutH, TanHalfHFov, TanHalfVFov;
        public float YawRad, PitchRad, AsteroidWeight, Pad1;
    }

    public ID3D11Device Device => _device;

    public EquirectPipeline(ID3D11Device device)
    {
        _device = device;
        _context = device.ImmediateContext;

        // shader 运行时编译（Vortice.D3DCompiler），源码从输出目录 Shaders/equirect.hlsl 读取
        string shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "equirect.hlsl");
        var vsBytecode = CompileShader(shaderPath, "VSMain", "vs_5_0");
        var psBytecode = CompileShader(shaderPath, "PSMain", "ps_5_0");
        _vs = device.CreateVertexShader(vsBytecode.Span);
        _ps = device.CreatePixelShader(psBytecode.Span);

        _sampler = device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,   // 经度环绕
            AddressV = TextureAddressMode.Clamp,  // 纬度钳制
            AddressW = TextureAddressMode.Clamp,
            MinLOD = 0,
            MaxLOD = float.MaxValue,
        });

        _paramsBuffer = device.CreateBuffer(new BufferDescription
        {
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ConstantBuffer,
            ByteWidth = 32,
            CPUAccessFlags = CpuAccessFlags.None,
        });

        // 全屏大三角形（TriangleList，3 顶点覆盖 [-1,3]×[-1,3]）
        ReadOnlySpan<float> quad = [
            -1f, -1f,
             3f, -1f,
            -1f,  3f,
        ];
        _vertexBuffer = device.CreateBuffer(quad, BindFlags.VertexBuffer);

        var layout = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
        };
        _inputLayout = device.CreateInputLayout(layout, vsBytecode.Span);

        // 禁用背面剔除：大三角形 CCW winding 会被默认 CullMode.Back 剔除，必须显式 CullNone
        _rasterizerState = device.CreateRasterizerState(new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthClipEnable = true,
        });

        // 颜色转换 shader（BGRA->NV12 Y/UV，ADR Q3）
        string ccPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "colorconvert.hlsl");
        var ccVsBc = CompileShader(ccPath, "VSMain", "vs_5_0");
        var ccPsBc = CompileShader(ccPath, "PSMain", "ps_5_0");
        _ccVs = device.CreateVertexShader(ccVsBc.Span);
        _ccPs = device.CreatePixelShader(ccPsBc.Span);

        // 小行星 shader（独立球面投影 + 透视过渡，复用 equirect 的 VS）
        string astPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "asteroid.hlsl");
        var astPsBc = CompileShader(astPath, "PSMain", "ps_5_0");
        _asteroidPs = device.CreatePixelShader(astPsBc.Span);
    }

    /// <summary>把 RGBA 像素上传为 ERP 纹理 SRV（R8G8B8A8_UNorm）。</summary>
    public ID3D11ShaderResourceView UploadErpTexture(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
        };

        var handle = GCHandle.Alloc(rgba.ToArray(), GCHandleType.Pinned);
        try
        {
            var box = new SubresourceData { DataPointer = handle.AddrOfPinnedObject(), RowPitch = (uint)(width * 4) };
            var texture = _device.CreateTexture2D(desc, new[] { box });
            return _device.CreateShaderResourceView(texture);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// 把 BGRA 纹理转换为 NV12 Y/UV（Rec.709 limited range，ADR Q3）。
    /// 返回 Y（R8，行优先）和 UV（R8G8，行优先，全分辨率验证用；真 NV12 布局在 Q2 接 MF 时处理）。
    /// </summary>
    public (byte[] Y, byte[] Uv) ConvertBgraToYuv(ID3D11ShaderResourceView bgraSrv, int width, int height)
    {
        var yDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
        };
        using var yTex = _device.CreateTexture2D(yDesc);
        using var yRtv = _device.CreateRenderTargetView(yTex);

        var uvDesc = yDesc;
        uvDesc.Format = Format.R8G8_UNorm;
        using var uvTex = _device.CreateTexture2D(uvDesc);
        using var uvRtv = _device.CreateRenderTargetView(uvTex);

        var p = new Params { OutW = width, OutH = height };
        _context.UpdateSubresource(ref p, _paramsBuffer);

        _context.ClearRenderTargetView(yRtv, new Color4(0, 0, 0, 1));
        _context.ClearRenderTargetView(uvRtv, new Color4(0, 0, 0, 1));
        _context.OMSetRenderTargets(new[] { yRtv, uvRtv }, null);
        _context.RSSetViewport(new Viewport(width, height));
        _context.RSSetState(_rasterizerState);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetVertexBuffer(0, _vertexBuffer, 8);
        _context.VSSetShader(_ccVs);
        _context.PSSetShader(_ccPs);
        _context.PSSetShaderResources(0, new[] { bgraSrv });
        _context.PSSetSamplers(0, new[] { _sampler });
        _context.PSSetConstantBuffers(0, new[] { _paramsBuffer });
        _context.Draw(3, 0);

        var yBytes = ReadBack(yTex, width, height, 1);
        var uvBytes = ReadBack(uvTex, width, height, 2);
        return (yBytes, uvBytes);
    }

    private byte[] ReadBack(ID3D11Texture2D src, int width, int height, int bytesPerPixel)
    {
        var desc = src.Description;
        var stagingDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        };
        using var staging = _device.CreateTexture2D(stagingDesc);
        _context.CopyResource(staging, src);
        var mapped = _context.Map(staging, 0, MapMode.Read);
        try
        {
            var bytes = new byte[width * height * bytesPerPixel];
            int srcRow = (int)mapped.RowPitch;
            for (int y = 0; y < height; y++)
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * srcRow), bytes, y * width * bytesPerPixel, width * bytesPerPixel);
            return bytes;
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    /// <summary>
    /// 渲染单帧并零拷贝转换为 NV12 平面纹理（ADR Q2/Q3 完整链路）。
    /// equirect shader 渲染 BGRA -> colorconvert shader 转 NV12（Y plane + UV plane）。
    /// 返回的 NV12 纹理可直接喂 MfH264Encoder.SubmitFrame 零拷贝编码。
    /// </summary>
    public ID3D11Texture2D RenderFrameToNv12(
        ID3D11ShaderResourceView erpSrv, int erpW, int erpH,
        int outW, int outH, double hFovDeg, double yawDeg, double pitchDeg)
    {
        // 1. equirect 渲染到 BGRA RT
        var bgraDesc = new Texture2DDescription
        {
            Width = (uint)outW, Height = (uint)outH, MipLevels = 1, ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        };
        using var bgraTex = _device.CreateTexture2D(bgraDesc);
        using var bgraRtv = _device.CreateRenderTargetView(bgraTex);
        using var bgraSrv = _device.CreateShaderResourceView(bgraTex);

        var vFovDeg = FovMath.VerticalFov(hFovDeg, outW, outH);
        var p = new Params
        {
            OutW = outW, OutH = outH,
            TanHalfHFov = (float)Math.Tan(hFovDeg * 0.5 * Math.PI / 180.0),
            TanHalfVFov = (float)Math.Tan(vFovDeg * 0.5 * Math.PI / 180.0),
            YawRad = (float)(yawDeg * Math.PI / 180.0),
            PitchRad = (float)(pitchDeg * Math.PI / 180.0),
        };
        _context.UpdateSubresource(ref p, _paramsBuffer);

        _context.ClearRenderTargetView(bgraRtv, new Color4(0, 0, 0, 1));
        _context.OMSetRenderTargets(bgraRtv, null);
        _context.RSSetViewport(new Viewport(outW, outH));
        _context.RSSetState(_rasterizerState);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetVertexBuffer(0, _vertexBuffer, 8);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_ps);
        _context.PSSetShaderResources(0, new[] { erpSrv });
        _context.PSSetSamplers(0, new[] { _sampler });
        _context.PSSetConstantBuffers(0, new[] { _paramsBuffer });
        _context.Draw(3, 0);

        // 2. colorconvert: BGRA -> NV12 平面纹理（Y R8 + UV R8G8 plane）
        var nv12Desc = new Texture2DDescription
        {
            Width = (uint)outW, Height = (uint)outH, MipLevels = 1, ArraySize = 1,
            Format = Format.NV12, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        };
        var nv12Tex = _device.CreateTexture2D(nv12Desc);
        using var yRtv = _device.CreateRenderTargetView(nv12Tex, new RenderTargetViewDescription
        {
            Format = Format.R8_UNorm, ViewDimension = RenderTargetViewDimension.Texture2D,
            Texture2D = new Texture2DRenderTargetView { MipSlice = 0 },
        });
        using var uvRtv = _device.CreateRenderTargetView(nv12Tex, new RenderTargetViewDescription
        {
            Format = Format.R8G8_UNorm, ViewDimension = RenderTargetViewDimension.Texture2D,
            Texture2D = new Texture2DRenderTargetView { MipSlice = 0 },
        });

        // colorconvert shader 用同一 cbuffer（OutW/OutH 作为 srcW/srcH 采样 BGRA）
        _context.UpdateSubresource(ref p, _paramsBuffer);
        _context.ClearRenderTargetView(yRtv, new Color4(0, 0, 0, 1));
        _context.ClearRenderTargetView(uvRtv, new Color4(0, 0, 0, 1));
        _context.OMSetRenderTargets(new[] { yRtv, uvRtv }, null);
        _context.RSSetViewport(new Viewport(outW, outH));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetVertexBuffer(0, _vertexBuffer, 8);
        _context.VSSetShader(_ccVs);
        _context.PSSetShader(_ccPs);
        _context.PSSetShaderResources(0, new[] { bgraSrv });
        _context.PSSetSamplers(0, new[] { _sampler });
        _context.PSSetConstantBuffers(0, new[] { _paramsBuffer });
        _context.Draw(3, 0);

        return nv12Tex;
    }

    /// <summary>渲染单帧到 R8G8B8A8 RenderTarget 并回读 RGBA 字节（行优先）。
    /// asteroidWeight=0 纯透视（默认）；>0 时混合小行星投影（阶段3 §1.5）。</summary>
    public byte[] RenderFrameToRgba(
        ID3D11ShaderResourceView erpSrv, int erpW, int erpH,
        int outW, int outH, double hFovDeg, double yawDeg, double pitchDeg,
        double asteroidWeight = 0.0)
    {
        var rtDesc = new Texture2DDescription
        {
            Width = (uint)outW,
            Height = (uint)outH,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        };
        using var rtTexture = _device.CreateTexture2D(rtDesc);
        using var rtv = _device.CreateRenderTargetView(rtTexture);

        var vFovDeg = FovMath.VerticalFov(hFovDeg, outW, outH);
        var p = new Params
        {
            OutW = outW,
            OutH = outH,
            TanHalfHFov = (float)Math.Tan(hFovDeg * 0.5 * Math.PI / 180.0),
            TanHalfVFov = (float)Math.Tan(vFovDeg * 0.5 * Math.PI / 180.0),
            YawRad = (float)(yawDeg * Math.PI / 180.0),
            PitchRad = (float)(pitchDeg * Math.PI / 180.0),
            AsteroidWeight = (float)asteroidWeight,
        };
        _context.UpdateSubresource(ref p, _paramsBuffer);

        _context.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
        _context.OMSetRenderTargets(rtv, null);
        _context.RSSetViewport(new Viewport(outW, outH));
        _context.RSSetState(_rasterizerState);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetVertexBuffer(0, _vertexBuffer, 8);
        _context.VSSetShader(_vs);
        // asteroidWeight>0 用小行星 shader（含透视/小行星混合），=0 用纯透视 equirect shader
        _context.PSSetShader(asteroidWeight > 0 ? _asteroidPs : _ps);
        _context.PSSetShaderResources(0, new[] { erpSrv });
        _context.PSSetSamplers(0, new[] { _sampler });
        _context.PSSetConstantBuffers(0, new[] { _paramsBuffer });
        _context.Draw(3, 0);

        // 回读：复制到 staging 纹理
        var stagingDesc = rtDesc;
        stagingDesc.Usage = ResourceUsage.Staging;
        stagingDesc.BindFlags = BindFlags.None;
        stagingDesc.CPUAccessFlags = CpuAccessFlags.Read;
        using var staging = _device.CreateTexture2D(stagingDesc);
        _context.CopyResource(staging, rtTexture);

        var mapped = _context.Map(staging, 0, MapMode.Read);
        try
        {
            var rgba = new byte[outW * outH * 4];
            int srcRow = (int)mapped.RowPitch;
            var srcPtr = mapped.DataPointer;
            for (int y = 0; y < outH; y++)
            {
                Marshal.Copy(IntPtr.Add(srcPtr, y * srcRow), rgba, y * outW * 4, outW * 4);
            }
            return rgba;
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    private static ReadOnlyMemory<byte> CompileShader(string path, string entryPoint, string profile)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"shader 源码未找到: {path}");
        string hlsl = File.ReadAllText(path);
        try
        {
            return Compiler.Compile(hlsl, entryPoint, Path.GetFileName(path), profile, ShaderFlags.OptimizationLevel3);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"shader {entryPoint} 编译失败: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _asteroidPs.Dispose();
        _ccPs.Dispose();
        _ccVs.Dispose();
        _rasterizerState.Dispose();
        _inputLayout.Dispose();
        _vertexBuffer.Dispose();
        _paramsBuffer.Dispose();
        _sampler.Dispose();
        _ps.Dispose();
        _vs.Dispose();
    }
}
