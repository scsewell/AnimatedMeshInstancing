using InstancedAnimation;

using UnityEngine;

public class InstancingTest : MonoBehaviour
{
    [SerializeField]
    int m_instanceCount = 5;

    InstancedAnimator m_animator;

    void Start()
    {
        m_animator = GetComponent<InstancedAnimator>();
    }

    void Update()
    {
        m_animator.InstanceCount = m_instanceCount;
    }
}
