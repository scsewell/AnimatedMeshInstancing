#ifndef ANIMATION_INSTANCING_INPUT_INCLUDED
#define ANIMATION_INSTANCING_INPUT_INCLUDED

#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED

struct InstanceProperties
{
    float4x4 model;
    float4x4 modelInv;
    float time;
};

struct AnimationRegion
{
    float2 min;
    float2 max;
};

TEXTURE2D(_Animation);  SAMPLER(sampler_Animation);
float4 _Animation_TexelSize;

StructuredBuffer<AnimationRegion> _AnimationRegions;
StructuredBuffer<InstanceProperties> _InstanceProperties;

AnimationRegion _AnimationRegion;
float _AnimationTime;

void Setup()
{
    // set the mode matrix for the current instance
    UNITY_MATRIX_M = _InstanceProperties[unity_InstanceID].model;
    UNITY_MATRIX_I_M = _InstanceProperties[unity_InstanceID].modelInv;

    // get the animation time of this intance
    _AnimationTime = _InstanceProperties[unity_InstanceID].time;

    // get the region of the animation texture atlas the required texture is in
    _AnimationRegion = _AnimationRegions[unity_InstanceID %  8];
}

float3 RotatePoint(float3 p, float4 quat)
{
    return p + 2.0 * cross(quat.xyz, cross(quat.xyz, p) + (p * quat.w));
}

struct Pose
{
    float3 position;
    float4 rotation;
};

Pose SampleAnimation(half time, half boneCoord)
{
    half3 min = _AnimationRegion.min.xyy;
    half3 max = _AnimationRegion.max.xyy;
    half3 fac = half3(time, boneCoord, boneCoord + 0.5);

    half3 uv = lerp(min, max, fac) / _Animation_TexelSize.zww;

    Pose pose;
    pose.position = SAMPLE_TEXTURE2D_LOD(_Animation, sampler_Animation, uv.xy, 0).rgb;
    pose.rotation = SAMPLE_TEXTURE2D_LOD(_Animation, sampler_Animation, uv.xz, 0).rgba;
    return pose;
}

#endif

void Skin(float2 uv2, float2 uv3, inout float3 positionOS, inout float3 normalOS, inout float3 tangentOS)
{
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    // get the bone pose for the frame in object space
    float time = frac(_AnimationTime * (30 / (_AnimationRegion.max.x - _AnimationRegion.min.x)));
    
    Pose pose = SampleAnimation(time, uv3.y);

    // unapply the bind pose
    float3 bindPose = float3(uv2.x, uv2.y, uv3.x);
    float3 boneRelativePos = positionOS - bindPose;

    // Apply the bone transformation
    positionOS = RotatePoint(boneRelativePos, pose.rotation) + pose.position;
    normalOS = RotatePoint(normalOS, pose.rotation);
    tangentOS = RotatePoint(tangentOS, pose.rotation);
#endif
}

#endif // ANIMATION_INSTANCING_INPUT_INCLUDED

