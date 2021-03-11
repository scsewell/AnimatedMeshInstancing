using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

using UnityEngine;
using UnityEngine.Rendering;

namespace Framework.Rendering.InstancedAnimation
{
    public class InstancedAnimator : MonoBehaviour
    {
        static readonly int k_animationProp = Shader.PropertyToID("_Animation");
        static readonly int k_instanceBufferProp = Shader.PropertyToID("_InstanceProperties");

        struct SubMeshData
        {
            public Mesh mesh;
            public int subMeshIndex;
            public Material material;
        }

        struct SubMeshArgs
        {
            public static readonly int k_size = Marshal.SizeOf<SubMeshArgs>();

            public uint indexCount;
            public uint instanceCount;
            public uint indexStart;
            public uint baseVertex;
            public uint instanceStart;
        }

        struct InstanceProperties
        {
            public static readonly int k_size = Marshal.SizeOf<InstanceProperties>();

            public Matrix4x4 model;
            public Matrix4x4 modelInv;
            public float time;
        }

        [SerializeField]
        [Tooltip("The animation asset containing the animated content to play.")]
        InstancedAnimationAsset m_animationAsset = null;

        int m_instanceCount;
        NativeArray<InstanceProperties> m_instanceData;
        ComputeBuffer m_instanceBuffer;

        bool m_buffersCreated;
        SubMeshData[] m_subMeshes;
        NativeArray<SubMeshArgs> m_argsData;
        ComputeBuffer m_argsBuffer;

        /// <summary>
        /// The animation asset containing the animated content to play.
        /// </summary>
        public InstancedAnimationAsset AnimationAsset
        {
            get => m_animationAsset;
            set
            {
                if (m_animationAsset != value)
                {
                    m_animationAsset = value;
                    CreateOrUpdateBuffersIfNeeded();
                    SetInstanceCount(m_instanceCount);
                }
            }
        }

