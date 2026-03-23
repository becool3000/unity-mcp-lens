using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.AI.Assistant.Editor.FunctionCalling;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine.Pool;
using OpCodes = Mono.Cecil.Cil.OpCodes;


namespace Unity.AI.Assistant.Editor
{
    [InitializeOnLoad]
    internal static class ToolPermissionsValidator
    {
#if ASSISTANT_INTERNAL
        [MenuItem("AI Assistant/Internals/Tools/Check Permissions")]
        static void CheckToolPermissionsMenuItem()
        {
            ValidateAllAgentTools();
        }
#endif

        static ToolPermissionsValidator()
        {
            ValidateAllAgentTools();
        }

        static void ValidateAllAgentTools()
        {
            var toolMethods = TypeCache.GetMethodsWithAttribute<AgentToolAttribute>();
            foreach (var method in toolMethods)
            {
                if (method.GetCustomAttribute<AgentToolAttribute>() == null)
                    continue;

                try
                {
                    ValidateMethod(method);
                }
                catch (Exception ex)
                {
                    Debug.LogError(method.TryGetLocation(out var path, out var line)
                        ? $"[Tool Permissions] ({path.Replace("\\", "/")}:{line}) Tool {method.DeclaringType.FullName}.{method.Name} failed validation: {ex.Message}"
                        : $"[Tool Permissions] Tool {method.DeclaringType.FullName}.{method.Name} failed validation: {ex.Message}"
                    );
                }
            }
        }

        public static void ValidateMethod(MethodInfo method, int maxDepth = 10)
        {
            if (method == null)
                return;

            var modulePath = method.Module.FullyQualifiedName;
            using var module = ModuleDefinition.ReadModule(modulePath);

            var typeDef = FindTypeDefinition(module, method.DeclaringType);
            if (typeDef == null)
                throw new Exception("Type not found in module");

            var methodDef = typeDef.Methods.FirstOrDefault(m => m.Name == method.Name);
            if (methodDef == null)
                throw new Exception("Method not found in type");

            using var pooledList = ListPool<MethodDefinition>.Get(out var parentCallChain);

            // Detect async wrapper and inspect generated MoveNext() body instead
            var stateMachineAttr = method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>();
            if (stateMachineAttr != null)
            {
                var smType = FindTypeDefinition(module, stateMachineAttr.StateMachineType);
                var moveNext = smType?.Methods.FirstOrDefault(m => m.Name == "MoveNext");

                if (moveNext != null)
                {
                    ValidateMethodRecursive(moveNext, new HashSet<MethodDefinition>(), 0, maxDepth, parentCallChain);
                    return;
                }
            }

            // Fallback for non-async methods
            ValidateMethodRecursive(methodDef, new HashSet<MethodDefinition>(), 0, maxDepth, parentCallChain);
        }


        static void ValidateMethodRecursive(
            MethodDefinition methodDef,
            HashSet<MethodDefinition> visited,
            int depth,
            int maxDepth,
            List<MethodDefinition> parentCallChain = null)
        {
            // stop recursion if beyond limit
            if (depth > maxDepth)
                return;

            // Skip methods marked with [ToolPermissionIgnore]
            if (HasIgnoreAttribute(methodDef))
                return;

            // already scanned this method
            if (!visited.Add(methodDef))
                return;

            // Initialize parent call chain if not provided
            if (parentCallChain == null)
                parentCallChain = new List<MethodDefinition>();

            // Add current method to the call chain
            parentCallChain.Add(methodDef);

            foreach (var instr in methodDef.Body.Instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                    continue;

                if (!(instr.Operand is MethodReference calledMethod))
                    continue;

                // Check if this call is forbidden
                foreach (var forbidden in ToolPermissionRules.CheckedApis)
                {
                    if (calledMethod.DeclaringType.FullName == forbidden.TypeName &&
                        calledMethod.Name == forbidden.MethodName)
                    {
                        bool permissionFound = false;

                        // Support new ExpectedPermissions array format
                        if (forbidden.ExpectedPermissions != null && forbidden.ExpectedPermissions.Length > 0)
                        {
                            // Check if ANY of the expected permissions is found in current method or any parent method (OR logic)
                            permissionFound = forbidden.ExpectedPermissions.Any(expectedPermission =>
                                parentCallChain.Any(parentMethod =>
                                    parentMethod.Body.Instructions.Any(i =>
                                    {
                                        if (i.OpCode != OpCodes.Call && i.OpCode != OpCodes.Callvirt)
                                            return false;

                                        if (!(i.Operand is MethodReference mr))
                                            return false;

                                        if (mr.Name != expectedPermission.MethodName)
                                            return false;

                                        if (expectedPermission.ExpectedOperation != null)
                                        {
                                            if (!TryGetEnumArgumentValue(i, out var pushedValue))
                                                return false;

                                            var expectedValue = Convert.ToInt32(expectedPermission.ExpectedOperation);
                                            if (pushedValue != expectedValue)
                                                return false;
                                        }

                                        return true;
                                    })
                                )
                            );
                        }

                        if (!permissionFound)
                        {
                            var expectedPermissionsText = forbidden.ExpectedPermissions != null && forbidden.ExpectedPermissions.Length > 0
                                ? string.Join(" OR ", forbidden.ExpectedPermissions.Select(p => $"{p.MethodName}({p.ExpectedOperation})"))
                                : "unknown permission check";

                            throw new Exception(
                                $"Forbidden call '{calledMethod.FullName}' detected without permission check. Expected one of: {expectedPermissionsText}"
                            );
                        }
                    }
                }

                // Recursively inspect called method bodies
                MethodDefinition resolved = null;
                try
                {
                    resolved = calledMethod.Resolve();
                }
                catch
                {
                    // ignore external framework method, no body to inspect
                }
                if (resolved != null && resolved.HasBody)
                    ValidateMethodRecursive(resolved, visited, depth + 1, maxDepth, parentCallChain);
            }

            // Remove current method from call chain when exiting
            parentCallChain.RemoveAt(parentCallChain.Count - 1);
        }

