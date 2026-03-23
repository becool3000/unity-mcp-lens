using System;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    interface IProxy<T>
    {
        T Value { get; set; }
    }
}
