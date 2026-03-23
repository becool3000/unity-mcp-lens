using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.AI.Animate.Motion
{
    /// <summary>
    /// Manages a series of "PoseModels"—one for each frame—and can bake from a VideoToMotionResponse.
    /// Then, optionally, you can export them or apply them to an Animator, etc.
    /// </summary>
    class Timeline
    {
        PoseModel[] m_Poses;

        const float k_OutputFps = 30f;

        /// <summary>
        /// Bake frames from a Response into this timeline, capturing local transforms
        /// and applying the response data to each frame's PoseModel.
        /// Optionally resamples if input FPS != 30.
        /// </summary>
        public bool BakeFromResponse(IReadOnlyList<MotionResponse.Frame> responseFrames, float responseFps, ArmatureMapping armature)
        {
            if (responseFrames == null || responseFrames.Count == 0 || armature == null)
                return false;

            // Allocate a PoseModel per frame
            m_Poses = new PoseModel[responseFrames.Count];
            var numJoints = armature.joints?.Length ?? 0;
            for (var i = 0; i < m_Poses.Length; i++)
                m_Poses[i] = new PoseModel(numJoints);

            // Normalize quaternion continuity in the response frames
            NormalizeResponseFrameQuaternions(responseFrames);

            // Fill local transforms from the frame data
            for (var frameIndex = 0; frameIndex < responseFrames.Count; frameIndex++)
            {
                var frame = responseFrames[frameIndex]; // positions and rotations
                // We first capture the local transforms from the *current* scene state
                // (assuming the armature is in some default T-pose).
                // Then we overwrite the joints to match the new root position + rotation array.

                var pose = m_Poses[frameIndex];
                pose.CaptureLocal(armature);

                // Root is joint 0. If positions[] has at least 1 element, set that as the root pos
                if (frame.positions.Length > 0 && frameIndex < m_Poses.Length)
                {
                    var rootPos = frame.positions[0];
                    // Overwrite the "pos" in the local array
                    var rt = pose.local[0];
                    pose.local[0] = new RigidTransform(rt.rot, rootPos);
                }

                // If rotations[] matches the number of joints, apply them all
                for (var j = 0; j < numJoints && j < frame.rotations.Length; j++)
                {
                    var localRT = pose.local[j];
                    pose.local[j] = new RigidTransform(frame.rotations[j], localRT.pos);
                }

                m_Poses[frameIndex] = pose;
            }

            // If responseFps != 30, resample to 30
            if (Mathf.Abs(responseFps - k_OutputFps) > 0.01f)
                ResampleInPlace(responseFps, k_OutputFps);

            return true;
        }

        /// <summary>
        /// Normalizes quaternions in the response frames to ensure continuity
        /// </summary>
        static void NormalizeResponseFrameQuaternions(IReadOnlyList<MotionResponse.Frame> frames)
        {
            if (frames is not { Count: > 1 })
                return;

            // Process each joint separately
            var numJoints = frames[0].rotations.Length;
            for (var jointIndex = 0; jointIndex < numJoints; jointIndex++)
            {
                // For each joint, ensure quaternion continuity across all frames
                for (var frameIndex = 1; frameIndex < frames.Count; frameIndex++)
                {
                    var refQuat = frames[frameIndex - 1].rotations[jointIndex];
                    var currentQuat = frames[frameIndex].rotations[jointIndex];

                    // Normalize current quaternion relative to previous one
                    frames[frameIndex].rotations[jointIndex] = MotionUtilities.NormalizeQuaternionContinuity(refQuat, currentQuat);
                }
            }
        }

        /// <summary>
        /// Resample the local poses in this timeline to a new framerate, overwriting m_Poses.
        /// Then re-apply them to the given ArmatureMapping so that further steps can capture or export if desired.
        /// </summary>
        void ResampleInPlace(float oldFps, float newFps)
        {
            if (m_Poses == null || m_Poses.Length == 0)
                return;

            var totalTime = m_Poses.Length / oldFps;
            var newCount = Mathf.RoundToInt(totalTime * newFps);
            if (newCount <= 0)
                newCount = 1;

            var oldPoses = m_Poses;
            m_Poses = new PoseModel[newCount];

            var numJoints = oldPoses[0].local.Length;
            for (var i = 0; i < newCount; i++)
                m_Poses[i] = new PoseModel(numJoints);

            // First, ensure quaternion continuity in the old poses for better interpolation
            EnsureQuaternionContinuityInPoses(oldPoses);

            for (var i = 0; i < newCount; i++)
            {
                var t = i / newFps;
                var oldIndexFloat = t * oldFps;
                var idx0 = Mathf.FloorToInt(oldIndexFloat);
                var frac = oldIndexFloat - idx0;
                var idx1 = idx0 + 1;
                if (idx0 >= oldPoses.Length)
                    idx0 = oldPoses.Length - 1;
                if (idx1 >= oldPoses.Length)
                    idx1 = oldPoses.Length - 1;

                var poseA = oldPoses[idx0];
                var poseB = oldPoses[idx1];

                m_Poses[i].InterpolateLocal(poseA, poseB, frac);
            }

            // Apply quaternion continuity to the resampled poses too
            EnsureQuaternionContinuityInPoses(m_Poses);
        }

        /// <summary>
        /// Ensures quaternion continuity across all poses for each joint
        /// </summary>
        static void EnsureQuaternionContinuityInPoses(PoseModel[] poses)
        {
            if (poses is not { Length: > 1 })
                return;

            var numJoints = poses[0].local.Length;

            // For each joint
            for (var jointIndex = 0; jointIndex < numJoints; jointIndex++)
            {
                // Create a temporary array of just this joint's quaternions
                var jointQuats = new Quaternion[poses.Length];
                for (var i = 0; i < poses.Length; i++)
                {
                    jointQuats[i] = poses[i].local[jointIndex].rot;
                }

                // Normalize the array
                MotionUtilities.EnsureQuaternionContinuity(jointQuats);

                // Put normalized quaternions back into poses
                for (var i = 0; i < poses.Length; i++)
                {
                    var local = poses[i].local[jointIndex];
                    poses[i].local[jointIndex] = new RigidTransform(jointQuats[i], local.pos);
                }
            }
        }

        /// <summary>
        /// Export the (already baked) timeline to a humanoid AnimationClip
        /// using the provided ArmatureMapping (which must have an Animator & Avatar).
        /// </summary>
        /// <param name="poseArmature">The armature mapping to use for exporting</param>
        /// <returns>The created animation clip</returns>
        public AnimationClip ExportToHumanoidClip(ArmatureMapping poseArmature)
        {
            if (!poseArmature.TryGetComponent<Animator>(out var animator))
                return null;

            var clip = new AnimationClip { legacy = false };

            if (m_Poses == null || m_Poses.Length == 0)
                return clip;

            using var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var humanPose = new HumanPose();
            handler.GetHumanPose(ref humanPose);

            var rootPos = MotionUtilities.PositionCurve.New();
            var rootRot = MotionUtilities.RotationCurve.New();

            var muscleCurves = new AnimationCurve[humanPose.muscles.Length];
            for (var i = 0; i < muscleCurves.Length; i++)
                muscleCurves[i] = new AnimationCurve();

            // Store all keyframe values for post-processing
            var rootQuaternions = new Quaternion[m_Poses.Length];

            for (var f = 0; f < m_Poses.Length; f++)
            {
                var time = f / k_OutputFps;
                m_Poses[f].ApplyLocal(poseArmature, Vector3.zero, Quaternion.identity);
                handler.GetHumanPose(ref humanPose);

                // Store quaternion for later normalization
                rootQuaternions[f] = humanPose.bodyRotation;

                // Add position keys
                rootPos.AddKey(time, humanPose.bodyPosition);

                for (var m = 0; m < humanPose.muscles.Length; m++)
                {
                    muscleCurves[m].AddKey(time, humanPose.muscles[m]);
                }
            }

            // Normalize quaternion continuity before creating rotation curve
            MotionUtilities.EnsureQuaternionContinuity(rootQuaternions);

            // Now add the normalized quaternions to the rotation curve
            for (var f = 0; f < m_Poses.Length; f++)
            {
                var time = f / k_OutputFps;
                rootRot.AddKey(time, rootQuaternions[f]);
            }

            clip.SetHumanoidCurves(rootPos, rootRot, muscleCurves);

            return clip;
        }
    }
}
