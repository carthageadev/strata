#ifndef STRATA_TILES_INCLUDED
#define STRATA_TILES_INCLUDED

#define ST_TEX 16.0

float stHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float stVN(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = stHash(i), b = stHash(i + float2(1.0, 0.0)), c = stHash(i + float2(0.0, 1.0)), d = stHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float4 stRim(float along, float row, int style)
{
    float span = floor(along / 32.0), a = along - span * 32.0;
    float h1 = stHash(float2(span, 3.1)), h2 = stHash(float2(span, 7.7));
    float c1, c2;
    if (style == 2) { c1 = 14.0 + floor(h1 * 5.0); c2 = 32.0; }
    else { c1 = 9.0 + floor(h1 * 4.0); c2 = c1 + 9.0 + floor(h2 * 4.0); }
    float k = a < c1 ? 0.0 : (a < c2 ? 1.0 : 2.0);
    float s0 = k == 0.0 ? 0.0 : (k == 1.0 ? c1 : c2);
    float s1 = k == 0.0 ? c1 : (k == 1.0 ? c2 : 32.0);
    float L = s1 - s0, u = a - s0;
    float id = span * 3.0 + k;
    float tone = stHash(float2(id, style == 2 ? 11.3 : 5.1));
    float depth = style == 2 ? 4.0 : 6.0 - step(0.6, stHash(float2(id, 9.7)));
    if (row >= depth) return float4(tone, 2.0, 0.0, 0.0);
    float cu = (u + 0.5) / L * 2.0 - 1.0, co = 1.0 - (row + 0.5) / depth * 2.0;
    float kind = u < 0.5 ? 1.0 : 0.0;
    if (style == 3 && row > depth - 1.5 && (u < 1.5 || u > L - 1.5)) kind = 2.0;
    if (style == 3 && row > depth - 2.5 && (u < 0.5 || u > L - 0.5)) kind = 2.0;
    return float4(tone, kind, cu, co);
}

float3 stRimColor(float4 r, int style, float3 copingDark, float3 copingLight, float3 stonesDark, float3 stonesLight)
{
    float3 c = style == 2 ? lerp(copingDark, copingLight, r.x) : lerp(stonesDark, stonesLight, r.x);
    return r.y > 0.5 ? c * 0.35 : c;
}

float3 stEdgeColor(float4 r, float3 dark, float3 light, bool textured, float3 texCol)
{
    float3 c = textured ? texCol * lerp(0.82, 1.12, r.x) : lerp(dark, light, r.x);
    return r.y > 0.5 ? c * 0.35 : c;
}

float3 ST_ToGrid(float3 v) { return float3(v.x, v.y, -v.z); }

#endif
