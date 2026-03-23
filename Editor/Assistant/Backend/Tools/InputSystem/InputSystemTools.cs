#if UNITY_AI_INPUT_SYSTEM
using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine.InputSystem;
using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEditor;
using System.Linq;


namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools.InputSystem
{
    static class InputSystemTools
    {
        const string k_FunctionNameModify = "Unity.InputSystem.Modify";
        const string k_FunctionNameReadStatus = "Unity.InputSystem.ReadStatus";

        public enum Operation
        {
            Add,
            Remove,
            Rename,
        }

        public enum Target
        {
            ActionAsset = 0,
            ControlScheme = 1,
            ActionMap = 2,
            Action = 3,
            Binding = 4,
            Interaction = 5,
            Processor = 6
        }

        [AgentTool(
            @"Adds, removes, or renames input system assets or their data. Retrieve proper syntax and data first.
            For different operation targets, invoke this tool separately for each step.
            For example, if a task needs to add an input action and then create binding for the action, first invoke this tool to add the action, then invoke again to add the binding to the action.
            Different parameters are required based on the value of target:
            'ActionAsset' - inputActionAsset
            'ControlScheme' - controlScheme
            'ActionMap' - actionMap
            'Action' - actionMap, action
            'Binding' - actionMap, action, binding
            'Interaction' - actionMap, (action OR binding)
            'Processor' -  actionMap, (action OR binding)",
            k_FunctionNameModify,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            assistantMode: AssistantMode.Agent
            )]
        public static async Task<InputSystemUtils.ActionOutput>  ModifyInputSystem(
            ToolExecutionContext context,
        [Parameter("The operation to perform: Add, Remove, or Rename")]
        Operation operation,
        [Parameter("The asset or piece of data to perform the operation on: ActionAsset, ControlScheme, ActionMap, Action, Binding, Interaction, or Processor.")]
        Target target,
        [Parameter("The name of the input action asset. Can be blank to use the system default asset.")]
        string inputActionAsset,
        [Parameter("Control schemes that can be part of an inputActionAsset.")]
        string controlScheme,
        [Parameter("The name of the action map in the input action asset.")]
        string actionMap,
        [Parameter("The name of the action in the specified action map.")]
        string action,  // Name
        [Parameter("The type of the action (Button, PassThrough, or Value). Required only when adding a new action.")]
        InputActionType? actionType,
        [Parameter("The input binding path to use.")]
        string binding,
        [Parameter("Interactions applied to a binding or action.")]
        string interaction,
        [Parameter("Processors applied to a binding or action.")]
        string processor,
        [Parameter("When renaming, the new name to use.")]
        string newName = null)
        {
            var output = new InputSystemUtils.ActionOutput();

            InputActionAsset actionAssetObj = null;
            InputControlScheme? controlSchemeObj = null;
            InputActionMap actionMapObj = null;
            InputAction actionObj = null;
            InputActionSetupExtensions.BindingSyntax? bindingObj = null;

            string interactionsVal = null;
            string processorsVal = null;

            // Based on the target preload any possible prereq assets
            if (target > Target.ActionAsset)
            {
                InputSystemUtils.GetInputActionAssetResult getInputActionAssetResult = await InputSystemUtils.GetInputActionAsset(context, inputActionAsset, false, false);
                actionAssetObj = getInputActionAssetResult.InputActionAsset;
            }

            if (target > Target.ActionMap)
            {
                InputSystemUtils.GetInputActionMapResult getInputActionMapResult = await InputSystemUtils.GetInputActionMap(context, actionAssetObj, actionMap, false, false);
                actionMapObj = getInputActionMapResult.InputActionMap;
            }

            if (target > Target.Action)
            {
                InputSystemUtils.GetInputActionResult result = await InputSystemUtils.GetInputAction(context, actionAssetObj, actionMapObj, action, actionType, false, false);
                actionObj = result.InputAction;
            }

            if (target > Target.Binding)
            {
                InputSystemUtils.GetInputBindingResult getInputBindingResult = await InputSystemUtils.GetInputBinding(context, actionAssetObj, actionMapObj, actionObj, binding, false, false);
                bindingObj = getInputBindingResult.Binding;
            }

            switch (operation)
            {
                case Operation.Add:
                {
                    var message = "";
                    switch (target)
                    {
                        case Target.ActionAsset:
                            InputSystemUtils.GetInputActionAssetResult getInputActionAssetResult = await InputSystemUtils.GetInputActionAsset(context, inputActionAsset, true, true);
                            actionAssetObj = getInputActionAssetResult.InputActionAsset;
                            message = getInputActionAssetResult.Message;
                            break;
                        case Target.ControlScheme:
                            InputSystemUtils.GetControlSchemeResult getControlScheme = await InputSystemUtils.GetControlScheme(context, actionAssetObj, controlScheme, true, true);
                            controlSchemeObj = getControlScheme.ControlScheme;
                            message = getControlScheme.Message;
                            break;
                        case Target.ActionMap:
                            InputSystemUtils.GetInputActionMapResult getInputActionMapResult = await InputSystemUtils.GetInputActionMap(context, actionAssetObj, actionMap, true, true);
                            actionMapObj = getInputActionMapResult.InputActionMap;
                            message = getInputActionMapResult.Message;
                            break;
                        case Target.Action:
                            InputSystemUtils.GetInputActionResult getInputActionResult = await InputSystemUtils.GetInputAction(context, actionAssetObj, actionMapObj, action, actionType, true, true);
                            actionObj = getInputActionResult.InputAction;
                            message = getInputActionResult.Message;
                            break;
                        case Target.Binding:
                            InputSystemUtils.GetInputBindingResult getInputBidingResult = await InputSystemUtils.GetInputBinding(context, actionAssetObj, actionMapObj, actionObj, binding, true, true);
                            bindingObj = getInputBidingResult.Binding;
                            message = getInputBidingResult.Message;
                            break;
                        case Target.Interaction:
                            InputSystemUtils.GetInteractionsResult getInteractionsResult = await InputSystemUtils.GetInteractions(context, actionAssetObj, actionObj, bindingObj, interaction, true, true);
                            interactionsVal = getInteractionsResult.Interactions;
                            message = getInteractionsResult.Message;
                            break;
                        case Target.Processor:
                            InputSystemUtils.GetProcessorsResult getProcessorsResult = await InputSystemUtils.GetProcessors(context, actionAssetObj, actionObj, bindingObj, processor, true, true);
                            processorsVal = getProcessorsResult.Processors;
                            message = getProcessorsResult.Message;
                            break;
                    }
                    output.Message = message;
                }
                break;
                case Operation.Remove:
                {
                    var message = "";
                    switch (target)
                    {
                        case Target.ActionAsset:
                            message = await InputSystemUtils.RemoveInputActionAsset(context, inputActionAsset);
                            break;
                        case Target.ControlScheme:
                            message = await InputSystemUtils.RemoveControlScheme(context, actionAssetObj, controlScheme);
                            break;
                        case Target.ActionMap:
                            message = await InputSystemUtils.RemoveInputActionMap(context, actionAssetObj, actionMap);
                            break;
                        case Target.Action:
                            message = await InputSystemUtils.RemoveInputAction(context, actionAssetObj, actionMapObj, action);
                            break;
                        case Target.Binding:
                            message = await InputSystemUtils.RemoveInputBinding(context, actionAssetObj, actionMapObj, actionObj, binding);
                            break;
                        case Target.Interaction:
                            message = await InputSystemUtils.RemoveInteractions(context, actionAssetObj, actionObj, bindingObj, interaction);
                            break;
                        case Target.Processor:
                            message = await InputSystemUtils.RemoveProcessors(context, actionAssetObj, actionObj, bindingObj, processor);
                            break;
                    }
                    output.Message = message;
                }
                break;
                case Operation.Rename:
                {
                    string message = "";
                    switch (target)
                    {
                        case Target.ActionAsset:
                            message= await InputSystemUtils.RenameInputActionAsset(context,inputActionAsset, newName);
                            break;
                        case Target.ControlScheme:
                            message = await InputSystemUtils.RenameControlScheme(context, actionAssetObj, controlScheme, newName);
                            break;
                        case Target.ActionMap:
                            message = await InputSystemUtils.RenameInputActionMap(context, actionAssetObj, actionMap, newName);
                            break;
                        case Target.Action:
                            message = await InputSystemUtils.RenameInputAction(context, actionAssetObj, actionMapObj, action, newName);
                            break;
                        case Target.Binding:
                        case Target.Interaction:
                        case Target.Processor:
                            message = $"Rename not supported for {target}";
                            break;
                    }
                    output.Message = message;

                }
                break;
            }
            return output;
        }


        [Serializable]
        public class ActionInfo
        {
            [JsonProperty("name")]
            public string Name;

            [JsonProperty("bindings")]
            public List<string> Bindings = new();
            [JsonProperty("interactions")]
            public string Interactions;
            [JsonProperty("processors")]
            public string Processors;
        }

        [Serializable]
        public class ActionMapInfo
        {
            [JsonProperty("name")]
            public string Name;

            [JsonProperty("actions")]
            public List<ActionInfo> Actions = new();
        }
        [Serializable]
        public class ActionAssetInfo
        {
            [JsonProperty("name")]
            public string Name;
            [JsonProperty("controlSchemes")]
            public List<string> ControlSchemes = new();
        }

        [Serializable]
        public class StatusOutput
        {
            [JsonProperty("allPossibleBindingsByDevice")]
            public List<GetPossibleBindingsUtils.DeviceEntry> AllPossibleBindingsByDevice = new();

            [JsonProperty("allPossibleProcessors")]
            public List<GetPossibleProcessorsUtils.ProcessorEntry> AllPossibleProcessors = new();

            [JsonProperty("allPossibleInteractions")]
            public List<GetPossibleInteractionsUtils.InteractionEntry> AllPossibleInteractions = new();

            [JsonProperty("inputActionAssets")]
            public List<ActionAssetInfo> InputActionAssets = new();

            [JsonProperty("systemWideDefaultInputActionAsset")]
            public string DefaultInputActionAsset = "";

            [JsonProperty("InputSystemVersionInformation")]
            public string InputSystemVersion;
        }

        [AgentTool(
            "Lists the current state of the input system along with valid syntax. Call before doing any input system function",
            k_FunctionNameReadStatus,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            assistantMode: AssistantMode.Ask
            )]
        public static StatusOutput ReadInputSystemStatus()
        {
            var output = new StatusOutput();

            var bindings = GetPossibleBindingsUtils.BindingsByDevice();
            var processors = GetPossibleProcessorsUtils.GetPossibleProcessors();
            var interactions = GetPossibleInteractionsUtils.GetPossibleInteractions();

            output.AllPossibleBindingsByDevice = bindings;
            output.AllPossibleProcessors = processors;
            output.AllPossibleInteractions = interactions;

            // We need to know what version of the input system we are using
            // If it's installed locally, we will include it's inputactions in the list of assets
            // so that if inputsystem developers want to use UAI,they are not locked out.
            // Otherwise we had that asset so it doesn't confuse the user and the LLM
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.inputsystem");
            output.InputSystemVersion = $"({packageInfo.name})@{packageInfo.version}";
            var includeInputSystemAsset = packageInfo.source is UnityEditor.PackageManager.PackageSource.Embedded or UnityEditor.PackageManager.PackageSource.Local;

            // Get all input action assets
            var actionList = new List<ActionAssetInfo>();
            var guids = AssetDatabase.FindAssets("t:InputActionAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Ignore assets within the input system
                if (!includeInputSystemAsset && path.StartsWith("Packages/com.unity.inputsystem"))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                if (asset != null)
                {
                    var actionAssetInfo = new ActionAssetInfo();
                    actionAssetInfo.Name = asset.name;

                    var controlSchemes = asset.controlSchemes;
                    foreach (var controlScheme in controlSchemes)
                    {
                        actionAssetInfo.ControlSchemes.Add(controlScheme.name);
                    }

                    foreach (var currentMap in asset.actionMaps)
                    {
                        var mapInfo = new ActionMapInfo();
                        mapInfo.Name = currentMap.name;
                        // Fill in actions
                        foreach (var currentAction in currentMap.actions)
                        {
                            var actionInfo = new ActionInfo();
                            actionInfo.Name = currentAction.name;
                            actionInfo.Interactions = currentAction.interactions;
                            actionInfo.Processors = currentAction.processors;
                            actionInfo.Bindings = currentAction.bindings.Select(binding => $"{binding.name} : {binding.path}").ToList();
                            mapInfo.Actions.Add(actionInfo);
                        }
                        // Fill in 'global' actions
                        var globalBindings = currentMap.bindings.Where(binding => binding.action == null);
                        foreach (var currentBinding in globalBindings)
                        {
                            var actionInfo = new ActionInfo();
                            actionInfo.Name = null;
                            actionInfo.Interactions = currentBinding.interactions;
                            actionInfo.Processors = currentBinding.processors;
                            actionInfo.Bindings.Add(currentBinding.path);
                            mapInfo.Actions.Add(actionInfo);
                        }
                    }
                    actionList.Add(actionAssetInfo);
                }
            }
            output.InputActionAssets = actionList;

            var defaultAsset = UnityEngine.InputSystem.InputSystem.actions;

            output.DefaultInputActionAsset = (defaultAsset != null) ? defaultAsset.name : "None specified";
            return output;
        }
    }
}
#endif
