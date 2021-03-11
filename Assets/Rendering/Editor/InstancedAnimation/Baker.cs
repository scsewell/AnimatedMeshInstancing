using System.Collections.Generic;

using UnityEditor;

using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Framework.Rendering.InstancedAnimation
{
    public struct BakeConfig
    {
        public Animator animator;
        public AnimationClip[] animations;
        public Dictionary<AnimationClip, float> frameRates;
        public SkinnedMeshRenderer[] renderers;
        public Dictionary<Material, Material> materialRemap;
    }

    public class Baker
    {
        readonly BakeConfig m_config;
        readonly List<BakedMesh> m_meshes = new List<BakedMesh>();
        readonly List<Animation> m_animations = new List<Animation>();

        Transform[] m_bones;
        Matrix4x4[] m_bindPoses;
        Dictionary<SkinnedMeshRenderer, Dictionary<int, int>> m_renderIndexMaps;

        /// <summary>
        /// Creates a baker instance.
        /// </summary>
        /// <param name="config">The description of the data to bake.</param>
        public Baker(BakeConfig config)
        {
            m_config = config;
        }

        /// <summary>
        /// Bakes the animations.
        /// </summary>
        /// <returns>True if the operation was cancelled.</returns>
        public bool Bake()
        {
            if (BakeMeshes())
            {
                return true;
            }
            if (BakeAnimations())
            {
                return true;
            }
            return false;
        }

        bool BakeMeshes()
        {
            m_meshes.Clear();

            try
            {
                var renderers = m_config.renderers;

                PrepareBones(renderers);

                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];

                    var title = "Baking Meshes...";
                    var info = $"{i + 1}/{renderers.Length} {renderer.name}";
                    var progress = (float)i / renderers.Length;

                    if (EditorUtility.DisplayCancelableProgressBar(title, info, progress))
                    {
                        return true;
                    }

                    var bake = BakeMesh(renderer);
                    m_meshes.Add(bake);
                }

                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        bool BakeAnimations()
        {
            m_animations.Clear();

            try
            {
                // start animation mode, allowing us to sample animation clip frames in the editor
                AnimationMode.StartAnimationMode();

                var animations = m_config.animations;

                for (var i = 0; i < animations.Length; i++)
                {
                    var animation = animations[i];

                    var title = "Baking Animations...";
                    var info = $"{i + 1}/{animations.Length} {animation.name}";
                    var progress = (float)i / animations.Length;

                    if (EditorUtility.DisplayCancelableProgressBar(title, info, progress))
                    {
                        return true;
                    }

                    var bake = BakeClip(animation);
                    m_animations.Add(bake);
                }

                return false;
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                EditorUtility.ClearProgressBar();
            }
        }

        void PrepareBones(SkinnedMeshRenderer[] renderers)
        {
            // We must find all unique bones used by any renderers, as well as the bind pose
            // for each of those bones.
            var bones = new List<Transform>();
            var bindPoses = new List<Matrix4x4>();

            // Since each renderer might only use a subset of the bones, we must be able to map
            // from indices into the renderer bone list to indices into the combined bone list.
            m_renderIndexMaps = new Dictionary<SkinnedMeshRenderer, Dictionary<int, int>>();

            foreach (var renderer in renderers)
            {
                var boneIndexToCombinedIndex = new Dictionary<int, int>();
                var rendererBones = renderer.bones;
                var rendererBindPoses = renderer.sharedMesh.bindposes;

                for (var i = 0; i < rendererBones.Length; i++)
                {
                    var bone = rendererBones[i];

                    if (!bones.Contains(bone))
                    {
                        bones.Add(bone);
                        bindPoses.Add(rendererBindPoses[i]);
                    }

                    boneIndexToCombinedIndex.Add(i, bones.IndexOf(bone));
                }

                m_renderIndexMaps.Add(renderer, boneIndexToCombinedIndex);
            }

            m_bones = bones.ToArray();
            m_bindPoses = bindPoses.ToArray();
        }

        BakedMesh BakeMesh(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            var indexMap = m_renderIndexMaps[renderer];

            // Get the bind pose position and bone index used by each vertex,
            // with the assumption that each vertex is influnced by a single bone.
            var weights = mesh.boneWeights;

            var uv3 = new Vector2[mesh.vertexCount];
            var uv4 = new Vector2[mesh.vertexCount];

            for (var i = 0; i < mesh.vertexCount; i++)
            {
                var index = indexMap[weights[i].boneIndex0];
                var position = (Vector3)m_bindPoses[index].inverse.GetColumn(3);

                // This coordinate gives the row in the animation texture this vertex should read from.
                // We offset the coordinate to be in the center of the pixel.
                var boneCoord = 0.5f * ((index + 0.5f) / m_bones.Length);

                uv3[i] = new Vector2(position.x, position.y);
                uv4[i] = new Vector2(position.z, boneCoord);
            }

            // Create a static mesh with a copy of the required mesh data.
            // We use uv channels 2 and 3 to pack additional data used
            // for the skinning, so we do not copy them.
            var bakedMesh = new Mesh
            {
                name = $"{mesh.name}_Baked",
                bounds = mesh.bounds,

                vertices = mesh.vertices,
                normals = mesh.normals,
                tangents = mesh.tangents,
                colors = mesh.colors,

                uv = mesh.uv,
                uv2 = mesh.uv2,
                uv3 = uv3,
                uv4 = uv4,
                uv5 = mesh.uv5,
                uv6 = mesh.uv6,
                uv7 = mesh.uv7,
                uv8 = mesh.uv8,

                subMeshCount = mesh.subMeshCount,
                indexFormat = mesh.indexFormat,
            };

            for (var i = 0; i < mesh.subMeshCount; i++)
            {
                bakedMesh.SetIndices(mesh.GetIndices(i), mesh.GetTopology(i), i, false);
            }

            // apply the mesh data and make no longer readable to reduce memory usage
            bakedMesh.UploadMeshData(true);

            // get the instanced materials to use for the mesh
            var materials = new List<Material>();

            foreach (var material in renderer.sharedMaterials)
            {
                materials.Add(m_config.materialRemap[material]);
            }

            return new BakedMesh(bakedMesh, materials.ToArray());
        }

        Animation BakeClip(AnimationClip animation)
        {
            var animator = m_config.animator.gameObject;
            var frameRate = m_config.frameRates[animation];

            // The top half of texture contains rotation data, the top half position data,
            // so each bone needs to rows. Each column represents a frame of animation.
            var length = Mathf.RoundToInt(animation.length * frameRate);

            var texture = new Texture2D(length, m_bones.Length * 2, GraphicsFormat.R16G16B16A16_SFloat, 0, TextureCreationFlags.None)
            {
                name = $"Anim_{animation.name}",
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };

            // Bake the animation to the texture data, while finding the bounds of the meshes
            // during the course of the animation.
            var values = new ushort[texture.width * texture.height * 4];

            var bounds = default(Bounds);
            var boundsInitialized = false;
            var boundsMeshes = new Mesh[m_config.renderers.Length];

            for (var i = 0; i < boundsMeshes.Length; i++)
            {
                boundsMeshes[i] = new Mesh();
            }

            for (var frame = 0; frame < length; frame++)
            {
                var normalizedTime = (float)frame / length;
                var time = normalizedTime * animation.length;

                // play a frame in the animation
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator, animation, time);

                // bake the column in the animation texture for the frame
                var x = frame * 4;
                var rowLength = length * 4;
                var halfOffset = rowLength * m_bones.Length;

                for (var bone = 0; bone < m_bones.Length; bone++)
                {
                    // get the offset from the bind pose to the current pose for the bone
                    var root = m_config.animator.transform;
                    var t = m_bones[bone];

                    var pos = root.InverseTransformPoint(t.position);
                    var rot = t.rotation * m_bindPoses[bone].rotation;

                    var y = bone * rowLength;

                    // set the position for the bone for this frame
                    values[y + x] = Mathf.FloatToHalf(pos.x);
                    values[y + x + 1] = Mathf.FloatToHalf(pos.y);
                    values[y + x + 2] = Mathf.FloatToHalf(pos.z);
                    values[y + x + 3] = Mathf.FloatToHalf(0f);

                    // set the rotation for the bone for this frame
                    values[halfOffset + y + x] = Mathf.FloatToHalf(rot.x);
                    values[halfOffset + y + x + 1] = Mathf.FloatToHalf(rot.y);
                    values[halfOffset + y + x + 2] = Mathf.FloatToHalf(rot.z);
                    values[halfOffset + y + x + 3] = Mathf.FloatToHalf(rot.w);
                }

                // calculate the bounds for the meshes for the frame in the animator's space
                for (var i = 0; i < boundsMeshes.Length; i++)
                {
                    var mesh = boundsMeshes[i];
                    var renderer = m_config.renderers[i];
                    var transform = renderer.transform;

                    renderer.BakeMesh(mesh);

                    if (TryGetTransformedBounds(mesh.vertices, transform, animator.transform, out var meshBounds))
                    {
                        if (!boundsInitialized)
                        {
                            bounds = meshBounds;
                            boundsInitialized = true;
                        }
                        else
                        {
                            bounds.Encapsulate(meshBounds);
                        }
                    }
                }

                AnimationMode.EndSampling();
            }

            // apply the texture data and make no longer readable to reduce memory usage
            texture.SetPixelData(values, 0);
            texture.Apply(false, true);

            return new Animation(texture, animation.length, frameRate, bounds);
        }

        /// <summary>
        /// Outputs the bake results to an asset.
        /// </summary>
        /// <param name="assetPath">The path starting from and including the assets folder
        /// under which to save the animation data.</param>
        public void SaveBake(string assetPath)
        {
            try
            {
                // create the asset
                EditorUtility.DisplayProgressBar("Creating Asset", string.Empty, 1f);

                var asset = InstancedAnimationAsset.Create(m_meshes.ToArray(), m_animations.ToArray());

                // Save the generated asset and meshes. The asset file extention is special and is recognized by unity.
                var uniquePath = AssetDatabase.GenerateUniqueAssetPath($"{assetPath}/{m_config.animator.name}.asset");
                AssetDatabase.CreateAsset(asset, uniquePath);

                foreach (var mesh in m_meshes)
                {
                    AssetDatabase.AddObjectToAsset(mesh.Mesh, asset);
                }
                foreach (var clip in m_animations)
                {
                    AssetDatabase.AddObjectToAsset(clip.Texture, asset);
                }

                AssetDatabase.SaveAssets();

                // focus the new asset in the project window
                ProjectWindowUtil.ShowCreatedAsset(asset);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        bool TryGetTransformedBounds(Vector3[] vertices, Transform from, Transform to, out Bounds bounds)
        {
            if (vertices.Length == 0)
            {
                bounds = default;
                return false;
            }

            var localToWorld = from.localToWorldMatrix;
            var worldToLocal = to.worldToLocalMatrix;

            var min = float.MaxValue * Vector3.one;
            var max = float.MinValue * Vector3.one;

            for (var i = 0; i < vertices.Length; i++)
            {
                var vert = vertices[i];
                vert = localToWorld.MultiplyPoint3x4(vert);
                vert = worldToLocal.MultiplyPoint3x4(vert);

                min = Vector3.Min(min, vert);
                max = Vector3.Max(max, vert);
            }

            var center = (max + min) * 0.5f;
            var size = max - min;
            bounds = new Bounds(center, size);
            return true;
        }
    }
}
