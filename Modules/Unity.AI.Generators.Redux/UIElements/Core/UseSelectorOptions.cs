using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Generators.UIElements.Core
{
    record UseSelectorOptions<TResult>(
        Store store = null,
        Selector<TResult> selector = null,
        Action<TResult> callback = null,
        IEqualityComparer<TResult> Comparer = null,     // Compares results for dispatching changes
        bool selectImmediately = true,
        TResult initialValue = default,
        bool waitForValue = false,                      // Wait until a non-default (eg: non-null) value has been returned before processing changes
        StackTrace sourceInfo = null);                  // Optional stacktrace containing original builder of option object to retrace source calls.
}
