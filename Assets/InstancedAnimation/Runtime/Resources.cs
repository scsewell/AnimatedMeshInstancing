using UnityEngine;

namespace InstancedAnimation
{
    /// <summary>
    /// An asset that contains referenced to required resources.
    /// </summary>
    [CreateAssetMenu(fileName = "New Resources", menuName = "Framework/Animation/Resources")]
    public class Resources : ScriptableObject
    {
        [SerializeField]
        ComputeShader m_culling;

        public ComputeShader Culling => m_culling;
    }
}
