using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Image.Services.Stores.Actions.Creators
{
    // Image Editor Context Action type.
    record ContextAction<TPayload>(string type = "", TPayload payload = default) : StandardAction<TPayload, AssetContext, object>(type, payload);
}
