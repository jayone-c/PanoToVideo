// Asteroid intro projection + transition to perspective (plan 1.5, stage3 task4)
// Independent spherical projection (bottom pole looking down), NOT setting perspective FOV to 180.
// weight=1: pure asteroid (little planet); weight=0: pure perspective; 0<weight<1: blend.
// Reuses same D3D11 pipeline, controlled by g_asteroidWeight uniform (no second render path).

cbuffer Params : register(b0)
{
    float g_outW;
    float g_outH;
    float g_tanHalfHFov;
    float g_tanHalfVFov;
    float g_yawRad;
    float g_pitchRad;
    float g_asteroidWeight;
    float g_pad1;
};

Texture2D    g_erpTex : register(t0);
SamplerState g_sampler : register(s0);

#define PI 3.14159265358979323846

struct VSInput { float2 pos : POSITION; };
struct VSOut { float4 pos : SV_Position; };
VSOut VSMain(VSInput input) { VSOut o; o.pos = float4(input.pos, 0.0, 1.0); return o; }

// Perspective projection direction (same as equirect.hlsl)
float3 perspectiveDir(float2 ndc)
{
    float dx = ndc.x * g_tanHalfHFov;
    float dy = ndc.y * g_tanHalfVFov;
    float dz = 1.0;
    float cp = cos(g_pitchRad), sp = sin(g_pitchRad);
    float ax = dx;
    float ay = dy * cp + dz * sp;
    float az = -dy * sp + dz * cp;
    float cy = cos(g_yawRad), sy = sin(g_yawRad);
    float wx = ax * cy + az * sy;
    float wy = ay;
    float wz = -ax * sy + az * cy;
    return normalize(float3(wx, wy, wz));
}

// Asteroid (little planet) projection direction: bottom pole looking down.
// Center pixel = nadir (looking straight down, lat=-90); edge = horizon (lat=0).
// Output pixel -> polar (r, theta): r in [0,1] maps to latitude lat = -(1-r)*90deg (center=-90, edge=0)
// theta maps to longitude lon = theta + yaw (rotates the planet)
float3 asteroidDir(float2 ndc)
{
    // ndc in [-1,1], center (0,0)
    float r = length(ndc);
    // latitude: center=-PI/2 (nadir), edge=0 (horizon)
    float lat = -(1.0 - saturate(r)) * (PI / 2.0);
    // longitude: angle of ndc, rotated by yaw
    float lon = atan2(ndc.x, -ndc.y) + g_yawRad;
    // direction vector from (lon, lat)
    float cl = cos(lat);
    return normalize(float3(cl * cos(lon), cl * sin(lon), sin(lat)));
}

float4 PSMain(VSOut input) : SV_Target
{
    float px = input.pos.x - 0.5;
    float py = input.pos.y - 0.5;
    float xNdc = px / (g_outW - 1.0) * 2.0 - 1.0;
    float yNdc = 1.0 - py / (g_outH - 1.0) * 2.0;

    // blend directions by asteroid weight, then sample
    // H8 fix: weight=0.5 center dirPersp=(0,0,1) and dirAst=(0,0,-1) lerp to zero vector,
    // normalize yields NaN. Fall back to perspective dir when blended length near zero.
    float3 dirPersp = perspectiveDir(float2(xNdc, yNdc));
    float3 dirAst = asteroidDir(float2(xNdc, yNdc));
    float3 blended = lerp(dirPersp, dirAst, g_asteroidWeight);
    float3 dir = length(blended) < 1e-6 ? dirPersp : normalize(blended);

    float lon = atan2(dir.x, dir.z);
    float lat = asin(dir.y);
    float u = lon / (2.0 * PI) + 0.5;
    u = u - floor(u);
    float v = 0.5 - lat / PI;

    return float4(g_erpTex.Sample(g_sampler, float2(u, v)).rgb, 1.0);
}
