using System;
using System.Reflection;
using System.Text;

namespace Unity.Relay
{
    static class ChannelNaming
    {
        /// <summary>
        /// Scan all public static fields implementing IRelayChannel on the given type
        /// and auto-assign the channel Name from the field name using PascalCase to dot.case.
        /// Fields that already have a non-empty Name (explicit override) are skipped.
        /// </summary>
        public static void AutoName(Type containerType)
        {
            foreach (var field in containerType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is IRelayChannel channel && string.IsNullOrEmpty(channel.Name))
                {
                    channel.Name = PascalToDotCase(field.Name);
                }
            }
        }

        /// <summary>
        /// Convert PascalCase to dot.separated.lowercase.
        /// Examples:
        ///   PersistenceLoad    → persistence.load
        ///   McpSessionRegister → mcp.session.register
        ///   CredentialReveal   → credential.reveal
        ///   Ping               → ping
        /// </summary>
        public static string PascalToDotCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (i > 0 && char.IsUpper(c))
                {
                    bool prevIsLower = !char.IsUpper(name[i - 1]);
                    bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);

                    // Insert dot before:
                    // - Uppercase preceded by lowercase (e.g., "Load" in "PersistenceLoad")
                    // - Uppercase followed by lowercase when preceded by uppercase (e.g., "Session" in "McpSession")
                    if (prevIsLower || (char.IsUpper(name[i - 1]) && nextIsLower))
                    {
                        sb.Append('.');
                    }
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }
    }
}
