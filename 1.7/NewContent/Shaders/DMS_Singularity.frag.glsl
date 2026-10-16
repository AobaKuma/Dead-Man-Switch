#version 450

// 零點能奇點：扭曲背後畫面的重力透鏡、旋轉的吸積盤、光子環與全黑的事件視界，全部程序產生，不需要貼圖。
// 畫在 MeshPool.plane10 上；DMS.CompSingularityGraphic 以 property block 每次繪製時送入
// _Color.a（開啟程度 0..1）、_AgeSecs（視覺時鐘）與 _Seed（每棟建築的相位）。
// Zero-point singularity: gravitational lensing of the scene behind it, a swirling accretion disk, a photon ring
// and a pitch-black event horizon, all procedural (no texture). Drawn on MeshPool.plane10; DMS.CompSingularityGraphic
// feeds _Color.a (how far the hole is open, 0..1), _AgeSecs (visual clock) and _Seed (per-building phase) through a
// property block on every draw.

layout(location = 1) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;     // @color @default = vec4(0.45, 0.8, 1.0, 1.0)
    float _AgeSecs;
    float _Seed;
    float _Horizon;  // @default = 0.17
    float _Lensing;  // @default = 1.0
} u;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _GrabDistortionBackground;

float hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float valueNoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 w = f * f * (3.0 - 2.0 * f);
    float a = hash12(i);
    float b = hash12(i + vec2(1.0, 0.0));
    float c = hash12(i + vec2(0.0, 1.0));
    float d = hash12(i + vec2(1.0, 1.0));
    return mix(mix(a, b, w.x), mix(c, d, w.x), w.y);
}

float fbm(vec2 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * valueNoise(p);
        p = p * 2.07 + vec2(17.1, 9.3);
        a *= 0.5;
    }
    return v;
}

vec2 rotate(vec2 p, float a)
{
    float c = cos(a);
    float s = sin(a);
    return vec2(c * p.x - s * p.y, s * p.x + c * p.y);
}

// 吸積盤紋理：內圈轉得比外圈快（近似克卜勒）。兩個相差半週期的相位交叉淡化，剪切才不會無限累積成細條紋。
// 尺度與角速度都以穩定的 _Horizon 計算，開啟過程中紋理不會跟著縮放閃爍。
// Accretion disk pattern. Inner orbits turn faster than outer ones (roughly Keplerian); two phases half a cycle
// apart are cross-faded so the shear never winds up into fine stripes. Scale and angular speed use the steady
// _Horizon, so the pattern does not zoom or shimmer while the hole opens.
float diskPattern(vec2 p, float r, float t)
{
    const float cycle = 3.0;
    float rh = max(u._Horizon, 1e-3);
    float omega = 1.4 * pow(rh / max(r, 1e-3), 1.5);
    float ph1 = fract(t / cycle);
    float ph2 = fract(t / cycle + 0.5);
    vec2 q = p / rh * 2.8;
    float n1 = fbm(rotate(q, -omega * ph1 * cycle) + u._Seed);
    float n2 = fbm(rotate(q, -omega * ph2 * cycle) + u._Seed + 31.7);
    float w1 = 1.0 - abs(ph1 * 2.0 - 1.0);
    return mix(n2, n1, w1);
}

