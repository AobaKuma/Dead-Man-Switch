#version 450

// 零點能奇點的微型電弧（加色混合）。頂點色編碼橫截面：r = 白熱的芯、g = 染色的輝光、a = 沿電弧的亮度。
// 顏色與每格的強度由 DMS.CompSingularityGraphic 以 property block 送入。
// Micro arcs around the zero-point singularity, blended additively. Vertex colour encodes the cross-section:
// r = white-hot core, g = tinted glow, a = brightness along the arc. Colours and this frame's strength come from
// DMS.CompSingularityGraphic through a property block.

layout(location = 0) in vec4 vColor;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;      // @color @default = vec4(0.5, 0.85, 1.0, 1.0)
    vec4 _CoreColor;  // @color @default = vec4(0.9, 0.97, 1.0, 1.0)
} u;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

void main()
{
    vec3 c = vColor.r * u._CoreColor.rgb + vColor.g * u._Color.rgb;
    outColor = vec4(min(c * (vColor.a * u._Color.a), vec3(1.0)), 1.0);
}
