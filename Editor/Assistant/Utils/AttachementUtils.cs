using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class AttachmentUtils
    {
        public static List<Object> GetValidAttachment(List<Object> contextAttachments)
        {
            if (contextAttachments == null)
                return new List<Object>();

            if (contextAttachments.Any(obj => obj == null))
                return contextAttachments.Where(obj => obj != null).ToList();

            return contextAttachments;
        }
    }
}
