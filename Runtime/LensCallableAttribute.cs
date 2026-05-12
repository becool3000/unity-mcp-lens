using System;

namespace Becool.UnityMcpLens.Runtime
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class LensCallableAttribute : Attribute
    {
        public LensCallableAttribute()
        {
        }

        public LensCallableAttribute(string description)
        {
            Description = description;
        }

        public string Description { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class LensSmokeActionAttribute : LensCallableAttribute
    {
        public LensSmokeActionAttribute()
        {
        }

        public LensSmokeActionAttribute(string description)
            : base(description)
        {
        }
    }
}
