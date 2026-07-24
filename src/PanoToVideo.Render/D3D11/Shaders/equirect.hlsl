// equirect->perspective projection shader (plan 1.4, ported from Core.EquirectProjection, PSNR 77-82dB vs py360convert)
// Input: full-screen quad via vertex buffer (4 verts, triangle strip)
// Pixel normalization: endpoint-aligned px/(W-1), matching py360convert/v360 convention
// Longitude wraps (AddressU=Wrap), latitude clamps (AddressV=Clamp)

cbuffer Params : register(b0)
{
    float g_outW;
    float g_outH;
    float g_tanHalfHFov;
    float g_tanHalfVFov;
    float g_yawRad;
    float g_pitchRad;
    float g_pad0;
    float g_pad1;
};

Texture2D    g_erpTex : register(t0);
SamplerState g_sampler : register(s0); // linear; AddressU=Wrap, AddressV=Clamp

#define PI 3.14159265358979323846

struct VSInput
{
    float2 pos : POSITION;
};

struct VSOutput
{
    float4 pos : SV_Position;
};

VSOutput VSMain(VSInput input)
{
    VSOutput o;
    o.pos = float4(input.pos, 0.0, 1.0);
    return o;
}

float4 PSMain(VSOutput input) : SV_Target
{
    // SV_Position in PS is pixel center (i+0.5); subtract 0.5 to get integer pixel index i,
    // matching Core/py360convert endpoint-aligned normalization i/(W-1)
    float px = input.pos.x - 0.5;
    float py = input.pos.y - 0.5;

    float xNdc = px / max(g_outW - 1.0, 1.0) * 2.0 - 1.0;
    float yNdc = 1.0 - py / max(g_outH - 1.0, 1.0) * 2.0;

    float dx = xNdc * g_tanHalfHFov;
    float dy = yNdc * g_tanHalfVFov;
    float dz = 1.0;

    // RotX(pitch)
    float cp = cos(g_pitchRad);
    float sp = sin(g_pitchRad);
    float ax = dx;
    float ay = dy * cp + dz * sp;
    float az = -dy * sp + dz * cp;

    // RotY(yaw)
    float cy = cos(g_yawRad);
    float sy = sin(g_yawRad);
    float wx = ax * cy + az * sy;
    float wy = ay;
    float wz = -ax * sy + az * cy;

    float lon = atan2(wx, wz);
    float len = sqrt(wx * wx + wy * wy + wz * wz);
    // M3 fix: clamp to [-1,1] to avoid NaN from asin when floating-point error
    // makes wy/len slightly exceed +/-1 (e.g. pitch near +/-90 degrees)
    float lat = asin(clamp(wy / len, -1.0, 1.0));

    // ERP tex coord: u wraps (longitude), v=0 = north pole top row
    float u = lon / (2.0 * PI) + 0.5;
    u = u - floor(u);
    float v = 0.5 - lat / PI;

    float3 color = g_erpTex.Sample(g_sampler, float2(u, v)).rgb;
    return float4(color, 1.0);
}
