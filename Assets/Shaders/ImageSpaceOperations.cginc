#ifndef ISAO_HELPER_INCLUDED
#define ISAO_HELPER_INCLUDED

// --------------------------------- helper functions

inline float3 findViewSpaceNormal(sampler2D nbuffer, float2 uv, float4x4 viewMat)
{
	// World-space normal vector
    float4 worldNorm = float4(tex2D(nbuffer, uv).xyz * 2.0f - 1.0f, 0.0f);
    return mul(viewMat, worldNorm).xyz;
}

inline float3 findViewSpaceNormal(Texture2D<float4> nbuffer, uint2 id, float4x4 viewMat)
{
	// World-space normal vector
    float4 worldNorm = float4(nbuffer[id].xyz * 2.0f - 1.0f, 0.0f);
    return mul(viewMat, worldNorm).xyz;
}


inline float3 findViewSpacePositionFromDepthTexture(sampler2D zbuffer, float2 uv, float4x4 invProjMat)
{
    float z = 1.0f - tex2D(zbuffer, uv).r; // Raw z value, [0, 1]
    z = z * 2.0f - 1.0f;
    float x = uv.x * 2 - 1; // Viewport position
    float y = uv.y * 2 - 1;
    float4 viewPos = mul(invProjMat, float4(x, y, z, 1.0f));
    return viewPos.xyz / viewPos.w;
}


inline float3 findViewSpacePositionFromDepthTexture(Texture2D<float> zbuffer, uint2 id, float2 uv, float4x4 invProjMat)
{
    float z = 1.0f - zbuffer[id].r; // Raw z value, [0, 1]
    z = z * 2.0f - 1.0f;
    float x = uv.x * 2 - 1; // Viewport position
    float y = uv.y * 2 - 1;
    float4 viewPos = mul(invProjMat, float4(x, y, z, 1.0f));
    return viewPos.xyz / viewPos.w;
}

inline float3 findViewSpacePositionFromDepthTextureVolume(RWTexture2DArray<float> zbufferVol, uint3 id, float2 uv, float4x4 invProjMat)
{
    float z = 1.0f - zbufferVol[id].r; // Raw z value, [0, 1]
    z = z * 2.0f - 1.0f;
    float x = uv.x * 2 - 1; // Viewport position
    float y = uv.y * 2 - 1;
    float4 viewPos = mul(invProjMat, float4(x, y, z, 1.0f));
    return viewPos.xyz / viewPos.w;
}

inline float depthToViewZ(float depth, float near, float far)
{
    
    return lerp(near, far, (2 * near) / (far + near -
    depth * (far - near)));

}

inline float3 findNDC(float depth, float2 uv)
{
    float z = 1.0f - depth; // Raw z value, [0, 1]
    z = z * 2.0f - 1.0f;
    float x = uv.x * 2 - 1; // Viewport position
    float y = uv.y * 2 - 1;
    return float3(x, y, z);
}

inline float3 findViewSpacePositionFromNDC(float3 ndc, float4x4 invProjMat)
{
    float4 viewPos = mul(invProjMat, float4(ndc.xyz, 1.0f));
    return viewPos.xyz / viewPos.w;
}

uint findSampleCountAdaptive(float viewPosZ)
{
    if (viewPosZ > -15.0f)
        return 8;
    else if (viewPosZ > -50.0f)
        return 4;
    else if (viewPosZ > -100.0f)
        return 2;
    else
        return 0;
}





#endif