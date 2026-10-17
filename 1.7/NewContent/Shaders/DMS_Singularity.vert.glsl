#version 450

// 零點能奇點與電弧共用的頂點著色器：只傳頂點色與 UV，不走 INSTANCED（由 CompSingularityGraphic 逐一 DrawMesh）。
// Vertex stage shared by the zero-point singularity and its arcs: passes vertex colour and UV only. No INSTANCED
// path, because CompSingularityGraphic draws each mesh with its own DrawMesh call.

layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 2) in vec4 inColor;    // @vertex Color optional @default = vec4(1.0, 1.0, 1.0, 1.0)
layout(location = 3) in vec4 inUv0;      // @vertex TexCoord0 optional @default = vec4(0.0, 0.0, 0.0, 0.0)

layout(push_constant) uniform Push { mat4 modelMatrix; } push;

layout(location = 0) out vec4 vColor;
layout(location = 1) out vec2 vUv;

layout(set = 0, binding = 0) uniform VertexUniforms
{
    mat4 ViewProj;
} uVertex;

void main()
{
    gl_Position = uVertex.ViewProj * push.modelMatrix * vec4(inPosition, 1.0);
    vColor = inColor;
    vUv = inUv0.xy;
}
