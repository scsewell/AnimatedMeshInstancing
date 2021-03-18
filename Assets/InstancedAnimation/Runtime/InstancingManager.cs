﻿using System.Runtime.InteropServices;

using Unity.Collections;
using Unity.Jobs;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Profiling;

namespace InstancedAnimation
{
    /// <summary>
    /// A class that manages the rendering of all instanced meshes. 
    /// </summary>
    public class InstancingManager
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct DrawArgs
        {
            public static readonly int k_size = Marshal.SizeOf<DrawArgs>();

            public uint indexCount;
            public uint instanceCount;
            public uint indexStart;
            public uint baseVertex;
            public uint instanceStart;
        }

        struct RendererData
        {
            public Mesh mesh;
            public int subMeshIndex;
            public Material material;
            public MaterialPropertyBlock propertyBlock;
        }

        static bool s_resourcesInitialized;
        static InstancingResources s_resources;
        static ComputeShader s_cullingShader;
        static ComputeShader s_scanShader;
        static ComputeShader s_compactShader;
        static int s_cullingKernel;
        static int s_scanInBucketKernel;
        static int s_scanAcrossBucketsKernel;
        static int s_compactKernel;

        static CommandBuffer s_cullingCmdBuffer;
        static ComputeBuffer s_meshDataBuffer;
        static ComputeBuffer s_animationDataBuffer;
        static ComputeBuffer s_instanceDataBuffer;
        static ComputeBuffer s_drawArgsBuffer;
        static ComputeBuffer s_isVisibleBuffer;
        static ComputeBuffer s_scanInBuffer;
        static ComputeBuffer s_scanOutBuffer;
        static ComputeBuffer s_scanIntermediateBuffer;
        static ComputeBuffer s_instancePropertiesBuffer;

        static bool s_enabled;

        /// <summary>
        /// Is the instance renderer enabled.
        /// </summary>
        public bool Enabled => s_enabled;

        struct AnimationInstancingUpdate
        {
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            // In case the domain reload on enter play mode is disabled, we must
            // reset all static fields.
            DisposeResources();

            // inject the instancing update method into the player loop
            var loop = PlayerLoop.GetCurrentPlayerLoop();

            if (loop.TryFindSubSystem<PostLateUpdate>(out var postLateUpdate))
            {
                postLateUpdate.AddSubSystem<AnimationInstancingUpdate>(1, Update);
                loop.TryUpdate(postLateUpdate);
            }

            PlayerLoop.SetPlayerLoop(loop);

            // enable the instanced renderer by default
            Enable();
        }

        /// <summary>
        /// Checks if the current platform has support for the features required for the
        /// instancing implementation.
        /// </summary>
        /// <returns>True if the current platform is supported.</returns>
        public bool IsSupported()
        {
            return IsSupported(out var reasons);
        }

        /// <summary>
        /// Enable the instanced renderer.
        /// </summary>
        public static void Enable()
        {
            if (!IsSupported(out var reasons))
            {
                Debug.LogWarning($"Animation instancing is not supported by the current platform: {reasons}");
                return;
            }

            if (CreateResources())
            {
                s_enabled = true;
            }
        }

        /// <summary>
        /// Disable the instanced renderer.
        /// </summary>
        /// <param name="disposeResources">Deallocate the resources used by the renderer.</param>
        public static void Disable(bool disposeResources)
        {
            s_enabled = false;

            if (disposeResources)
            {
                DisposeResources();
            }
        }

