using System;
using UnityEngine;

namespace Unity.AI.Animate.Motion
{
    static class MotionUtilities
    {
        // ----------------
        // Simple base64 decode (inline, no pooled arrays)
        // ----------------
        public static float[] DecodeFloatsFromBase64(string base64, int floatsPerElement, out int elemCount)
        {
            if (string.IsNullOrEmpty(base64))
            {
                elemCount = 0;
                return Array.Empty<float>();
            }

            var bytes = Convert.FromBase64String(base64);
            var totalFloats = bytes.Length / 4;
            elemCount = totalFloats / floatsPerElement;

            // Convert the raw bytes to single-precision floats
            var floats = new float[totalFloats];
            for (var i = 0; i < totalFloats; i++)
                floats[i] = BitConverter.ToSingle(bytes, i * 4);

            return floats;
        }

        // ----------------
        // Utility structs for building root positions/rotations
        // ----------------

        public struct PositionCurve
        {
            public AnimationCurve x, y, z;
            public static PositionCurve New() => new() { x = new(), y = new(), z = new() };
            public void AddKey(float time, Vector3 position)
            {
                x.AddKey(time, position.x);
                y.AddKey(time, position.y);
                z.AddKey(time, position.z);
            }
        }

        public struct RotationCurve
        {
            public AnimationCurve x, y, z, w;
            public static RotationCurve New() => new() { x = new(), y = new(), z = new(), w = new() };
            public void AddKey(float time, Quaternion rotation)
            {
                x.AddKey(time, rotation.x);
                y.AddKey(time, rotation.y);
                z.AddKey(time, rotation.z);
                w.AddKey(time, rotation.w);
            }
        }

        /// <summary>
        /// Helper to set the root transforms and muscle curves on an AnimationClip,
        /// then fix quaternion continuity.
        /// </summary>
        public static void SetHumanoidCurves(
            this AnimationClip clip,
            PositionCurve rootPos,
            RotationCurve rootRot,
            AnimationCurve[] muscleCurves)
        {
            clip.ClearCurves();

            // Root position
            clip.SetCurve("", typeof(Animator), "RootT.x", rootPos.x);
            clip.SetCurve("", typeof(Animator), "RootT.y", rootPos.y);
            clip.SetCurve("", typeof(Animator), "RootT.z", rootPos.z);

            // Root rotation
            clip.SetCurve("", typeof(Animator), "RootQ.x", rootRot.x);
            clip.SetCurve("", typeof(Animator), "RootQ.y", rootRot.y);
            clip.SetCurve("", typeof(Animator), "RootQ.z", rootRot.z);
            clip.SetCurve("", typeof(Animator), "RootQ.w", rootRot.w);

            // Muscles
            for (var i = 0; i < muscleCurves.Length; i++)
            {
                var muscleName = HumanTrait.MuscleName[i];
                // Fix hand muscle names to match Unity's standard format
                muscleName = FixHandMuscleName(muscleName);
                clip.SetCurve("", typeof(Animator), muscleName, muscleCurves[i]);
            }
        }

        /// <summary>
        /// Ensures quaternion continuity by flipping quaternions if needed to ensure
        /// the shortest interpolation path. This helps prevent quaternion flips in animations.
        /// </summary>
        /// <param name="quaternions">Array of quaternions to normalize</param>
        public static void EnsureQuaternionContinuity(Quaternion[] quaternions)
        {
            if (quaternions is not { Length: > 1 })
                return;

            for (var i = 1; i < quaternions.Length; i++)
            {
                // Calculate dot product between adjacent quaternions
                var dot = Quaternion.Dot(quaternions[i-1], quaternions[i]);

                // If dot product is negative, the quaternions are in opposite hemispheres
                // Flip the current quaternion to ensure smooth interpolation
                if (dot < 0f)
                {
                    quaternions[i] = new Quaternion(-quaternions[i].x, -quaternions[i].y, -quaternions[i].z, -quaternions[i].w);
                }
            }
        }

        /// <summary>
        /// Ensures quaternion continuity relative to a reference quaternion
        /// </summary>
        public static Quaternion NormalizeQuaternionContinuity(Quaternion reference, Quaternion target) =>
            Quaternion.Dot(reference, target) < 0f ? new Quaternion(-target.x, -target.y, -target.z, -target.w) : target;

        /// <summary>
        /// Transforms standard muscle names to Unity's expected format for finger muscles.
        /// e.g.: "Left Index 1 Stretched" becomes "LeftHand.Index.1 Stretched"
        /// </summary>
        static string FixHandMuscleName(string name)
        {
            var newName = name;
            newName = newName.Replace("Left Index ", "LeftHand.Index.");
            newName = newName.Replace("Left Middle ", "LeftHand.Middle.");
            newName = newName.Replace("Left Ring ", "LeftHand.Ring.");
            newName = newName.Replace("Left Thumb ", "LeftHand.Thumb.");
            newName = newName.Replace("Left Little ", "LeftHand.Little.");

            newName = newName.Replace("Right Index ", "RightHand.Index.");
            newName = newName.Replace("Right Middle ", "RightHand.Middle.");
            newName = newName.Replace("Right Ring ", "RightHand.Ring.");
            newName = newName.Replace("Right Thumb ", "RightHand.Thumb.");
            newName = newName.Replace("Right Little ", "RightHand.Little.");

            return newName;
        }
    }
}
