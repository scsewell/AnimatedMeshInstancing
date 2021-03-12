using System.Collections.Generic;
using System.Linq;

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
        Texture2D m_animationTexture;

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
                var animations = m_config.animations;
                var frameRates = m_config.frameRates;

                // All of the animations are baked into a single texture. Each animation occupies a
                // rectangular region of that texture. The height of each animation region is twice the
                // number of bones, as each bone uses two rows per frame of animation, while the length
                // in pixels of the animation is the number of frames in the animation. Since there are
                // the same number of bones per animation, all animations have the same height in the
                // texture. We want to pack the animation textures such that no animation runs off the
                // edge of the texture, while minimizing the wasted space.
                var animationSizes = new Vector2Int[animations.Length];

                // find the size required by each animation
                var height = m_bones.Length * 2;

                for (var i = 0; i < animations.Length; i++)
                {
                    var animation = animations[i];
                    var frameRate = frameRates[animation];
                    var length = Mathf.RoundToInt(animation.length * frameRate);

                    animationSizes[i] = new Vector2Int(length, height);
                }

                // find a reasonably optimal packing of the animation textures
                var regions = Pack(animationSizes, out var size);

                // create the texture data buffer
                var texture = new ushort[size.x * size.y * 4];

                // start animation mode, allowing us to sample animation clip frames in the editor
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();

                for (var i = 0; i < animations.Length; i++)
                {
                    var animation = animations[i];
                    var region = regions[i];

                    var title = "Baking Animations...";
                    var info = $"{i + 1}/{animations.Length} {animation.name}";
                    var progress = (float)i / animations.Length;

                    if (EditorUtility.DisplayCancelableProgressBar(title, info, progress))
                    {
                        return true;
                    }

                    var bounds = BakeAnimation(texture, size, animation, region);

                    m_animations.Add(new Animation(region, animation.length, bounds));
                }

                // create the animation texture
                m_animationTexture = new Texture2D(size.x, size.y, GraphicsFormat.R16G16B16A16_SFloat, 0, TextureCreationFlags.None)
                {
                    name = $"Anim_{m_config.animator.name}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0,
                };

                m_animationTexture.SetPixelData(texture, 0);
                m_animationTexture.Apply(false, true);

                return false;
            }
            finally
            {
                AnimationMode.EndSampling();
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

        Bounds BakeAnimation(ushort[] texture, Vector2Int textureSize, AnimationClip animation, RectInt region)
        {
            var animator = m_config.animator.gameObject;
            var renderers = m_config.renderers;

            // Bake the animation to the texture, while finding the bounds of the meshes
            // during the course of the animation.
            var bounds = default(Bounds);
            var boundsInitialized = false;
            var boundsMeshes = new Mesh[renderers.Length];

            for (var i = 0; i < boundsMeshes.Length; i++)
            {
                boundsMeshes[i] = new Mesh();
            }

            for (var frame = 0; frame < region.width; frame++)
            {
                var normalizedTime = (float)frame / region.width;
                var time = normalizedTime * animation.length;

                // play a frame in the animation
                AnimationMode.SampleAnimationClip(animator, animation, time);

                for (var bone = 0; bone < m_bones.Length; bone++)
                {
                    // get the offset from the bind pose to the current pose for the bone
                    var root = animator.transform;
                    var t = m_bones[bone];

                    var pos = root.InverseTransformPoint(t.position);
                    var rot = t.rotation * m_bindPoses[bone].rotation;

                    // write the pose to the animation texture
                    var x = region.x + frame;
                    var y = region.y + bone;
                    SetValue(texture, textureSize, x, y, pos);
                    SetValue(texture, textureSize, x, y + (region.height / 2), new Vector4(rot.x, rot.y, rot.z, rot.w));
                }

                // calculate the bounds for the meshes for the frame in the animator's space
                for (var i = 0; i < boundsMeshes.Length; i++)
                {
                    var mesh = boundsMeshes[i];
                    var renderer = renderers[i];
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
            }

            for (var i = 0; i < boundsMeshes.Length; i++)
            {
                Object.DestroyImmediate(boundsMeshes[i]);
            }

            return bounds;
        }

        void SetValue(ushort[] texture, Vector2Int textureSize, int x, int y, Vector4 value)
        {
            var i = ((y * textureSize.x) + x) * 4;

            texture[i] = Mathf.FloatToHalf(value.x);
            texture[i + 1] = Mathf.FloatToHalf(value.y);
            texture[i + 2] = Mathf.FloatToHalf(value.z);
            texture[i + 3] = Mathf.FloatToHalf(value.w);
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

                var asset = InstancedAnimationAsset.Create(m_meshes.ToArray(), m_animationTexture, m_animations.ToArray());

                // Save the generated asset and meshes. The asset file extention is special and is recognized by unity.
                var uniquePath = AssetDatabase.GenerateUniqueAssetPath($"{assetPath}/{m_config.animator.name}.asset");
                AssetDatabase.CreateAsset(asset, uniquePath);

                foreach (var mesh in m_meshes)
                {
                    AssetDatabase.AddObjectToAsset(mesh.Mesh, asset);
                }
                AssetDatabase.AddObjectToAsset(m_animationTexture, asset);

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

        static RectInt[] Pack(Vector2Int[] boxes, out Vector2Int packedSize)
        {
            var area = 0;
            var minWidth = 0;

            for (var i = 0; i < boxes.Length; i++)
            {
                var size = boxes[i];
                area += size.x * size.y;
                minWidth = Mathf.Max(minWidth, size.x);
            }

            var sortedByWidth = Enumerable.Range(0, boxes.Length)
                .OrderByDescending(i => boxes[i].x)
                .ToArray();

            // we want a squarish container
            var width = Mathf.Max(minWidth, Mathf.CeilToInt(Mathf.Sqrt(area / 0.95f)));

            var spaces = new List<RectInt>()
            {
                new RectInt(0, 0, width, int.MaxValue),
            };

            packedSize = Vector2Int.zero;
            var packed = new RectInt[boxes.Length];

            for (var i = 0; i < sortedByWidth.Length; i++)
            {
                var boxIndex = sortedByWidth[i];
                var box = boxes[boxIndex];

                // pack the box in the smallest free space
                for (var j = spaces.Count - 1; j >= 0; j--)
                {
                    var space = spaces[j];

                    if (box.x > space.width || box.y > space.height)
                    {
                        continue;
                    }

                    var packedBox = new RectInt(space.x, space.y, box.x, box.y);
                    packed[boxIndex] = packedBox;
                    packedSize = Vector2Int.Max(packedSize, packedBox.max);

                    if (box.x == space.width && box.y == space.height)
                    {
                        spaces.RemoveAt(j);
                    }
                    else if (box.x == space.width)
                    {
                        space.y += box.y;
                        space.height -= box.y;
                        spaces[j] = space;
                    }
                    else if (box.y == space.height)
                    {
                        space.x += box.x;
                        space.width -= box.x;
                        spaces[j] = space;
                    }
                    else
                    {
                        spaces.Add(new RectInt
                        {
                            x = space.x + box.x,
                            y = space.y,
                            width = space.width - box.x,
                            height = box.y,
                        });

                        space.y += box.y;
                        space.height -= box.y;
                        spaces[j] = space;
                    }
                    break;
                }
            }

            return packed;
        }
    }
}
