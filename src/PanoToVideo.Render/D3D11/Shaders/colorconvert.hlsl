// BGRA/sRGB -> NV12/Rec.709 limited color conversion (ADR Q3)
// Input: BGRA source texture, full-screen quad
// Output: Y (SV_Target0, R8) + UV (SV_Target1, R8G8 interleaved Cb,Cr)
// Rec.709 limited range: Y 16-235, Cb/Cr 16-240
// Verify: white(1,1,1)->Y=235,Cb=128,Cr=128; red(1,0,0)->Y=63,Cb=102,Cr=240

Texture2D    g_srcTex : register(t0);
SamplerState g_sampler : register(s0);

cbuffer Params : register(b0)
{
    float g_srcW;
    float g_srcH;
    float g_pad0;
    float g_pad1;
};

struct VSInput { float2 pos : POSITION; };
struct VSOut { float4 pos : SV_Position; };
VSOut VSMain(VSInput input) { VSOut o; o.pos = float4(input.pos, 0.0, 1.0); return o; }

struct PSOut
{
    float4 y  : SV_Target0;
    float4 uv : SV_Target1;
};

PSOut PSMain(VSOut input)
{
    float2 uv = input.pos.xy / float2(g_srcW, g_srcH);
    float3 rgb = g_srcTex.Sample(g_sampler, uv).rgb;
    float r = rgb.r, g = rgb.g, b = rgb.b;
    // BT.709 normalized (Cb/Cr in [-0.5, 0.5])
    float y  = 0.2126 * r + 0.7152 * g + 0.0722 * b;
    float cb = -0.1146 * r - 0.3854 * g + 0.5000 * b;
    float cr = 0.5000 * r - 0.4542 * g - 0.0458 * b;
    // limited range 8-bit, normalized to [0,1] for R8/R8G8 UNorm RTV
    float yp  = (16.0 + 219.0 * y)  / 255.0;
    float cbp = (128.0 + 224.0 * cb) / 255.0;
    float crp = (128.0 + 224.0 * cr) / 255.0;
    PSOut o;
    o.y  = float4(yp, 0.0, 0.0, 1.0);
    o.uv = float4(cbp, crp, 0.0, 1.0);
    return o;
}
