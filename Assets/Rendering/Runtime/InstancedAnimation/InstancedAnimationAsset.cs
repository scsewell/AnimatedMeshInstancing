using UnityEngine;

namespace Framework.Rendering.InstancedAnimation
{
    /// <summary>
    /// An asset that stores data needed for instanced animation playback.
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
