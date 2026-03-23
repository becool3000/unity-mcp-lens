using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    record MutationPayload(object data = null, ReduxException error = null);
}
