using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class EnumUtils
    {
        public static T ParseByEnumMember<T>(string memberName)
            where T : Enum
        {
            var enumType = typeof(T);
            foreach (var name in Enum.GetNames(enumType))
            {
                var enumMemberAttribute = ((EnumMemberAttribute[])enumType.GetField(name).GetCustomAttributes(typeof(EnumMemberAttribute), true)).Single();
                if (enumMemberAttribute.Value == memberName) return (T)Enum.Parse(enumType, name);
            }

            throw new InvalidDataException("No such Enum member: " + memberName);
        }
    }
}
