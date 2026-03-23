using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Bridge.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Editor.Api
{
    static partial class AssistantApi
    {
        /// <summary>
        /// Context to attach to an agent run.
        /// </summary>
        public class AttachedContext
        {
            public bool IsEmpty => !Objects.Any() && !Logs.Any() && !VirtualAttachments.Any();

            /// <summary>
            /// List of objects to attach to the context.
            /// </summary>
            public List<Object> Objects { get; } = new();

            /// <summary>
            /// List of logs to attach to the context.
            /// </summary>
            public List<LogData> Logs { get; } = new();

            /// <summary>
            /// List of virtual attachments to attach to the context.
            /// </summary>
            public List<VirtualAttachment> VirtualAttachments { get; } = new();

            /// <summary>
            /// Attach a UnityEngine.Object
            /// </summary>
            /// <param name="obj">The object to attach</param>
            public void Add(Object obj) => Objects.Add(obj);
            
            /// <summary>
            /// Attach multiple UnityEngine.Object instances
            /// </summary>
            /// <param name="objs">The objects to attach</param>
            public void AddRange(IEnumerable<Object> objs) => Objects.AddRange(objs);
            
            /// <summary>
            /// Attach a Console Log item
            /// </summary>
            /// <param name="log">The log to attach</param>
            public void Add(LogData log) => Logs.Add(log);
            
            /// <summary>
            /// Attach multiple Console Log items
            /// </summary>
            /// <param name="logs">The items to attach</param>
            public void AddRange(IEnumerable<LogData> logs) => Logs.AddRange(logs);
            
            /// <summary>
            /// Attach a custom data item
            /// </summary>
            /// <param name="attachment">The item to attach</param>
            public void Add(VirtualAttachment attachment) => VirtualAttachments.Add(attachment);
            
            /// <summary>
            /// Attach multiple custom data items
            /// </summary>
            /// <param name="attachments">The items to attach</param>
            public void AddRange(IEnumerable<VirtualAttachment> attachments) => VirtualAttachments.AddRange(attachments);

            /// <summary>
            /// Attach the content of the given texture as image data
            /// Only use this when Vision capabilities are needed to analyze the actual content of an image.
            /// Otherwise, consider attaching the Texture as an Object instead.
            /// </summary>
            /// <param name="texture">The source image</param>
            /// <param name="displayName">An optional display name (used for backend logging)</param>
            public void AddImageContent(Texture texture, string displayName = "")
            {
                if (texture == null)
                    return;
                
                var attachment = texture.GetImageContentAttachment(displayName);
                VirtualAttachments.Add(attachment);
            }
        }
    }
}