        static bool HasIgnoreAttribute(MethodDefinition methodDef)
        {
            var ignoreAttrTypeName = typeof(ToolPermissionIgnoreAttribute).FullName;
            return methodDef.CustomAttributes.Any(attr =>
                attr.AttributeType.FullName == ignoreAttrTypeName);
        }


        static TypeDefinition FindTypeDefinition(ModuleDefinition module, Type type)
        {
            if (type == null || type.FullName == null)
                return null;

            var parts = type.FullName.Split('+');
            var typeDef = module.GetType(parts[0]);
            if (typeDef == null)
                return null;

            for (var i = 1; i < parts.Length; i++)
            {
                typeDef = typeDef.NestedTypes.FirstOrDefault(t => t.Name == parts[i]);
                if (typeDef == null)
                    return null;
            }

            return typeDef;
        }

        static bool TryGetEnumArgumentValue(Instruction callInstr, out int enumValue, int depth = 0)
        {
            enumValue = 0;

            const int maxRecursiveDepth = 15;
            if (depth > maxRecursiveDepth)
                return false;

            const int maxBackscan = 80;  // Need because async calls adds dozen of operations
            var cur = callInstr.Previous;
            var scanned = 0;

            while (cur != null && scanned < maxBackscan)
            {
                switch (cur.OpCode.Code)
                {
                    // direct constants
                    case Code.Ldc_I4_M1: enumValue = -1; return true;
                    case Code.Ldc_I4_0:  enumValue = 0;  return true;
                    case Code.Ldc_I4_1:  enumValue = 1;  return true;
                    case Code.Ldc_I4_2:  enumValue = 2;  return true;
                    case Code.Ldc_I4_3:  enumValue = 3;  return true;
                    case Code.Ldc_I4_4:  enumValue = 4;  return true;
                    case Code.Ldc_I4_5:  enumValue = 5;  return true;
                    case Code.Ldc_I4_6:  enumValue = 6;  return true;
                    case Code.Ldc_I4_7:  enumValue = 7;  return true;
                    case Code.Ldc_I4_8:  enumValue = 8;  return true;

                    case Code.Ldc_I4_S:
                        enumValue = (sbyte)cur.Operand;
                        return true;

                    case Code.Ldc_I4:
                        enumValue = (int)cur.Operand;
                        return true;

                    // loading a local: resolve where it was last stored
                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                    {
                        var index = cur.OpCode.Code - Code.Ldloc_0;
                        if (TryResolveLocalValue(cur, index, out enumValue, depth + 1))
                            return true;
                        break;
                    }

                    case Code.Ldloc_S:
                    case Code.Ldloc:
                    {
                        if (cur.Operand is VariableDefinition v &&
                            TryResolveLocalValue(cur, v.Index, out enumValue, depth + 1))
                            return true;
                        break;
                    }

                    // static readonly enum fields
                    case Code.Ldsfld:
                    {
                        if (cur.Operand is FieldReference fr && fr.Resolve()?.HasConstant == true)
                        {
                            enumValue = (int)fr.Resolve().Constant;
                            return true;
                        }
                        break;
                    }
                }

                cur = cur.Previous;
                scanned++;
            }

            return false;
        }

        static bool TryResolveLocalValue(Instruction ldlocInstr, int localIndex, out int enumValue, int depth)
        {
            enumValue = 0;

            const int maxScan = 80;
            var scanned = 0;
            var cur = ldlocInstr.Previous;

            while (cur != null && scanned < maxScan)
            {
                var isStore = false;
                var storeIndex = -1;

                switch (cur.OpCode.Code)
                {
                    case Code.Stloc_0: storeIndex = 0; isStore = true; break;
                    case Code.Stloc_1: storeIndex = 1; isStore = true; break;
                    case Code.Stloc_2: storeIndex = 2; isStore = true; break;
                    case Code.Stloc_3: storeIndex = 3; isStore = true; break;

                    case Code.Stloc_S:
                    case Code.Stloc:
                        if (cur.Operand is VariableDefinition v)
                        {
                            storeIndex = v.Index;
                            isStore = true;
                        }
                        break;
                }

                if (isStore && storeIndex == localIndex)
                {
                    // The value stored is the previous instruction's pushed value
                    return TryGetEnumArgumentValue(cur, out enumValue, depth + 1);
                }

                cur = cur.Previous;
                scanned++;
            }

            return false;
        }
    }
}
