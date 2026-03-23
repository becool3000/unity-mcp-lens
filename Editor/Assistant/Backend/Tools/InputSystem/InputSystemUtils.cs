using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEngine;

#if UNITY_AI_INPUT_SYSTEM
using System.Threading.Tasks;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Editor;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;
#endif

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools.InputSystem
{
    static class InputSystemUtils
    {
        [Serializable]
        public struct ActionOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("path")]
            public string Path;
        }

#if UNITY_AI_INPUT_SYSTEM

        internal static async Task<bool> SaveChangesToAsset(ToolExecutionContext context, InputActionAsset actionAssetObj)
        {
            var path = AssetDatabase.GetAssetPath(actionAssetObj);
            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, path);

            if (actionAssetObj.m_ActionMaps == null)
                actionAssetObj.m_ActionMaps = new InputActionMap[0];

            var result = InputActionAssetManager.SaveAsset(path, actionAssetObj.ToJson());
            AssetDatabase.Refresh();
            return result;
        }

        static internal string ErrorMessage(string message, bool throwOnError)
        {
            if (throwOnError)
            {
                throw new Exception(message);
            }
            return message;
        }

        public struct GetInputActionAssetResult
        {
            public InputActionAsset InputActionAsset;
            public string Message;
        }

        internal static async Task<GetInputActionAssetResult> GetInputActionAsset(ToolExecutionContext context, string name, bool create, bool throwOnFailure)
        {
            var result = new GetInputActionAssetResult();

            if (string.IsNullOrWhiteSpace(name))
            {
                result.InputActionAsset = UnityEngine.InputSystem.InputSystem.actions;
            }
            else
            {
                string filter = $"t:InputActionAsset {name}";
                var guids = AssetDatabase.FindAssets(filter);
                const string k_AssetLocation = "Assets/AI_Asset";

                if (guids.Length == 0)
                {
                    if (!create)
                    {
                        result.Message = ErrorMessage($"Input actions asset: {name} does not exist!", throwOnFailure);

                        return result;
                    }
                    else
                    {
                        result.InputActionAsset = ScriptableObject.CreateInstance<InputActionAsset>();
                        result.InputActionAsset.name = name;
                        result.InputActionAsset.m_ActionMaps = new InputActionMap[0];

                        var path = $"{k_AssetLocation}/{name}.inputactions";
                        if (!AssetDatabase.IsValidFolder(k_AssetLocation))
                        {
                            AssetDatabase.CreateFolder("Assets", "AI_Asset");
                        }

                        await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, path);
                        InputActionAssetManager.SaveAsset(path, result.InputActionAsset.ToJson());
                        AssetDatabase.Refresh();
                        guids = AssetDatabase.FindAssets(filter);

                        if (guids.Length == 0)
                        {
                            result.Message = ErrorMessage($"Input actions asset: {name}.  Unable to create asset!", throwOnFailure);
                            return result;
                        }
                        path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        result.InputActionAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                        result.Message = $"Created input actions asset: {path}";
                        return result;
                    }
                }
                else
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    result.InputActionAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                }
            }
            name = GetActionAssetName(name);

            if (result.InputActionAsset == null)
            {
                result.Message = ErrorMessage($"Input actions asset: {name} does not exist!", throwOnFailure);
                return result;
            }

            result.Message = $"Loaded input actions asset: {name}.!";
            return result;
        }

        public struct GetControlSchemeResult
        {
            public InputControlScheme? ControlScheme;
            public string Message;
        }
        internal static async Task<GetControlSchemeResult> GetControlScheme(ToolExecutionContext context, InputActionAsset actionAssetObj, string controlScheme, bool create, bool throwOnFailure)
        {
            var result = new GetControlSchemeResult();

            if (actionAssetObj == null)
            {
                result.Message = ErrorMessage($"Get control scheme: {controlScheme}. Parent input action asset missing.", throwOnFailure);
                return result;
            }
            if (string.IsNullOrWhiteSpace(controlScheme))
            {
                result.Message = ErrorMessage($"Get control scheme failed - no control scheme name specified", throwOnFailure);
                return result;
            }
            result.ControlScheme = actionAssetObj.FindControlScheme(controlScheme);
            if (result.ControlScheme != null)
            {
                result.Message = $"Control scheme: {controlScheme} successfully loaded.";
                return result;
            }

            if (create)
            {
                var newControlScheme = new InputControlScheme(controlScheme);
                actionAssetObj.AddControlScheme(newControlScheme);
                bool updateResult = await SaveChangesToAsset(context, actionAssetObj);
                result.Message = updateResult? $"Control scheme: {controlScheme} successfully created.": ErrorMessage($"Control scheme: {controlScheme} could not be created on {actionAssetObj.name}.", throwOnFailure);
                return result;
            }
            result.Message = ErrorMessage($"Control scheme: {controlScheme} missing from {actionAssetObj.name}", throwOnFailure);
            return result;
        }

        public struct GetInputActionMapResult
        {
            public InputActionMap InputActionMap;
            public string Message;
        }

        internal static async Task<GetInputActionMapResult> GetInputActionMap(ToolExecutionContext context, InputActionAsset actionAssetObj, string actionMap, bool create, bool throwOnFailure)
        {
            var result = new GetInputActionMapResult();
            if (actionAssetObj == null)
            {
                result.Message = ErrorMessage($"Get input action map: {actionMap}. Parent input action asset missing.", throwOnFailure);
                return result;
            }
            if (string.IsNullOrWhiteSpace(actionMap))
            {
                result.Message = ErrorMessage($"Get input action map failed - no action map name specified", throwOnFailure);
                return result;
            }

            result.InputActionMap = actionAssetObj.FindActionMap(actionMap);
            if (result.InputActionMap == null)
            {
                if (!create)
                {
                    result.Message = ErrorMessage($"Input action map: {actionMap} missing from {actionAssetObj.name}!", throwOnFailure);
                    return result;
                }
                else
                {
                    result.InputActionMap = actionAssetObj.AddActionMap(actionMap);

                    await SaveChangesToAsset(context, actionAssetObj);

                    result.Message = $"Input action map {actionMap} created on {actionAssetObj.name}";
                    return result;
                }
            }

            result.Message = $"Input action map: {actionMap} successfully loaded.";
            return result;
        }

        public struct GetInputActionResult
        {
            public InputAction InputAction;
            public string Message;
        }

        internal static async Task<GetInputActionResult> GetInputAction(
            ToolExecutionContext context,
            InputActionAsset actionAssetObj,
            InputActionMap actionMapObj,
            string actionName,
            InputActionType? actionType,
            bool create,
            bool throwOnFailure)
        {
            var result = new GetInputActionResult();

            if (string.IsNullOrWhiteSpace(actionName))
            {
                result.Message = ErrorMessage($"Get input action - no action name specified", throwOnFailure);
                return result;
            }

            if (actionMapObj == null)
            {
                result.Message = ErrorMessage($"Get input action: {actionName}. Parent input action map missing.", throwOnFailure);
                return result;
            }

            result.InputAction = actionMapObj.FindAction(actionName);
            if (result.InputAction == null)
            {
                if (create)
                {
                    if (!actionType.HasValue)
                    {
                        result.Message = ErrorMessage($"No valid input action type specified for: {actionName}.", throwOnFailure);
                        return result;
                    }
                    result.InputAction = actionMapObj.AddAction(actionName, actionType.Value);
                    await SaveChangesToAsset(context, actionAssetObj);
                    result.Message = $"Create input action: {actionName} Succeeded";
                    return result;
                }

                result.Message = ErrorMessage($"Input action: {actionName} missing.", throwOnFailure);
                return result;
            }
            else
            {
                // if a value is not specified, bypass the type check for getting the input action.
                if (actionType.HasValue && result.InputAction.type != actionType.Value)
                {
                    result.Message = ErrorMessage($"Create input action: {actionName}. Type requested: {actionType.Value} conflicts with existing action's type {result.InputAction.type.ToString()}", throwOnFailure);
                    return result;
                }
                result.Message = $"Input action: {actionName} successfully loaded";
                return result;
            }
        }

        public struct GetInputBindingResult
        {
            public InputActionSetupExtensions.BindingSyntax? Binding;
            public string Message;
        }

        static internal async Task<GetInputBindingResult> GetInputBinding(
            ToolExecutionContext context,
            InputActionAsset actionAssetObj,
            InputActionMap actionMapObj,
            InputAction actionObj,
            string binding,
            bool create,
            bool throwOnFailure)
        {
            var result = new GetInputBindingResult();

            if (string.IsNullOrWhiteSpace(binding))
            {
                result.Message = ErrorMessage($"Get input binding - no binding specified", throwOnFailure);
                return result;
            }

            // If action is provided, get binding from there
            if (actionObj != null)
            {
                actionMapObj = actionObj.actionMap;
            }
            if (actionMapObj == null)
            {
                result.Message = ErrorMessage($"Create binding: {binding}. Missing parent actionMap", throwOnFailure);
                return result;
            }

            var bindingObj = new InputBinding(binding, actionObj?.name);
            var foundIndex = actionMapObj.FindBinding(bindingObj, out var _);

            if (foundIndex != -1)
            {
                result.Binding = new InputActionSetupExtensions.BindingSyntax(actionMapObj, foundIndex, actionObj);

                result.Message = $"Binding: {binding} found on {actionMapObj.name}!";
                return result;
            }

            if (create)
            {
                result.Binding = actionMapObj.AddBinding(bindingObj);
                if (!result.Binding.Value.valid)
                {
                    result.Message = ErrorMessage($"{binding} is not a valid binding!", throwOnFailure);
                    return result;
                }
                await SaveChangesToAsset(context, actionAssetObj);
                result.Message = $"Binding: {binding} created successfully on Action Map {actionMapObj.name}!";
                return result;
            }
            result.Message = ErrorMessage($"Could not find Binding: {binding} in Action Map: {actionMapObj.name}", throwOnFailure);
            return result;
        }

        public struct GetProcessorsResult
        {
            public string Processors;
            public string Message;
        }

        static internal async Task<GetProcessorsResult> GetProcessors(ToolExecutionContext context, InputActionAsset actionAssetObj, InputAction actionObj, InputActionSetupExtensions.BindingSyntax? bindingObj, string processor, bool create, bool throwOnFailure)
        {
            GetProcessorsResult result = new GetProcessorsResult();

            if (string.IsNullOrWhiteSpace(processor))
            {
                result.Message = ErrorMessage($"GetProcessors failed. No processor(s) specified", throwOnFailure);
                return result;
            }

            if (actionObj == null && bindingObj == null)
            {
                result.Message = ErrorMessage("GetProcessors failed. No action or binding provided", throwOnFailure);
                return result;
            }

            // Get the current interactions on the binding if it is supplied
            if (bindingObj != null)
            {
                var bindingSource = bindingObj.Value.binding;

                result.Processors = bindingSource.processors;
                if (AllEntriesPresent(processor, result.Processors))
                {
                    result.Message = $"Processor: {processor} already existing on binding.";
                    return result;
                }
                else
                {
                    // To do, get unique list
                    result.Processors = AddToDataString(processor, result.Processors);
                    bindingSource.processors = result.Processors;
                    await SaveChangesToAsset(context, actionAssetObj);
                    result.Message = $"Processor: {processor} added to binding.";
                    return result;
                }
            }
            else
            {
                result.Processors = actionObj.processors;
                if (AllEntriesPresent(processor, result.Processors))
                {
                    result.Message = $"Processor: {processor} already existing on action.";
                    return result;
                }
                else
                {
                    result.Processors = AddToDataString(processor, result.Processors);
                    actionObj.m_Processors = result.Processors;
                    await SaveChangesToAsset(context, actionAssetObj);
                    result.Message = $"Processor: {processor} added to action.";
                    return result;
                }
            }
        }

        public struct GetInteractionsResult
        {
            public string Interactions;
            public string Message;
        }

        static internal async Task<GetInteractionsResult> GetInteractions(ToolExecutionContext context, InputActionAsset actionAssetObj, InputAction actionObj, InputActionSetupExtensions.BindingSyntax? bindingObj, string interaction, bool create, bool throwOnFailure)
        {
            var result = new GetInteractionsResult();
            if (string.IsNullOrWhiteSpace(interaction))
            {
                result.Message = ErrorMessage($"GetInteractions failed. No interaction(s) specified", throwOnFailure);
                return result;
            }

            if (actionObj == null && bindingObj == null)
            {
                result.Message = ErrorMessage("GetInteractions failed. No action or binding provided", throwOnFailure);
                return result;
            }
            // Get the current interactions on the binding if it is supplied
            if (bindingObj != null)
            {
                var bindingSource = bindingObj.Value.binding;
                var currentInteractions = bindingSource.interactions;
                if (AllEntriesPresent(interaction, currentInteractions))
                {
                    result.Message = $"Interaction: {interaction} already existing on binding.";
                    result.Interactions = currentInteractions;
                    return result;
                }
                else
                {
                    currentInteractions = AddToDataString(interaction, currentInteractions);
                    bindingSource.interactions = currentInteractions;
                    await SaveChangesToAsset(context, actionAssetObj);
                    result.Message = $"Interaction: {interaction} added to binding.";
                    result.Interactions = currentInteractions;
                    return result;
                }
            }
            else
            {
                var currentInteractions = actionObj.interactions;
                if (AllEntriesPresent(interaction, currentInteractions))
                {
                    result.Message = $"Interaction: {interaction} already existing on action.";
                    result.Interactions = currentInteractions;
                    return result;
                }
                else
                {
                    currentInteractions = AddToDataString(interaction, currentInteractions);
                    actionObj.m_Interactions = currentInteractions;
                    await SaveChangesToAsset(context, actionAssetObj);
                    result.Message = $"Interaction: {interaction} added to action.";
                    result.Interactions = currentInteractions;
                    return result;
                }
            }
        }

        internal static async Task<string> RemoveInputActionAsset(ToolExecutionContext context, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                // While normally we just get the default actions asset if none is specified
                // That is too dangerous of an assumption for remove.
                throw new Exception("Remove Input Actions Asset failed - no asset specified.");
            }
            else
            {
                string filter = $"t:InputActionAsset {name}";
                var guids = AssetDatabase.FindAssets(filter);

                if (guids.Length == 0)
                {
                    return $"Input actions asset: {name} does not exist!";
                }
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Delete, path);

                if (AssetDatabase.DeleteAsset(path))
                {
                    return $"Input actions asset: {name} removed!";
                }
                else
                {
                    throw new Exception($"Input actions asset: {name}. could not be removed!");
                }
            }
        }

        static internal async Task<string> RemoveControlScheme(ToolExecutionContext context, InputActionAsset actionAssetObj, string controlScheme)
        {
            if (actionAssetObj == null)
            {
                throw new Exception($"Remove control scheme: {controlScheme}. Parent input action asset missing.");
            }
            if (string.IsNullOrWhiteSpace(controlScheme))
            {
                throw new Exception($"Remove control scheme failed - no control scheme name specified");
            }
            var controlSchemObj = actionAssetObj.FindControlScheme(controlScheme);
            if (controlSchemObj != null)
            {
                actionAssetObj.RemoveControlScheme(controlScheme);
                await SaveChangesToAsset(context, actionAssetObj);
                return $"Control scheme: {controlScheme} successfully removed.";
            }
            return $"Control scheme: {controlScheme} does not exist.";
        }

        static internal async Task<string> RemoveInputActionMap(ToolExecutionContext context, InputActionAsset actionAssetObj, string actionMap)
        {
            if (actionAssetObj == null)
            {
                throw new Exception($"Remove input action map: {actionMap}. Parent input action asset missing.");
            }
            if (string.IsNullOrWhiteSpace(actionMap))
            {
                throw new Exception($"Remove input action map failed - no action map name specified");
            }

            var actionMapObj = actionAssetObj.FindActionMap(actionMap);
            if (actionMapObj == null)
            {
                return $"Input action map: {actionMap} does not exist in {actionAssetObj.name}!";
            }
            actionAssetObj.RemoveActionMap(actionMapObj);
            await SaveChangesToAsset(context, actionAssetObj);

            return $"Input action map {actionMap} removed from {actionAssetObj.name}";
        }

        static internal async Task<string> RemoveInputAction(ToolExecutionContext context, InputActionAsset actionAssetObj, InputActionMap actionMapObj, string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                throw new Exception($"Remove input action failed - no action name specified");
            }

            if (actionMapObj == null)
            {
                throw new Exception($"Remove input action: {actionName}. Parent input action map missing.");
            }

            var existingAction = actionMapObj.FindAction(actionName);
            if (existingAction == null)
            {
                return $"Input action: {actionName} does not exist.";
            }
            else
            {
                existingAction.RemoveAction();
                await SaveChangesToAsset(context, actionAssetObj);
                return $"Input action: {actionName} successfully removed.";
            }
        }

        static internal async Task<string> RemoveInputBinding(ToolExecutionContext context, InputActionAsset actionAssetObj, InputActionMap actionMapObj, InputAction actionObj, string binding)
        {
            if (string.IsNullOrWhiteSpace(binding))
            {
                throw new Exception($"Remove input binding failed. No binding specified");
            }

            // If action is provided, get binding from there
            if (actionObj != null)
            {
                actionMapObj = actionObj.actionMap;
            }
            if (actionMapObj == null)
            {
                throw new Exception($"Remove binding: {binding}. Missing parent action or actionMap");
            }

            var bindingObj = new InputBinding(binding, actionObj?.name);
            var foundIndex = actionMapObj.FindBinding(bindingObj, out var _);
            if (foundIndex != -1)
            {
                var existingBinding = new InputActionSetupExtensions.BindingSyntax(actionMapObj, foundIndex, actionObj);
                existingBinding.Erase();
                await SaveChangesToAsset(context, actionAssetObj);
                return $"Binding: {binding} removed!";
            }
            return $"Binding: {binding} does not exist!";
        }

        internal static async Task<string> RemoveProcessors(ToolExecutionContext context, InputActionAsset actionAssetObj, InputAction actionObj, InputActionSetupExtensions.BindingSyntax? bindingObj, string processor)
        {
            if (string.IsNullOrWhiteSpace(processor))
            {
                throw new Exception($"RemoveProcessors failed. No processor(s) specified");
            }

            if (actionObj == null && bindingObj == null)
            {
                throw new Exception("RemoveProcessors failed. No action or binding provided");
            }
            // Get the current processors on the binding if it is supplied
            if (bindingObj != null)
            {
                var bindingSource = bindingObj.Value.binding;
                var currentProcessors = bindingSource.processors;

                if (AnyEntriesPresent(processor, currentProcessors))
                {
                    bindingSource.processors = RemoveFromDataString(processor, currentProcessors);
                    await SaveChangesToAsset(context, actionAssetObj);
                    return $"Processor: {processor} removed from binding.";
                }
                else
                {
                    return $"Processor: {processor} does not exist on binding.";
                }
            }
            else
            {
                var currentProcessors = actionObj.processors;
                if (AnyEntriesPresent(processor, currentProcessors))
                {
                    actionObj.m_Processors = RemoveFromDataString(processor, currentProcessors);
                    await SaveChangesToAsset(context, actionAssetObj);
                    return $"Processor: {processor} removed from action.";
                }
                else
                {
                    return $"Processor: {processor} does not exist on action.";
                }
            }
        }

        internal static async Task<string> RemoveInteractions(ToolExecutionContext context, InputActionAsset actionAssetObj, InputAction actionObj, InputActionSetupExtensions.BindingSyntax? bindingObj, string interaction)
        {
            if (string.IsNullOrWhiteSpace(interaction))
            {
                throw new Exception($"RemoveInteractions failed. No interaction(s) specified");
            }

            if (actionObj == null && bindingObj == null)
            {
                throw new Exception("RemoveInteractions failed. No action or binding provided");
            }

            // Get the current interactions on the binding if it is supplied
            if (bindingObj != null)
            {
                var bindingSource = bindingObj.Value.binding;
                var currentInteractions = bindingSource.interactions;
                if (AnyEntriesPresent(interaction, currentInteractions))
                {
                    bindingSource.interactions = RemoveFromDataString(interaction, currentInteractions);
                    await SaveChangesToAsset(context, actionAssetObj);
                    return $"Interaction: {interaction} removed from binding.";
                }
                else
                {
                    return $"Interaction: {interaction} does not exist on binding.";
                }
            }
            else
            {
                var currentInteractions = actionObj.interactions;
                if (AnyEntriesPresent(interaction, currentInteractions))
                {
                    actionObj.m_Interactions = RemoveFromDataString(interaction, currentInteractions);
                    await SaveChangesToAsset(context, actionAssetObj);
                    return $"Interaction: {interaction} removed from action.";
                }
                else
                {
                    return $"Interaction: {interaction} does not exist on action.";
                }
            }
        }

        internal static async Task<string> RenameInputActionAsset(ToolExecutionContext context, string name, string newName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new Exception("Rename Input Actions Asset failed - no asset specified.");
            }
            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new Exception("Rename Input Actions Asset failed - no new name specified.");
            }

            string filter = $"t:InputActionAsset {name}";
            var guids = AssetDatabase.FindAssets(filter);

            if (guids.Length == 0)
            {
                throw new Exception($"Input actions asset: {name} does not exist!");
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            AssetDatabase.Refresh();
            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, path);

            var renameMessage = AssetDatabase.RenameAsset(path, newName);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrWhiteSpace(renameMessage))
            {
                throw new Exception(renameMessage);
            }
            return $"Input actions asset: {name} renamed to {newName}.";
        }

        internal static async Task<string> RenameControlScheme(ToolExecutionContext context, InputActionAsset actionAssetObj, string controlScheme, string newName)
        {
            if (actionAssetObj == null)
            {
                throw new Exception($"Rename control scheme: {controlScheme}. Parent input action asset missing.");
            }
            if (string.IsNullOrWhiteSpace(controlScheme) || string.IsNullOrWhiteSpace(newName))
            {
                throw new Exception($"Rename control scheme failed - no control scheme name or new name specified");
            }
            var controlSchemeIndex = actionAssetObj.FindControlSchemeIndex(controlScheme);
            if (controlSchemeIndex > -1)
            {
                actionAssetObj.m_ControlSchemes[controlSchemeIndex].m_Name = newName;
                await SaveChangesToAsset(context, actionAssetObj);
                return $"Control scheme: {controlScheme} successfully renamed to {newName}.";
            }
            throw new Exception($"Control scheme: {controlScheme} does not exist.");
        }

        internal static async Task<string> RenameInputActionMap(ToolExecutionContext context, InputActionAsset actionAssetObj, string actionMap, string newName)
        {
            if (actionAssetObj == null)
            {
                throw new Exception($"Rename input action map: {actionMap}. Parent input action asset missing.");
            }
            if (string.IsNullOrWhiteSpace(actionMap) || string.IsNullOrWhiteSpace(newName))
            {
                throw new Exception($"Remove input action map failed - no action map name or new name specified");
            }

            var actionMapObj = actionAssetObj.FindActionMap(actionMap);
            if (actionMapObj == null)
            {
                throw new Exception($"Input action map: {actionMap} does not exist in {actionAssetObj.name}!");
            }
            actionMapObj.m_Name = newName;
            await SaveChangesToAsset(context, actionAssetObj);

            return $"Input action map {actionMap} renamed to {newName}";
        }

        internal static async Task<string> RenameInputAction(ToolExecutionContext context, InputActionAsset actionAssetObj, InputActionMap actionMapObj, string actionName, string newName)
        {
            if (string.IsNullOrWhiteSpace(actionName) || string.IsNullOrWhiteSpace(newName))
            {
                throw new Exception($"Rename input action failed - no action name or new name specified");
            }

            if (actionMapObj == null)
            {
                throw new Exception($"Rename input action: {actionName}. Parent input action map missing.");
            }

            var existingAction = actionMapObj.FindAction(actionName);
            if (existingAction == null)
            {
                throw new Exception($"Input action: {actionName} does not exist.");
            }
            else
            {
                existingAction.m_Name = newName;
                await SaveChangesToAsset(context, actionAssetObj);
                return $"Input action: {actionName} successfully renamed to {newName}.";
            }
        }

        static internal string GetActionAssetName(string name)
        {
            return string.IsNullOrEmpty(name) ? "Default Action Asset" : name;
        }

        [ToolPermissionIgnore]
        static internal List<ParameterInfo> GetParametersForType(Type registeredType)
        {
            var parameters = new List<ParameterInfo>();
            if (registeredType == null)
            {
                // No registered type. This usually happens when data references a registration that has
                // been removed in the meantime (e.g. an interaction that is no longer supported). We want
                // to accept this case and simply pretend that the given type has no parameters.
                return parameters;
            }

            // Try to instantiate object so that we can determine defaults.
            object instance = null;
            try
            {
                instance = Activator.CreateInstance(registeredType);
            }
            catch (Exception)
            {
                // Swallow. If we can't create an instance, we simply assume no defaults.
            }

            // Go through public instance fields and add every parameter found on the registered
            // type.
            var fields = registeredType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                // Skip all fields that have an [InputControl] attribute. This is relevant
                // only for composites, but we just always do it here.
                if (field.GetCustomAttribute<InputControlAttribute>(false) != null)
                    continue;

                // Determine parameter name from field.
                ParameterInfo parameter = null;

                // Determine parameter type from field.
                var fieldType = field.FieldType;

                if (fieldType.IsEnum)
                {
                    var enumParam = new EnumInfo();
                    enumParam.Name = field.Name;
                    enumParam.DataType = $"{TypeHelpers.GetNiceTypeName(fieldType)}(enum)";
                    enumParam.Options = Enum.GetNames(fieldType).ToList();
                    parameter = enumParam;
                }
                else
                {
                    parameter = new ParameterInfo();
                    parameter.Name = field.Name;
                    parameter.DataType = TypeHelpers.GetNiceTypeName(fieldType);
                }

                // Fill in default values, if they exist
                if (instance != null)
                {
                    try
                    {
                        var value = field.GetValue(instance);
                        parameter.DefaultValue = value.ToString();
                    }
                    catch
                    {
                        // If the getter throws, ignore. All we lose is the actual default value from
                        // the field.
                    }
                }
                parameters.Add(parameter);
            }

            return parameters;
        }

        static List<string> DataStringToList(string dataString)
        {
            return dataString.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(str => str.Trim()).ToList();
        }

        static string AddToDataString(string entry, string dataString)
        {
            if (string.IsNullOrWhiteSpace(dataString))
            {
                return entry;
            }
            if (string.IsNullOrWhiteSpace(entry))
            {
                return dataString;
            }
            var entries = DataStringToList(entry);
            var destList = DataStringToList(dataString);

            return string.Join(';', entries.Union(destList));
        }

        static string RemoveFromDataString(string entry, string dataString)
        {
            if (string.IsNullOrWhiteSpace(dataString))
            {
                return "";
            }
            if (string.IsNullOrWhiteSpace(entry))
            {
                return dataString;
            }
            var entries = DataStringToList(entry);
            var destList = DataStringToList(dataString);

            return string.Join(';', destList.Except(entries));
        }

        static bool AnyEntriesPresent(string entry, string dataString)
        {
            if (string.IsNullOrWhiteSpace(entry) || string.IsNullOrWhiteSpace(dataString))
            {
                return false;
            }
            var entries = DataStringToList(entry);
            var destSet = DataStringToList(dataString).ToHashSet();
            return entries.Any(x => destSet.Contains(x));
        }

        static bool AllEntriesPresent(string entry, string dataString)
        {
            if (string.IsNullOrWhiteSpace(entry) || string.IsNullOrWhiteSpace(dataString))
            {
                return false;
            }
            var entries = DataStringToList(entry);
            var destSet = DataStringToList(dataString).ToHashSet();
            return entries.All(x => destSet.Contains(x));
        }
#endif

        [Serializable]
        public class ParameterInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("dataType")]
            public string DataType { get; set; }
            [JsonProperty("defaultValue")]
            public string DefaultValue { get; set; }
        }

        [Serializable]
        public class EnumInfo : ParameterInfo
        {
            [JsonProperty("options")]
            public List<string> Options { get; set; } = new List<string>();
        }
    }
}
