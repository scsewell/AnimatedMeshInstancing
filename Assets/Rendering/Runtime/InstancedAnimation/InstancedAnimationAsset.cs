using UnityEngine;

namespace Framework.Rendering.InstancedAnimation
{
    /// <summary>
    /// An asset that stores content that can be played back using instanced animation.
    /// </summary>
    [CreateAssetMenu(fileName = "New InstancedAnimation", menuName = "Framework/Animation/Instanced Animation")]
    public class InstancedAnimationAsset : ScriptableObject
    {
        [SerializeField]
        [Tooltip("The meshes that can be used when playing these animation.")]
        BakedMesh[] m_meshes;

        [SerializeField]
        [Tooltip("The animations.")]
        BakedClip[] m_clips;

        internal BakedMesh[] meshes => m_meshes;

        internal BakedClip[] clips => m_clips;

        /// <summary>
        /// Creates a <see cref="InstancedAnimationAsset"/> instance.
        /// </summary>
        /// <param name="meshes"></param>
        /// <param name="clips"></param>
        public static InstancedAnimationAsset Create(BakedMesh[] meshes, BakedClip[] clips)
        {
            var asset = CreateInstance<InstancedAnimationAsset>();
            asset.m_meshes = meshes;
            asset.m_clips = clips;
            return asset;
        }
    }
}
