#ifndef ANIMATION_INSTANCING_INPUT_INCLUDED
#define ANIMATION_INSTANCING_INPUT_INCLUDED

struct InstanceProperties
{
    float4x4 model;
    float4x4 modelInv;
    float time;
};

StructuredBuffer<InstanceProperties> _InstanceProperties;

TEXTURE2D(_Animation);  SAMPLER(sampler_Animation);

float3 RotatePoint(float3 p, float4 quat)
{
    return p + 2.0 * cross(quat.xyz, cross(quat.xyz, p) + (p * quat.w));
}

void Skin(uint instanceID, float2 uv2, float2 uv3, inout float3 positionOS, inout float3 normalOS, inout float3 tangentOS)
{
    // set the mode matrix for the current instance
    UNITY_MATRIX_M = _InstanceProperties[instanceID].model;
    UNITY_MATRIX_I_M = _InstanceProperties[instanceID].modelInv;

    // get the bone pose for the frame in object space
    float time = _InstanceProperties[instanceID].time;
    
    float2 posUV = float2(time, uv3.y);
    float2 rotUV = float2(time, uv3.y + 0.5);

    float3 position = SAMPLE_TEXTURE2D_LOD(_Animation, sampler_Animation, posUV, 0).rgb;
    float4 rotation = SAMPLE_TEXTURE2D_LOD(_Animation, sampler_Animation, rotUV, 0).rgba;

    // unapply the bind pose
    float3 bindPose = float3(uv2.x, uv2.y, uv3.x);
    float3 boneRelativePos = positionOS - bindPose;

    // Apply the bone transformation
    positionOS = RotatePoint(boneRelativePos, rotation) + position;
    normalOS = RotatePoint(normalOS, rotation);
    tangentOS = RotatePoint(tangentOS, rotation);
}

#endif // ANIMATION_INSTANCING_INPUT_INCLUDED

