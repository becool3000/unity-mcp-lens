using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.UI.Editor.Scripts;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Api
{
    static class ContextUtils
    {
        public static ContextBuilder GetBuilder(this AssistantApi.AttachedContext attachedContext)
        {
            var contextBuilder = new ContextBuilder();

            // Objects
            foreach (var contextObject in attachedContext.Objects)
            {
                var contextItem = new UnityObjectContextSelection();
                contextItem.SetTarget(contextObject);
                contextBuilder.InjectContext(contextItem);
            }

            // Logs
            foreach (var contextLog in attachedContext.Logs)
            {
                var contextItem = new ConsoleContextSelection();
                contextItem.SetTarget(contextLog);
                contextBuilder.InjectContext(contextItem);
            }

            // Virtual
            foreach (var contextVirtual in attachedContext.VirtualAttachments)
            {
                var contextItem = contextVirtual.ToContextSelection();
                contextBuilder.InjectContext(contextItem);
            }

            return contextBuilder;
        }

        public static void AttachContext(this AssistantBlackboard blackboard, AssistantApi.AttachedContext attachedContext)
        {
            if (attachedContext == null)
                return;

            foreach (var contextObject in attachedContext.Objects)
            {
                blackboard.AddObjectAttachment(contextObject);
            }

            foreach (var contextLog in attachedContext.Logs)
            {
                blackboard.AddConsoleAttachment(contextLog);
            }

            foreach (var contextVirtual in attachedContext.VirtualAttachments)
            {
                blackboard.AddVirtualAttachment(contextVirtual);
            }
        }
        
        public static VirtualAttachment GetImageContentAttachment(this Texture texture, string displayName)
        {
            var processedImage = TextureUtils.ProcessTextureToBase64(texture);
            if (string.IsNullOrEmpty(processedImage.Base64Data))
                return null;

            var metaData = new ImageContextMetaData
            {
                Category = ImageContextCategory.Image,
                Width = processedImage.Width,
                Height = processedImage.Height,
                Size = processedImage.SizeInBytes,
                Format = "png"  // ProcessTextureToBase64 always encode as PNG data
            };
            var attachment = new VirtualAttachment(processedImage.Base64Data, "Image", displayName, metaData);
            return attachment;
        }
    }
}