        static bool CreateResources()
        {
            if (s_resourcesInitialized)
            {
                return true;
            }

            // load the compute shaders
            s_resources = Resources.Load<InstancingResources>(nameof(InstancingResources));

            if (s_resources == null)
            {
                Debug.LogError("Failed to load instancing resources!");
                DisposeResources();
                return false;
            }

            s_cullingShader = s_resources.Culling;
            s_scanShader = s_resources.Scan;
            s_compactShader = s_resources.Compact;

            if (s_cullingShader == null || s_scanShader == null || s_compactShader == null)
            {
                Debug.LogError("Required compute shaders have not been assigned to the instancing resources asset!");
                DisposeResources();
                return false;
            }

            if (!TryGetKernel(s_cullingShader,  Kernels.k_CullingKernel,            ref s_cullingKernel) || 
                !TryGetKernel(s_scanShader,     Kernels.k_ScanInBucketKernel,       ref s_scanInBucketKernel) ||
                !TryGetKernel(s_scanShader,     Kernels.k_ScanAcrossBucketsKernel,  ref s_scanAcrossBucketsKernel) ||
                !TryGetKernel(s_compactShader,  Kernels.k_CullingKernel,            ref s_compactKernel))
            {
                DisposeResources();
                return false;
            }

            // create the command buffers
            s_cullingCmdBuffer = new CommandBuffer
            {
                name = $"InstanceCulling",
            };

            s_resourcesInitialized = true;
            return true;
        }

        static void DisposeResources()
        {
            s_enabled = false;
            s_resourcesInitialized = false;

            // clean up compute shaders
            if (s_resources != null)
            {
                Resources.UnloadAsset(s_resources);
                s_resources = null;
            }

            s_cullingShader = null;
            s_scanShader = null;
            s_compactShader = null;

            s_cullingKernel = 0;
            s_scanInBucketKernel = 0;
            s_scanAcrossBucketsKernel = 0;
            s_compactKernel = 0;

            // destory command buffers
            Dispose(ref s_cullingCmdBuffer);

            // destroy compute buffers
            Dispose(ref s_meshDataBuffer);
            Dispose(ref s_animationDataBuffer);
            Dispose(ref s_instanceDataBuffer);
            Dispose(ref s_drawArgsBuffer);
            Dispose(ref s_isVisibleBuffer);
            Dispose(ref s_scanInBuffer);
            Dispose(ref s_scanOutBuffer);
            Dispose(ref s_scanIntermediateBuffer);
            Dispose(ref s_instancePropertiesBuffer);
        }

        static void Update()
        {
            // skip if 0 instances
            if (!s_enabled)
            {
                return;
            }

            Cull(Camera.main);
            Draw(Camera.main);
        }

        static void Cull(Camera cam)
        {
            Profiler.BeginSample($"{nameof(InstancingManager)}.{nameof(Cull)}");

            s_cullingCmdBuffer.Clear();

            // create the compute buffers
            //s_drawArgsBuffer = new ComputeBuffer(m_subMeshes.Length, SubMeshArgs.k_size, ComputeBufferType.IndirectArguments)
            //{
            //    name = $"{nameof(InstancingManager)}DrawArgs",
            //};

            Profiler.EndSample();
        }

        static void Draw(Camera cam)
        {
            Profiler.BeginSample($"{nameof(InstancingManager)}.{nameof(Draw)}");

            Profiler.EndSample();
        }

        static bool IsSupported(out string reasons)
        {
            reasons = null;

            if (!SystemInfo.supportsComputeShaders)
            {
                reasons = (reasons == null ? string.Empty : reasons) + "Compute shaders are not supported!\n";
            }
            if (!SystemInfo.supportsSetConstantBuffer)
            {
                reasons = (reasons == null ? string.Empty : reasons) + "Set constant buffer is not supported!\n";
            }
            if (!SystemInfo.supportsInstancing)
            {
                reasons = (reasons == null ? string.Empty : reasons) + "Instancing is not supported!\n";
            }

            return reasons == null;
        }

        static bool TryGetKernel(ComputeShader shader, string name, ref int kernelID)
        {
            if (!shader.HasKernel(name))
            {
                Debug.LogError($"Kernel \"{name}\" not found in compute shader \"{shader.name}\"!");
                return false;
            }

            kernelID = shader.FindKernel(name);
            return true;
        }

        static void Dispose(ref CommandBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        static void Dispose(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }
    }
}
