// BGRA/sRGB -> NV12/Rec.709 limited color conversion (ADR Q3, stage4 fix)
// NV12: Y plane full-resolution (PlaneSlice=0), UV plane half-height 4:2:0 (PlaneSlice=1)
// Rec.709 limited range: Y 16-235, Cb/Cr 16-240
// Verify: white(1,1,1)->Y=235,Cb=128,Cr=128; red(1,0,0)->Y=63,Cb=102,Cr=240
//
// PSMain: dual-output (full-res Y + UV), used by ConvertBgraToYuv color math validation
// PSY: Y plane full-resolution output (single RTV, PlaneSlice=0)
// PSUv: UV plane half-height output with 2x2 chroma downsampling (single RTV, PlaneSlice=1)

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

// BT.709 limited range conversion helpers
float ToYp(float3 rgb) { float y = 0.2126 * rgb.r + 0.7152 * rgb.g + 0.0722 * rgb.b; return (16.0 + 219.0 * y) / 255.0; }
float ToCbp(float3 rgb) { float cb = -0.1146 * rgb.r - 0.3854 * rgb.g + 0.5000 * rgb.b; return (128.0 + 224.0 * cb) / 255.0; }
float ToCrp(float3 rgb) { float cr = 0.5000 * rgb.r - 0.4542 * rgb.g - 0.0458 * rgb.b; return (128.0 + 224.0 * cr) / 255.0; }

struct PSOut
{
    float4 y  : SV_Target0;
    float4 uv : SV_Target1;
};

// Dual-output full-resolution (for ConvertBgraToYuv validation)
PSOut PSMain(VSOut input)
{
    float2 uv = input.pos.xy / float2(g_srcW, g_srcH);
    float3 rgb = g_srcTex.Sample(g_sampler, uv).rgb;
    PSOut o;
    o.y  = float4(ToYp(rgb), 0.0, 0.0, 1.0);
    o.uv = float4(ToCbp(rgb), ToCrp(rgb), 0.0, 1.0);
    return o;
}

// Y plane full-resolution (PlaneSlice=0, viewport = srcW x srcH)
float4 PSY(VSOut input) : SV_Target
{
    float2 uv = input.pos.xy / float2(g_srcW, g_srcH);
    float3 rgb = g_srcTex.Sample(g_sampler, uv).rgb;
    return float4(ToYp(rgb), 0.0, 0.0, 1.0);
}

// UV plane half-height 4:2:0 (PlaneSlice=1, viewport = srcW x srcH/2)
// Each UV output pixel samples a 2x2 block of BGRA and averages -> CbCr
float4 PSUv(VSOut input) : SV_Target
{
    // input.pos is UV-plane pixel center (i+0.5). Map to BGRA 2x2 block top-left center.
    float2 baseUv = (input.pos.xy * 2.0 - 0.5) / float2(g_srcW, g_srcH);
    float2 dx = float2(1.0 / g_srcW, 0.0);
    float2 dy = float2(0.0, 1.0 / g_srcH);
    float3 rgb00 = g_srcTex.Sample(g_sampler, baseUv).rgb;
    float3 rgb10 = g_srcTex.Sample(g_sampler, baseUv + dx).rgb;
    float3 rgb01 = g_srcTex.Sample(g_sampler, baseUv + dy).rgb;
    float3 rgb11 = g_srcTex.Sample(g_sampler, baseUv + dx + dy).rgb;
    float3 rgb = (rgb00 + rgb10 + rgb01 + rgb11) * 0.25;
    return float4(ToCbp(rgb), ToCrp(rgb), 0.0, 1.0);
}
