using System;
using UnityEngine;

namespace Unity.AI.Animate.Motion
{
    /// <summary>
    /// Skeleton + list of Transforms that represent the joints of the skeleton.
    /// This ArmatureMapping use the GameObject it's placed on to find the root and parse its hierarchy and find the joints.
    /// </summary>
    class ArmatureMapping : MonoBehaviour
    {
        [SerializeField]
        Transform[] m_Joints = Array.Empty<Transform>();

        public Transform[] joints => m_Joints;
    }
}
