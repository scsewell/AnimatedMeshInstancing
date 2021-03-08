using System;
using UnityEngine;

namespace Framework.Rendering.InstancedAnimation
{
    [Serializable]
    public class BakedClip
    {
        [SerializeField]
        Texture2D m_animation;

        // We should store the max bounds of the mesh during this animation for accurate culling

        public Texture2D Animation => m_animation;

        /// <summary>
        /// Creates a new <see cref="Bounds"/> instance.
        /// </summary>
        /// <param name="animation"></param>
        public BakedClip(Texture2D animation)
        {
            m_animation = animation;
        }
    }
}
