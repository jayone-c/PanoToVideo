// 最小原生 D3D11 渲染探针：禁用背面剔除，验证原生 D3D11 渲染 work
#include <d3d11.h>
#include <d3dcompiler.h>
#include <stdio.h>
#include <string.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3dcompiler.lib")

static const char* kHlsl = R"(
struct VSInput { float2 pos : POSITION; };
struct VSOut { float4 pos : SV_Position; };
VSOut VSMain(VSInput input) { VSOut o; o.pos = float4(input.pos, 0.0, 1.0); return o; }
struct PSIn { float4 pos : SV_Position; };
float4 PSMain(PSIn i) : SV_Target { return float4(1.0, 0.0, 0.0, 1.0); }
)";

struct Vertex { float x, y; };

int main() {
    fprintf(stderr, "step1: create device\n");
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    D3D_FEATURE_LEVEL fl;
    HRESULT hr = D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, 0,
        NULL, 0, D3D11_SDK_VERSION, &device, &fl, &ctx);
    if (FAILED(hr)) { printf("D3D11CreateDevice failed: 0x%08X\n", (unsigned)hr); return 1; }
    printf("D3D11 device ok, FeatureLevel=0x%X\n", (unsigned)fl);

    fprintf(stderr, "step2: rtv\n");
    // offscreen RTV
    D3D11_TEXTURE2D_DESC tdesc = {};
    tdesc.Width = 64; tdesc.Height = 64; tdesc.MipLevels = 1; tdesc.ArraySize = 1;
    tdesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; tdesc.SampleDesc.Count = 1;
    tdesc.Usage = D3D11_USAGE_DEFAULT; tdesc.BindFlags = D3D11_BIND_RENDER_TARGET;
    ID3D11Texture2D* tex = nullptr;
    device->CreateTexture2D(&tdesc, NULL, &tex);
    ID3D11RenderTargetView* rtv = nullptr;
    device->CreateRenderTargetView(tex, NULL, &rtv);

    fprintf(stderr, "step3: shaders\n");
    // shaders
    ID3DBlob* vsb = nullptr; ID3DBlob* psb = nullptr;
    D3DCompile(kHlsl, strlen(kHlsl), "min.hlsl", NULL, NULL, "VSMain", "vs_5_0", 0, 0, &vsb, NULL);
    D3DCompile(kHlsl, strlen(kHlsl), "min.hlsl", NULL, NULL, "PSMain", "ps_5_0", 0, 0, &psb, NULL);
    ID3D11VertexShader* vs = nullptr; ID3D11PixelShader* ps = nullptr;
    device->CreateVertexShader(vsb->GetBufferPointer(), vsb->GetBufferSize(), NULL, &vs);
    device->CreatePixelShader(psb->GetBufferPointer(), psb->GetBufferSize(), NULL, &ps);

    fprintf(stderr, "step4: vb\n");
    // 顶点缓冲：大三角形覆盖全屏
    Vertex verts[3] = { {-1,-1}, {3,-1}, {-1,3} };
    D3D11_BUFFER_DESC bdesc = {};
    bdesc.ByteWidth = sizeof(verts); bdesc.BindFlags = D3D11_BIND_VERTEX_BUFFER; bdesc.Usage = D3D11_USAGE_DEFAULT;
    D3D11_SUBRESOURCE_DATA sdata = {}; sdata.pSysMem = verts;
    ID3D11Buffer* vb = nullptr;
    device->CreateBuffer(&bdesc, &sdata, &vb);

    fprintf(stderr, "step5: inputlayout\n");
    // input layout
    D3D11_INPUT_ELEMENT_DESC layout[] = { {"POSITION", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 0} };
    ID3D11InputLayout* il = nullptr;
    device->CreateInputLayout(layout, 1, vsb->GetBufferPointer(), vsb->GetBufferSize(), &il);

    fprintf(stderr, "step6: rasterizer\n");
    // 禁用背面剔除的 rasterizer state
    D3D11_RASTERIZER_DESC rdesc = {};
    rdesc.FillMode = D3D11_FILL_SOLID;
    rdesc.CullMode = D3D11_CULL_NONE;
    rdesc.FrontCounterClockwise = FALSE;
    rdesc.DepthClipEnable = TRUE;
    ID3D11RasterizerState* rs = nullptr;
    hr = device->CreateRasterizerState(&rdesc, &rs);
    fprintf(stderr, "step6 done rs=%p hr=0x%08X\n", (void*)rs, (unsigned)hr);

    fprintf(stderr, "step7: render\n");
    // render
    float black[4] = {0, 0, 0, 1};
    ctx->ClearRenderTargetView(rtv, black);
    ctx->OMSetRenderTargets(1, &rtv, NULL);
    D3D11_VIEWPORT vp = {}; vp.Width = 64; vp.Height = 64; vp.MaxDepth = 1.0f;
    ctx->RSSetViewports(1, &vp);
    ctx->RSSetState(rs);
    ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    ctx->IASetInputLayout(il);
    UINT stride = sizeof(Vertex); UINT offset = 0;
    ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
    ctx->VSSetShader(vs, NULL, 0);
    ctx->PSSetShader(ps, NULL, 0);
    ctx->Draw(3, 0);
    fprintf(stderr, "step7 done draw\n");

    fprintf(stderr, "step8: readback\n");
    // readback
    D3D11_TEXTURE2D_DESC sdesc = tdesc;
    sdesc.Usage = D3D11_USAGE_STAGING; sdesc.BindFlags = 0; sdesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* staging = nullptr;
    device->CreateTexture2D(&sdesc, NULL, &staging);
    ctx->CopyResource(staging, tex);
    D3D11_MAPPED_SUBRESOURCE mapped = {};
    ctx->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);
    unsigned char* px = (unsigned char*)mapped.pData;
    printf("左上像素 RGBA: (%d,%d,%d,%d) 期望红(255,0,0,255)\n", px[0], px[1], px[2], px[3]);
    printf("结果: %s\n", (px[0]==255 && px[1]==0 && px[2]==0) ? "D3D11渲染正常(背面剔除是根因)" : "D3D11渲染仍未生效");
    ctx->Unmap(staging, 0);

    staging->Release(); rs->Release(); il->Release(); vb->Release();
    ps->Release(); vs->Release(); psb->Release(); vsb->Release();
    rtv->Release(); tex->Release(); ctx->Release(); device->Release();
    return 0;
}