void main()
{
    vec2 p = vUv * 2.0 - 1.0;
    float r = length(p);

    // p 每一單位對應多少像素（含正負號，所以畫面緩衝的 y 軸翻轉自動處理，縮放鏡頭時扭曲量也維持在世界尺度）。
    // 導數要在任何分支之前取。
    // Pixels per unit of p, sign included: the framebuffer's flipped y is handled for free and the warp stays in
    // world scale at any zoom. Derivatives are taken before any branching.
    float dx = dFdx(p.x);
    float dy = dFdy(p.y);
    vec2 pixelsPerUnit = vec2(1.0 / (abs(dx) < 1e-6 ? 1e-6 : dx), 1.0 / (abs(dy) < 1e-6 ? 1e-6 : dy));

    float open = clamp(u._Color.a, 0.0, 1.0);
    float t = u._AgeSecs;
    float pulse = 1.0 + 0.045 * sin(t * 2.3 + u._Seed) + 0.02 * sin(t * 5.9 + u._Seed * 1.7);
    // 開啟時視界從一個點長大。The horizon grows out of a point as the hole opens.
    float rh = max(u._Horizon * pulse * smoothstep(0.0, 1.0, open), 1e-4);
    float rimFade = 1.0 - smoothstep(0.55, 1.0, r);

    // 重力透鏡（點質量透鏡方程 β = θ − θE²/θ）：此像素顯示更靠近中心處的背景，背後的景物被往外推、拉成一圈。
    // Gravitational lensing, point-lens equation β = θ − θE²/θ: this pixel shows the scene from closer to the
    // centre, so whatever lies behind the hole is pushed outwards and smeared into a ring.
    float thetaE = rh * 1.5 * u._Lensing;
    float rs = max(r, rh * 0.7);
    vec2 src = p - p / rs * (thetaE * thetaE / rs) * rimFade;
    // 少許參考系拖曳，讓被扭曲的背景帶一點旋渦。A little frame dragging gives the warped scene a slight twist.
    src = rotate(src, 0.9 * (rh * rh) / (rs * rs) * rimFade);
    vec2 delta = src - p;
    delta *= min(1.0, 0.9 / max(length(delta), 1e-5));

    vec2 grabSize = vec2(textureSize(_GrabDistortionBackground, 0));
    vec2 grabUv = (gl_FragCoord.xy + delta * pixelsPerUnit) / grabSize;
    vec3 scene = texture(_GrabDistortionBackground, grabUv).rgb;
    // 越接近視界，光被吞掉越多。Light is swallowed the closer it gets to the horizon.
    scene *= mix(0.25, 1.0, smoothstep(rh, rh * 3.5, r));

    vec3 tint = u._Color.rgb;
    vec3 hot = mix(tint, vec3(1.0), 0.7);

    // 吸積盤：內熱外冷，亮度由旋轉紋理調變。Accretion disk: hot inside, cooler outside, modulated by the swirl.
    float rin = rh * 1.1;
    float rout = rh * 1.9;
    float band = smoothstep(rh * 0.98, rin * 1.08, r) * (1.0 - smoothstep(rin * 1.1, rout, r));
    float heat = 1.0 - smoothstep(rin, rout, r);
    float swirl = smoothstep(0.3, 0.75, diskPattern(p, r, t));
    vec3 disk = mix(tint * 0.5, hot, heat * heat) * band * (0.25 + 1.4 * swirl) * (0.6 + 0.6 * heat);

    // 光子環：緊貼視界的一道細亮環，微微閃爍。Photon ring: a thin bright ring hugging the horizon, flickering.
    float ringWidth = max(rh * 0.08, 0.004);
    float ringX = (r - rh * 1.04) / ringWidth;
    float flicker = 0.8 + 0.2 * sin(t * 31.0 + u._Seed) * sin(t * 17.3 + u._Seed * 2.1);
    vec3 photon = hot * exp(-ringX * ringX) * 1.6 * flicker;

    // 外圈淡淡的光暈。A faint halo further out.
    vec3 halo = tint * 0.3 * exp(-max(r - rh, 0.0) / max(rh * 1.3, 1e-3)) * rimFade;

    vec3 col = scene + (disk + photon + halo) * open;
    float hole = 1.0 - smoothstep(rh * 0.88, rh, r);
    col = mix(col, vec3(0.0), hole);

    // 外緣扭曲量已趨近零，透明度淡出只是為了和同佇列先畫的東西無縫銜接。
    // The warp is already near zero at the rim; fading alpha there just blends seamlessly with whatever the same
    // queue drew earlier.
    float alpha = open * (1.0 - smoothstep(0.75, 1.0, r));
    outColor = vec4(min(col, vec3(1.0)), alpha);
}
