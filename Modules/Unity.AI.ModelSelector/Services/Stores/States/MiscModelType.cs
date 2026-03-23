using System;
using System.Collections.Generic;

namespace Unity.AI.ModelSelector.Services.Stores.States
{
    enum MiscModelType
    {
        Favorites,
        Default,
        Custom
    }

    static class MiscModelTypeExtensions
    {
        static readonly MiscModelType[] k_AllValues = (MiscModelType[])Enum.GetValues(typeof(MiscModelType));

        public static IEnumerable<MiscModelType> EnumerateAll()
        {
            return k_AllValues;
        }
    }
}
