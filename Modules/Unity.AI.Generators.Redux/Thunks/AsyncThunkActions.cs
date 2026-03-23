using System;

namespace Unity.AI.Generators.Redux.Thunks
{
    record PendingAction<TArg>(string requestId, TArg arg) : StandardAction("");
    record FulfilledAction<TPayload>(TPayload payload) : StandardAction<TPayload>(payload);
    record RejectedAction(Exception error) : StandardAction("");
    record ProgressAction(float payload = 0f) : StandardAction<float>(payload: payload);
    record ProgressAction<TPayload>(TPayload payload, string type = "") : StandardAction<TPayload>(type: type, payload: payload);
}
