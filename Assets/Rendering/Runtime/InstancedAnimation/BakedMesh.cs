using System;

using UnityEngine;

namespace Framework.Rendering.InstancedAnimation
{
    [Serializable]
    public class BakedMesh
    {
        [SerializeField]
        Mesh m_mesh;

        public Mesh Mesh => m_mesh;

        public BakedMesh(Mesh mesh)
        {
            m_mesh = mesh;
        }
    }
}
