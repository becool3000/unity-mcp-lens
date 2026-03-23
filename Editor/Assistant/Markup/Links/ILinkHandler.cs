using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// An interface for handling custom links in a markdown block
    /// </summary>
    interface ILinkHandler
    {
        /// <summary>
        /// Represents the context of a link handling operation
        /// </summary>
        public struct Context
        {
            /// <summary>
            /// The ID of the conversation from which the link was triggered
            /// </summary>
            public string ConversationId { get; }
            
            /// <summary>
            /// The conversation persistent storage
            /// </summary>
            public PersistentStorage PersistentStorage { get; }

            internal Context(string conversationId)
            {
                ConversationId = conversationId;
                PersistentStorage = new PersistentStorage(ConversationId);
            }
        }
        
        /// <summary>
        /// Handles the given link
        /// </summary>
        /// <param name="context">The context in which the link was triggered</param>
        /// <param name="prefix">
        /// The prefix that was used for this url.
        /// For instance 'custom://my_link' would result in the prefix 'custom'
        /// </param>
        /// <param name="url">
        /// The link to handle, excluding any prefix.
        /// For instance 'custom://my_link' would result in the link 'my_link'
        /// </param>
        public void Handle(Context context, string prefix, string url);
    }
}
