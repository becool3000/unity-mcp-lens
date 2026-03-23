using System;
using System.Reflection;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Video;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// A reflection-based wrapper for the internal UnityEditorInternal.Media.MediaDecoder class.
    /// This class provides direct, low-level access to video frames.
    /// It is IDisposable and must be disposed to release native resources.
    /// </summary>
    class MediaDecoderReflected : IDisposable
    {
        static readonly Type k_MediaDecoderType;
        static readonly ConstructorInfo k_ConstructorFromClip;
        static readonly ConstructorInfo k_ConstructorFromPath;
        static readonly MethodInfo k_GetNextFrameMethod;
        static readonly MethodInfo k_SetPositionMethod;
        static readonly MethodInfo k_DisposeMethod;

        const BindingFlags k_BindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        object m_DecoderInstance;

        static MediaDecoderReflected()
        {
            // The internal type is in the Unity.Media.Editor assembly.
            k_MediaDecoderType = Type.GetType("UnityEditorInternal.Media.MediaDecoder, UnityEditor");

            if (k_MediaDecoderType == null)
            {
                Debug.LogError("MediaDecoderReflected: Could not find internal class UnityEditorInternal.Media.MediaDecoder. This may be due to a Unity version change.");
                return;
            }

            k_ConstructorFromClip = k_MediaDecoderType.GetConstructor(k_BindingFlags, null, new[] { typeof(VideoClip) }, null);
            k_ConstructorFromPath = k_MediaDecoderType.GetConstructor(k_BindingFlags, null, new[] { typeof(string) }, null);
            k_GetNextFrameMethod = k_MediaDecoderType.GetMethod("GetNextFrame", k_BindingFlags);
            k_SetPositionMethod = k_MediaDecoderType.GetMethod("SetPosition", k_BindingFlags);
            k_DisposeMethod = k_MediaDecoderType.GetMethod("Dispose", k_BindingFlags);

            if (k_ConstructorFromClip == null || k_ConstructorFromPath == null || k_GetNextFrameMethod == null || k_SetPositionMethod == null || k_DisposeMethod == null)
            {
                Debug.LogError("MediaDecoderReflected: Could not find one or more required methods or constructors on MediaDecoder. This may be due to a Unity version change.");
            }
        }

        /// <summary>
        /// Creates a new MediaDecoder for the given VideoClip.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the internal MediaDecoder class or its methods cannot be found via reflection.</exception>
        /// <exception cref="TargetInvocationException">Thrown if the internal constructor fails to create the decoder for the provided clip.</exception>
        public MediaDecoderReflected(VideoClip clip)
        {
            if (k_ConstructorFromClip == null)
            {
                throw new InvalidOperationException("MediaDecoderReflected is not initialized properly. Check for reflection errors on startup.");
            }
            m_DecoderInstance = k_ConstructorFromClip.Invoke(new object[] { clip });
        }

        /// <summary>
        /// Creates a new MediaDecoder for the given file path.
        /// This allows decoding video frames without importing the file as a Unity asset.
        /// </summary>
        /// <param name="filePath">Absolute path to the video file (e.g., mp4).</param>
        /// <exception cref="InvalidOperationException">Thrown if the internal MediaDecoder class or its methods cannot be found via reflection.</exception>
        /// <exception cref="TargetInvocationException">Thrown if the internal constructor fails to open the file.</exception>
        public MediaDecoderReflected(string filePath)
        {
            if (k_ConstructorFromPath == null)
            {
                throw new InvalidOperationException("MediaDecoderReflected file path constructor is not available. Check for reflection errors on startup.");
            }
            m_DecoderInstance = k_ConstructorFromPath.Invoke(new object[] { filePath });
        }

        /// <summary>
        /// Reads the next available video frame into the provided Texture2D.
        /// </summary>
        /// <param name="texture">The texture to write the frame data into. Its dimensions must match the video.</param>
        /// <param name="time">The timestamp of the decoded frame.</param>
        /// <returns>True if a frame was successfully decoded, false otherwise.</returns>
        public bool GetNextFrame(Texture2D texture, out MediaTime time)
        {
            if (m_DecoderInstance == null)
            {
                time = default;
                return false;
            }

            // For methods with 'out' parameters, we need to create an array to pass.
            // The 'out' parameter will be populated in this array after the call.
            var parameters = new object[] { texture, null };
            var success = (bool)k_GetNextFrameMethod.Invoke(m_DecoderInstance, parameters);

            // Retrieve the value from the 'out' parameter.
            time = (MediaTime)parameters[1];

            return success;
        }

        /// <summary>
        /// Seeks the decoder to a specific time.
        /// </summary>
        /// <param name="time">The time to seek to.</param>
        /// <returns>True if the seek was successful, false otherwise.</returns>
        public bool SetPosition(MediaTime time)
        {
            if (m_DecoderInstance == null) return false;
            return (bool)k_SetPositionMethod.Invoke(m_DecoderInstance, new object[] { time });
        }

        /// <summary>
        /// Releases the native resources used by the decoder.
        /// </summary>
        public void Dispose()
        {
            if (m_DecoderInstance != null)
            {
                k_DisposeMethod.Invoke(m_DecoderInstance, null);
                m_DecoderInstance = null;
            }
        }
    }
}