        /// <summary>
        /// The number of animated instances.
        /// </summary>
        public int InstanceCount
        {
            get => m_instanceCount;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Must be non-negative!");
                }
                if (m_instanceCount != value)
                {
                    m_instanceCount = value;
                    SetInstanceCount(m_instanceCount);
                }
            }
        }

        void OnEnable()
        {
            CreateOrUpdateBuffersIfNeeded();
            SetInstanceCount(m_instanceCount);
        }

        void OnDisable()
        {
            DestroyBuffers();
        }

        void LateUpdate()
        {
            if (!IsActive())
            {
                return;
            }

            var configureInstancesJob = new ConfigureInstancesJob
            {
                instanceCount = m_instanceCount,
                instanceProperties = m_instanceData,
                time = Time.time,
            };

            var handle = configureInstancesJob.Schedule(m_instanceCount, 64);
            handle.Complete();

            //for (var i = 0; i < m_instanceCount; i++)
            //{
            //    var offset = 0.5f * UnityEngine.Random.insideUnitSphere;
            //    offset.y = 0;

            //    var edgeLength = Mathf.CeilToInt(Mathf.Sqrt(m_instanceCount));
            //    var pos = new Vector3(-Mathf.Repeat(i, edgeLength), 0, -Mathf.Floor(i / edgeLength)) + offset;
            //    var rot = Quaternion.Euler(0, UnityEngine.Random.value * 30f - 15f, 0);
            //    var scale = Vector3.one * Mathf.Lerp(0.9f, 2.0f, Mathf.Pow(UnityEngine.Random.value, 20.0f));
            //    var matrix = Matrix4x4.TRS(pos, rot, scale);

            //    m_instanceData[i] = new InstanceProperties
            //    {
            //        model = matrix,
            //        modelInv = matrix.inverse,
            //        time = (Time.time / (2f * scale.magnitude)) + UnityEngine.Random.value,
            //    };
            //}

            m_instanceBuffer.SetData(m_instanceData, 0, 0, m_instanceCount);

            for (var i = 0; i < m_subMeshes.Length; i++)
            {
                var subMesh = m_subMeshes[i];

                Graphics.DrawMeshInstancedIndirect(
                    subMesh.mesh,
                    subMesh.subMeshIndex,
                    subMesh.material,
                    new Bounds(Vector3.zero, 1000f * Vector3.one),
                    m_argsBuffer,
                    i * SubMeshArgs.k_size,
                    null,
                    ShadowCastingMode.On,
                    true,
                    gameObject.layer,
                    null,
                    LightProbeUsage.BlendProbes
                );
            }
        }
        
        [BurstCompile]
        struct ConfigureInstancesJob : IJobParallelFor
        {
            public NativeArray<InstanceProperties> instanceProperties;
            public int instanceCount;
            public float time;

            public void Execute(int i)
            {
                var edgeLength = Mathf.CeilToInt(Mathf.Sqrt(instanceCount));
                var pos = new Vector3(-Mathf.Repeat(i, edgeLength), 0, -Mathf.Floor(i / edgeLength));
                //var rot = Quaternion.Euler(0, UnityEngine.Random.value * 30f - 15f, 0);
                //var scale = Vector3.one * Mathf.Lerp(0.9f, 2.0f, Mathf.Pow(UnityEngine.Random.value, 20.0f));
                var matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

                instanceProperties[i] = new InstanceProperties
                {
                    model = matrix,
                    modelInv = matrix.inverse,
                    time = time + pos.magnitude,
                };
            }
        }


        bool IsActive()
        {
            return isActiveAndEnabled && m_animationAsset != null && m_instanceCount > 0;
        }

        void CreateOrUpdateBuffersIfNeeded()
        {
            DestroyBuffers();

            if (!IsActive())
            {
                return;
            }

            // Get the submeshes to render
            var meshes = m_animationAsset.meshes;
            var subMeshes = new List<SubMeshData>();

            for (var i = 0; i < meshes.Length; i++)
            {
                var mesh = meshes[i].Mesh;

                if (mesh == null)
                {
                    continue;
                }

                for (var j = 0; j < mesh.subMeshCount; j++)
                {
                    // TODO: only duplicate one instance per material
                    var material = new Material(meshes[i].Materials[j]);

                    subMeshes.Add(new SubMeshData
                    {
                        mesh = mesh,
                        subMeshIndex = j,
                        material = material,
                    });

                    material.SetTexture(k_animationProp, m_animationAsset.clips[0].Texture);
                }
            }

            m_subMeshes = subMeshes.ToArray();

            // create and initialize the draw args buffer
            m_argsBuffer = new ComputeBuffer(m_subMeshes.Length, SubMeshArgs.k_size, ComputeBufferType.IndirectArguments)
            {
                name = $"{name}IndirectArgs",
            };

            m_argsData = new NativeArray<SubMeshArgs>(m_subMeshes.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            for (var i = 0; i < m_subMeshes.Length; i++)
            {
                var mesh = m_subMeshes[i].mesh;
                var subMesh = m_subMeshes[i].subMeshIndex;

                m_argsData[i] = new SubMeshArgs
                {
                    indexCount = mesh.GetIndexCount(subMesh),
                    instanceCount = 0,
                    indexStart = mesh.GetIndexStart(subMesh),
                    baseVertex = mesh.GetBaseVertex(subMesh),
                    instanceStart = 0,
                };
            }

            m_argsBuffer.SetData(m_argsData);
            m_buffersCreated = true;
        }

        void DestroyBuffers()
        {
            m_buffersCreated = false;

            if (m_instanceData.IsCreated)
            {
                m_instanceData.Dispose();
                m_instanceData = default;
            }
            if (m_instanceBuffer != null)
            {
                m_instanceBuffer.Release();
                m_instanceBuffer = null;
            }

            m_subMeshes = null;
            if (m_argsData.IsCreated)
            {
                m_argsData.Dispose();
                m_argsData = default;
            }
            if (m_argsBuffer != null)
            {
                m_argsBuffer.Release();
                m_argsBuffer = null;
            }
        }

        void SetInstanceCount(int count)
        {
            if (!IsActive())
            {
                return;
            }

            if (!m_buffersCreated)
            {
                CreateOrUpdateBuffersIfNeeded();
            }

            // ensure the instance data buffers are big enough for all the instance data
            if (m_instanceBuffer != null && m_instanceBuffer.count < count)
            {
                m_instanceBuffer.Dispose();
                m_instanceBuffer = null;
            }
            if (m_instanceBuffer == null)
            {
                m_instanceBuffer = new ComputeBuffer(count, InstanceProperties.k_size, ComputeBufferType.Structured)
                {
                    name = $"{name}InstanceData",
                };

                for (var i = 0; i < m_subMeshes.Length; i++)
                {
                    m_subMeshes[i].material.SetBuffer(k_instanceBufferProp, m_instanceBuffer);
                }
            }

            if (!m_instanceData.IsCreated)
            {
                m_instanceData = new NativeArray<InstanceProperties>(count, Allocator.Persistent);
            }
            else if (m_instanceData.Length < count)
            {
                var oldData = m_instanceData;
                
                m_instanceData = new NativeArray<InstanceProperties>(count, Allocator.Persistent);
                NativeArray<InstanceProperties>.Copy(oldData, m_instanceData, oldData.Length);

                oldData.Dispose();
            }

            // set the instance count for all submeshes
            for (var i = 0; i < m_subMeshes.Length; i++)
            {
                var args = m_argsData[i];

                args.instanceCount = (uint)count;

                m_argsData[i] = args;
            }

            m_argsBuffer.SetData(m_argsData);
        }
    }
}
