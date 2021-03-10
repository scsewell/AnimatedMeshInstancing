using System;

using UnityEngine;

namespace Framework.Rendering.InstancedAnimation
{
    [Serializable]
    public class BakedMesh
    {
        [SerializeField]
        Mesh m_mesh;
        [SerializeField]
        Material[] m_materials;

        public Mesh Mesh => m_mesh;
        public Material[] Materials => m_materials;

        public BakedMesh(Mesh mesh, Material[] materials)
        {
            m_mesh = mesh;
            m_materials = materials;
        }
    }
}
