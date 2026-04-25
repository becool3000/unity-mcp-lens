#nullable disable
using System.Collections.Generic;
using System.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects;
using Becool.UnityMcpLens.Editor.Models.GameObjects;

namespace Becool.UnityMcpLens.Editor.Services.GameObjects
{
    sealed class GameObjectQueryService
    {
        readonly UnityGameObjectAdapter m_Adapter;

        public GameObjectQueryService(UnityGameObjectAdapter adapter)
        {
            m_Adapter = adapter;
        }

        public GameObjectOperationResult Find(GameObjectQueryRequest request, GameObjectToolTiming timing)
        {
            List<UnityGameObjectHandle> foundObjects;
            using (timing.Measure("adapter"))
            {
                foundObjects = m_Adapter.FindObjects(request);
            }

            if (foundObjects.Count == 0)
                return GameObjectOperationResult.Ok("No matching GameObjects found.", new List<GameObjectInfo>());

            List<GameObjectInfo> results;
            using (timing.Measure("adapter"))
            {
                results = foundObjects.Select(m_Adapter.ToGameObjectInfo).ToList();
            }

            return GameObjectOperationResult.Ok($"Found {results.Count} GameObject(s).", results);
        }

        public GameObjectOperationResult InspectFind(GameObjectQueryRequest request, GameObjectToolTiming timing)
        {
            List<UnityGameObjectHandle> foundObjects;
            using (timing.Measure("adapter"))
            {
                foundObjects = m_Adapter.FindObjects(request);
            }

            List<GameObjectInfo> results;
            using (timing.Measure("adapter"))
            {
                results = foundObjects.Select(m_Adapter.ToGameObjectInfo).ToList();
            }

            return GameObjectOperationResult.Ok(
                foundObjects.Count == 0 ? "No matching GameObjects found." : $"Found {results.Count} GameObject(s).",
                new GameObjectInspectResult
                {
                    count = results.Count,
                    objects = results
                });
        }

        public GameObjectOperationResult GetSelection(GameObjectToolTiming timing)
        {
            GameObjectSelectionResult result;
            using (timing.Measure("adapter"))
            {
                result = m_Adapter.GetSelection();
            }

            return GameObjectOperationResult.Ok("Retrieved current Unity selection.", result);
        }

        public GameObjectOperationResult GetBounds(GameObjectBoundsRequest request, GameObjectToolTiming timing)
        {
            if (request?.target == null || request.target.isNull)
                return GameObjectOperationResult.Error("'target' parameter required for get_bounds.", "missing_target");

            UnityGameObjectHandle target;
            using (timing.Measure("adapter"))
            {
                target = m_Adapter.FindObject(request.target, request.searchMethod);
            }

            if (target == null)
            {
                return GameObjectOperationResult.Error(
                    $"Target GameObject ('{request.target.text}') not found using method '{request.searchMethod ?? "default"}'.",
                    "target_not_found");
            }

            GameObjectBoundsInfo bounds;
            using (timing.Measure("adapter"))
            {
                bounds = m_Adapter.GetBounds(target);
            }

            return GameObjectOperationResult.Ok("Retrieved GameObject bounds.", bounds);
        }
    }
}
