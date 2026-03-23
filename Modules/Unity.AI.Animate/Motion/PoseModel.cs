using System;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.AI.Animate.Motion
{
    /// <summary>
    /// A single frame’s worth of local transforms for each joint
    /// </summary>
    readonly struct PoseModel
    {
        // local transforms for each joint
        public readonly RigidTransform[] local;

        public PoseModel(int numJoints) => local = new RigidTransform[numJoints];

        /// <summary>
        /// Capture the current local transforms from the given ArmatureMapping's joints.
        /// </summary>
        public void CaptureLocal(ArmatureMapping armature)
        {
            if (armature == null || armature.joints == null)
                return;

            for (var i = 0; i < armature.joints.Length; i++)
            {
                var t = armature.joints[i];

                if (t)
                {
                    // local hinged on parent, which is typical for child joints
                    local[i] = new RigidTransform(t.localRotation, t.localPosition);
                    continue;
                }

                // If the expected joint is missing, use the parent's transform as a fallback if possible.
                // For the root (i == 0), or if there's no parent, fallback to identity.
                if (i > 0 && armature.joints[i - 1] != null)
                {
                    var parent = armature.joints[i - 1];
                    local[i] = new RigidTransform(parent.localRotation, parent.localPosition);
                }
                else
                {
                    local[i] = new RigidTransform(Quaternion.identity, Vector3.zero);
                }
            }
        }

        /// <summary>
        /// Apply the local transforms back onto the ArmatureMapping's joints
        /// </summary>
        /// <param name="armature"></param>
        /// <param name="translation">Optional root offset</param>
        /// <param name="rotation">Optional root rotation</param>
        public void ApplyLocal(ArmatureMapping armature, Vector3 translation, Quaternion rotation)
        {
            if (armature == null || armature.joints == null)
                return;

            for (var i = 0; i < armature.joints.Length; i++)
            {
                var rt = local[i];
                var jointTransform = armature.joints[i];
                if (!jointTransform)
                    continue;

                if (i == 0)
                {
                    // root joint gets offset/rotation
                    var pos = translation + rotation * rt.pos;
                    var rot = rotation * rt.rot;
                    jointTransform.SetPositionAndRotation(pos, rot);
                }
                else
                {
                    // child joint remains local
                    jointTransform.localPosition = rt.pos;
                    jointTransform.localRotation = rt.rot;
                }
            }
        }

        /// <summary>
        /// Interpolate the local transforms between two poses (A and B).
        /// </summary>
        public void InterpolateLocal(in PoseModel a, in PoseModel b, float t)
        {
            for (var i = 0; i < local.Length; i++)
            {
                var from = a.local[i];
                var to = b.local[i];

                // Normalize quaternion for continuity before interpolation
                var normalizedTo = new RigidTransform(
                    MotionUtilities.NormalizeQuaternionContinuity(from.rot, to.rot),
                    to.pos);

                local[i] = InterpolateRigid(from, normalizedTo, t);
            }
        }

        static RigidTransform InterpolateRigid(in RigidTransform from, in RigidTransform to, float t)
        {
            var pos = math.lerp(from.pos, to.pos, t);
            var rot = math.slerp(from.rot, to.rot, t);
            return new RigidTransform(rot, pos);
        }
    }
}
