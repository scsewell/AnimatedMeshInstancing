using System;
using UnityEngine;

namespace Framework.Rendering.InstancedAnimation
{
    /// <summary>
    /// A class that stores a baked animation.
    /// </summary>
    [Serializable]
    public class Animation
    {
        [SerializeField]
        [Tooltip("The baked animation texture.")]
        Texture2D m_texture;

        [SerializeField]
        [Tooltip("The length of the animation in seconds.")]
        float m_length;

        [SerializeField]
        [Tooltip("The frames per second of the animation.")]
        float m_fps;

        [SerializeField]
        [Tooltip("The bounds of the meshes during this animation.")]
        Bounds m_bounds;

        /// <summary>
        /// The baked animation texture.
        /// </summary>
        public Texture2D Texture => m_texture;

        /// <summary>
        /// The length of the animation in seconds.
        /// </summary>
        public float Length => m_length;

        /// <summary>
        /// The frames per second of the animation.
        /// </summary>
        public float Fps => m_fps;

        /// <summary>
        /// The bounds of the meshes during this animation.
        /// </summary>
        public Bounds Bounds => m_bounds;

        /// <summary>
        /// Creates a new <see cref="Animation"/> instance.
        /// </summary>
        /// <param name="texture">The baked animation texture.</param>
        /// <param name="length">The length of the animation in seconds.</param>
        /// <param name="fps">The frames per second of the animation.</param>
        /// <param name="bounds">The bounds of the meshes during this animation.</param>
        public Animation(Texture2D texture, float length, float fps, Bounds bounds)
        {
            if (texture == null)
            {
                throw new ArgumentNullException(nameof(texture));
            }
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "Must be greater than 0!");
            }
            if (fps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fps), fps, "Must be greater than 0!");
            }

            m_texture = texture;
            m_length = length;
            m_fps = fps;
            m_bounds = bounds;
        }
    }
}
