#version 450

// 零點能奇點的彩蛋：近距離盯著奇點太久時，畫面偶爾出現噪點、方格錯位、RGB 分離與掃描線撕裂，越靠近奇點越密集。
// 畫在以奇點為中心、蓋滿整個視野的方塊上；它自己的場景抓取點排在黑洞、電弧與黑暗之後，所以連它們一起故障。
// DMS.CompSingularityGraphic 只在故障發作時繪製，並送入 _Color（rgb = 雜訊色調、a = 這一段的強度）、
// _QuadSize（方塊尺寸，格）、_GlitchSeed（每段換一組方塊）與 _AgeSecs（發作後經過的真實秒數）。
// Easter egg for the zero-point singularity: after staring at it up close for too long, the view now and then
// breaks into noise, displaced blocks, RGB split and scanline tearing, densest near the hole. Drawn on a quad
// centred on the singularity that covers the whole view; its own scene grab comes after the hole, the arcs and
// the darkness, so all of them glitch too. DMS.CompSingularityGraphic draws it only during an episode and feeds
// _Color (rgb = static tint, a = this segment's strength), _QuadSize (quad size in cells), _GlitchSeed (a new block
// layout per segment) and _AgeSecs (real seconds since the episode began).

layout(location = 1) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;      // @color @default = vec4(0.45, 0.8, 1.0, 1.0)
    vec4 _QuadSize;   // @default = vec4(40.0, 24.0, 0.0, 0.0)
    float _GlitchSeed;
    float _AgeSecs;
} u;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _GrabDMS_SingularityGlitch;

float hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// 任一像素離奇點幾格：方塊對齊螢幕，所以用導數把像素位移換回方塊的 p 座標。
// Distance in cells from the hole for any pixel: the quad is screen-aligned, so derivatives map a pixel offset
// back to the quad's p coordinates.
float cellsFromHole(vec2 pixel, vec2 frag, vec2 p, vec2 pPerPixel)
{
    vec2 q = p + (pixel - frag) * pPerPixel;
    return length(q * u._QuadSize.xy * 0.5);
}

void main()
{
    vec2 p = vUv * 2.0 - 1.0;
    float dx = dFdx(p.x);
    float dy = dFdy(p.y);
    vec2 pPerPixel = vec2(abs(dx) < 1e-7 ? 1e-7 : dx, abs(dy) < 1e-7 ? 1e-7 : dy);

    vec2 grabSize = vec2(textureSize(_GrabDMS_SingularityGlitch, 0));
    vec2 frag = gl_FragCoord.xy;
    float strength = clamp(u._Color.a, 0.0, 1.0);
    float seed = u._GlitchSeed;
    float dHere = cellsFromHole(frag, frag, p, pPerPixel);

    vec2 offset = vec2(0.0);
    float split = 0.0;
    float staticMix = 0.0;
    float invert = 0.0;

    // 兩層方格：大塊扁長、小塊細碎；以方塊中心離奇點的距離決定機率，方塊邊緣才會俐落。
    // Two block layers, large flat ones and small fine ones. The chance uses the distance of the block centre, so
    // block edges stay crisp.
    for (int layer = 0; layer < 2; layer++)
    {
        vec2 blockPx = layer == 0 ? vec2(grabSize.y / 7.0, grabSize.y / 22.0) : vec2(grabSize.y / 26.0, grabSize.y / 64.0);
        vec2 cell = floor(frag / blockPx);
        float d = cellsFromHole((cell + 0.5) * blockPx, frag, p, pPerPixel);
        float nearHole = exp(-d / 3.0);
        float chance = strength * (layer == 0 ? 0.55 : 0.75) * (nearHole + 0.025);
        float salt = float(layer) * 101.0;
        if (hash12(cell + seed * 13.7 + salt) < chance)
        {
            float kx = hash12(cell * 1.7 + seed * 7.3 + salt + 5.0);
            float ky = hash12(cell * 2.3 + seed * 3.1 + salt + 9.0);
            offset += vec2((kx - 0.5) * blockPx.x * 1.6, (ky - 0.5) * blockPx.y * 0.6);
            split = max(split, 3.0 + ky * grabSize.y / 110.0);
            // 雜訊塊與反相塊只出現在奇點附近；遠處只錯位，否則看起來像介面壞掉而不是畫面故障。
            // Static and inverted blocks stay near the hole; far away blocks only shift, otherwise they read as a
            // broken UI rather than a corrupted picture.
            float kind = hash12(cell + seed * 5.9 + salt + 77.0);
            if (kind < 0.2 * nearHole)
                staticMix = 1.0;
            else if (kind < 0.28 * nearHole)
                invert = 1.0;
        }
    }

    // 掃描線撕裂：整列水平錯位。Scanline tearing: whole rows shift sideways.
    float rowPx = max(2.0, grabSize.y / 140.0);
    float row = floor(frag.y / rowPx);
    if (hash12(vec2(row, seed * 3.3 + 41.0)) < strength * 0.25 * (exp(-dHere / 5.0) + 0.05))
        offset.x += (hash12(vec2(row, seed * 1.9 + 3.0)) - 0.5) * grabSize.y / 12.0;

    // 只用 textureLod，分支裡外都不依賴隱含導數。textureLod only, so nothing relies on implicit derivatives.
    vec2 base = frag + offset;
    vec2 splitPx = vec2(split, 0.0);
    vec3 col = textureLod(_GrabDMS_SingularityGlitch, base / grabSize, 0.0).rgb;
    col.r = textureLod(_GrabDMS_SingularityGlitch, (base + splitPx) / grabSize, 0.0).r;
    col.b = textureLod(_GrabDMS_SingularityGlitch, (base - splitPx) / grabSize, 0.0).b;

    // 噪點：每格畫面都換一次，高解析度下顆粒也不會細到看不見。
    // Static: re-rolled every frame, with grains sized so they stay visible at high resolution.
    float grainPx = max(1.0, floor(grabSize.y / 360.0));
    float n = hash12(floor(frag / grainPx) + fract(seed * 0.618 + u._AgeSecs * 7.0) * 1000.0);
    vec3 staticCol = mix(vec3(n), u._Color.rgb * n * 1.4, 0.35);
    col = mix(col, staticCol, staticMix * 0.85);
    col = mix(col, vec3(1.0) - col, invert);

    float grain = strength * (0.28 * exp(-dHere / 4.0) + 0.06);
    col += (n - 0.5) * grain;
    // 淡淡的掃描線。Faint scanlines.
    col *= 1.0 - 0.12 * strength * exp(-dHere / 4.0) * step(0.5, fract(frag.y / (2.0 * grainPx)));

    outColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
