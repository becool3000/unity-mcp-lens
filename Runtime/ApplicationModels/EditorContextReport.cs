using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.ApplicationModels
{
    class EditorContextReport
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NonGeneratedPartials.EditorContextReport" /> class.
        /// </summary>
        [JsonConstructor]
        protected EditorContextReport() { }
        public EditorContextReport(List<ContextItem> attachedContext, int characterLimit, List<ContextItem> extractedContext)
        {
            AttachedContext = attachedContext;
            CharacterLimit = characterLimit;
        }

        /// <summary>
        /// Gets or Sets AttachedContext
        /// </summary>
        [DataMember(Name = "attached_context", IsRequired = true, EmitDefaultValue = true)]
        public List<ContextItem> AttachedContext { get; set; }

        /// <summary>
        /// Gets or Sets CharacterLimit
        /// </summary>
        [DataMember(Name = "character_limit", IsRequired = true, EmitDefaultValue = true)]
        public int CharacterLimit { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class EditorContextReport {\n");
            sb.Append("  AttachedContext: ").Append(AttachedContext).Append("\n");
            sb.Append("  CharacterLimit: ").Append(CharacterLimit).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Returns the JSON string presentation of the object
        /// </summary>
        /// <returns>JSON string presentation of the object</returns>
        public virtual string ToJson()
        {
            return AssistantJsonHelper.Serialize(this);
        }

        internal void Sort()
        {
            AttachedContext.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }
}
