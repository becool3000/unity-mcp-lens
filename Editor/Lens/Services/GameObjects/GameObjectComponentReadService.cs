#nullable disable
using System.Collections.Generic;
using Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects;
using Becool.UnityMcpLens.Editor.Models.GameObjects;

namespace Becool.UnityMcpLens.Editor.Services.GameObjects
{
    sealed class GameObjectComponentReadService
    {
        readonly UnityGameObjectAdapter m_Adapter;

        public GameObjectComponentReadService(UnityGameObjectAdapter adapter)
        {
            m_Adapter = adapter;
        }

        public GameObjectOperationResult ListComponents(GameObjectComponentListRequest request, GameObjectToolTiming timing)
        {
            var targetResult = ResolveTarget(request?.target, request?.searchMethod, request?.searchInactive ?? false, "ListComponents", timing);
            if (!targetResult.success)
                return targetResult;

            List<GameObjectComponentInfo> components;
            using (timing.Measure("adapter"))
            {
                components = m_Adapter.ListComponents((UnityGameObjectHandle)targetResult.data);
            }

            var target = (UnityGameObjectHandle)targetResult.data;
            return GameObjectOperationResult.Ok(
                $"Retrieved {components.Count} components from '{target.Name}'.",
                new GameObjectComponentListResult
                {
                    target = m_Adapter.ToTargetSummary(target),
                    count = components.Count,
                    components = components
                });
        }

        public GameObjectOperationResult GetComponent(GameObjectComponentGetRequest request, GameObjectToolTiming timing)
        {
            var targetResult = ResolveTarget(request?.target, request?.searchMethod, request?.searchInactive ?? false, "GetComponent", timing);
            if (!targetResult.success)
                return targetResult;

            var validation = ValidateComponentRequest(request);
            if (!validation.success)
                return validation;

            UnityComponentHandle component;
            using (timing.Measure("adapter"))
            {
                component = m_Adapter.FindComponent((UnityGameObjectHandle)targetResult.data, request.componentName, request.componentIndex);
            }

            var target = (UnityGameObjectHandle)targetResult.data;
            if (component == null)
            {
                return Error(
                    $"Component '{request.componentName}' not found on GameObject '{target.Name}'.",
                    "component_not_found");
            }

            object componentData;
            using (timing.Measure("adapter"))
            {
                componentData = m_Adapter.ReadComponentData(component, request.includeNonPublicSerialized);
            }

            if (componentData == null)
            {
                return Error(
                    $"Failed to serialize component '{request.componentName}' on GameObject '{target.Name}'.",
                    "component_serialization_failed");
            }

            return GameObjectOperationResult.Ok(
                $"Retrieved component '{request.componentName}' from '{target.Name}'.",
                new GameObjectComponentGetResult
                {
                    target = m_Adapter.ToTargetSummary(target),
                    component = component.Info,
                    data = componentData
                });
        }

        public GameObjectOperationResult GetComponentsLegacy(GameObjectComponentGetRequest request, GameObjectToolTiming timing)
        {
            var targetResult = ResolveTarget(request?.target, request?.searchMethod, request?.searchInactive ?? false, "get_components", timing);
            if (!targetResult.success)
                return targetResult;

            List<object> componentData;
            using (timing.Measure("adapter"))
            {
                componentData = m_Adapter.GetSerializedComponentDataList((UnityGameObjectHandle)targetResult.data, request.includeNonPublicSerialized);
            }

            var target = (UnityGameObjectHandle)targetResult.data;
            return GameObjectOperationResult.Ok(
                $"Retrieved {componentData.Count} components from '{target.Name}'.",
                componentData);
        }

        public GameObjectOperationResult GetComponentLegacy(GameObjectComponentGetRequest request, GameObjectToolTiming timing)
        {
            var targetResult = ResolveTarget(request?.target, request?.searchMethod, request?.searchInactive ?? false, "get_component", timing);
            if (!targetResult.success)
                return targetResult;

            var validation = ValidateComponentRequest(request);
            if (!validation.success)
                return validation;

            UnityComponentHandle component;
            using (timing.Measure("adapter"))
            {
                component = m_Adapter.FindComponent((UnityGameObjectHandle)targetResult.data, request.componentName, request.componentIndex);
            }

            var target = (UnityGameObjectHandle)targetResult.data;
            if (component == null)
            {
                return Error(
                    $"Component '{request.componentName}' not found on GameObject '{target.Name}'.",
                    "component_not_found");
            }

            object componentData;
            using (timing.Measure("adapter"))
            {
                componentData = m_Adapter.ReadComponentData(component, request.includeNonPublicSerialized);
            }

            if (componentData == null)
            {
                return Error(
                    $"Failed to serialize component '{request.componentName}' on GameObject '{target.Name}'.",
                    "component_serialization_failed");
            }

            return GameObjectOperationResult.Ok(
                $"Retrieved component '{request.componentName}' from '{target.Name}'.",
                componentData);
        }

        GameObjectOperationResult ResolveTarget(GameObjectTargetRef target, string searchMethod, bool searchInactive, string action, GameObjectToolTiming timing)
        {
            if (target == null || target.isNull || target.isEmptyString)
                return Error("'target' parameter required for component read.", "missing_target");

            UnityGameObjectHandle handle;
            using (timing.Measure("adapter"))
            {
                handle = m_Adapter.FindObject(target, searchMethod, searchInactive);
            }

            if (handle == null)
            {
                return Error(
                    $"Target GameObject ('{target.text}') not found using method '{searchMethod ?? "default"}'.",
                    "target_not_found");
            }

            return GameObjectOperationResult.Ok(action, handle);
        }

        static GameObjectOperationResult ValidateComponentRequest(GameObjectComponentGetRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.componentName))
                return Error("'componentName' parameter required for GetComponent.", "missing_component_name");

            if (request.componentIndex.HasValue && request.componentIndex.Value < 0)
                return Error("componentIndex must be greater than or equal to 0.", "invalid_component_index");

            return GameObjectOperationResult.Ok("validated");
        }

        static GameObjectOperationResult Error(string message, string errorKind)
        {
            return GameObjectOperationResult.Error(message, errorKind, new
            {
                errorKind,
                code = errorKind
            });
        }
    }
}
