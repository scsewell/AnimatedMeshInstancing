using System.Runtime.InteropServices;

using Unity.Mathematics;

using UnityEngine;

namespace InstancedAnimation
{
    // This file is kept in sync with InstancingTypes.hlsl

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct MeshData
    {
        public static readonly int k_size = Marshal.SizeOf<MeshData>();

        public uint argsIndex;
        public uint lodCount;
        public fixed float lodDistances[Constants.k_MaxLodCount];
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct AnimationData
    {
        public static readonly int k_size = Marshal.SizeOf<AnimationData>();

        Bounds bounds;
        float2 textureRegionMin;
        float2 textureRegionMax;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct InstanceData
    {
        public static readonly int k_size = Marshal.SizeOf<InstanceData>();

        float3 position;
        float4 rotation;
        float3 scale;
        uint meshIndex;
        uint animationStartIndex;
        uint animationIndex;
        float animationTime;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct InstanceProperties
    {
        public static readonly int k_size = Marshal.SizeOf<InstanceProperties>();

        float4x4 model;
        float4x4 modelInv;
        uint animationIndex;
        float animationTime;
    };
}
