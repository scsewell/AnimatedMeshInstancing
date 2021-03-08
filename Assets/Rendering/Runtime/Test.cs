using System;
using UnityEngine;

namespace Framework.Rendering.Assets.Rendering.Runtime
{
    class Test : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            var animator = GetComponentInParent<Animator>().transform;
            var renderer = GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = renderer.sharedMesh;
            var bindPoses = mesh.bindposes;

            for (var i = 0; i < bindPoses.Length; i++)
            {
                var rendererBindPose = bindPoses[i].inverse;
                var bindPose = animator.worldToLocalMatrix * renderer.localToWorldMatrix * rendererBindPose;

                //Debug.Log($"{renderer.transform.rotation} {renderer.localToWorldMatrix.rotation}");
                
                Gizmos.DrawWireSphere(bindPose.GetColumn(3), 0.02f);
            }
        }
    }
}
