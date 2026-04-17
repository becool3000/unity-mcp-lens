using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class ProjectDiagnosticsTools
    {
        public const string ScanMissingScriptsDescription = @"Scans open scenes and prefab assets for missing MonoBehaviour script references.

Args:
    Under: Folder under Assets to scan for prefabs. Defaults to Assets.
    IncludeOpenScenes: Scan currently open scenes.
    IncludePrefabs: Scan prefab assets on disk.
    MaxPrefabs: Maximum number of prefab assets to inspect.

Returns:
    Dictionary with success/message/data. Data contains scene findings, prefab findings, and counts.";

        public const string ValidateReferencesDescription = @"Audits serialized object-reference fields on a GameObject, component, or asset.

Args:
    Target: Target GameObject/path, instance id string, or asset path.
    SearchMethod: How to find a scene object target ('by_name', 'by_id', 'by_path').
    ComponentName: Optional component type name to narrow the audit.
    IncludeInactive: Include inactive scene objects when resolving the target.

Returns:
    Dictionary with success/message/data. Data contains null and missing object-reference fields without project-specific interpretation.";

        [McpTool("Unity.Project.ScanMissingScripts", ScanMissingScriptsDescription, Groups = new[] { "diagnostics", "project" }, EnabledByDefault = true)]
        public static object ScanMissingScripts(ScanMissingScriptsParams parameters)
        {
            parameters ??= new ScanMissingScriptsParams();

            var sceneFindings = new List<object>();
            var prefabFindings = new List<object>();

            if (parameters.IncludeOpenScenes)
            {
                for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                {
                    Scene scene = SceneManager.GetSceneAt(sceneIndex);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        continue;
                    }

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        CollectMissingScripts(root.transform, scene.path, sceneFindings, parameters.MaxFindings);
                        if (sceneFindings.Count + prefabFindings.Count >= parameters.MaxFindings)
                            break;
                    }
                }
            }

            if (parameters.IncludePrefabs)
            {
                string under = string.IsNullOrWhiteSpace(parameters.Under) ? "Assets" : parameters.Under.Replace('\\', '/');
                string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { under });
                int maxPrefabs = Math.Max(1, parameters.MaxPrefabs);
                foreach (string guid in prefabGuids.Take(maxPrefabs))
                {
                    if (sceneFindings.Count + prefabFindings.Count >= parameters.MaxFindings)
                        break;

                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                    try
                    {
                        CollectMissingScripts(prefabRoot.transform, assetPath, prefabFindings, parameters.MaxFindings - sceneFindings.Count);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }

            var payload = new
            {
                sceneFindingCount = sceneFindings.Count,
                prefabFindingCount = prefabFindings.Count,
                sceneFindings,
                prefabFindings
            };

            return Response.Success(
                "Missing-script scan completed.",
                ShapePayload(
                    "Unity.Project.ScanMissingScripts",
                    $"Missing-script scan completed with {sceneFindings.Count + prefabFindings.Count} findings.",
                    payload,
                    new
                    {
                        tool = "Unity.Project.ScanMissingScripts",
                        args = new
                        {
                            parameters.Under,
                            parameters.IncludeOpenScenes,
                            parameters.IncludePrefabs,
                            parameters.MaxPrefabs,
                            parameters.MaxFindings
                        }
                    }));
        }

        [McpTool("Unity.Object.ValidateReferences", ValidateReferencesDescription, Groups = new[] { "diagnostics", "project" }, EnabledByDefault = true)]
        public static object ValidateReferences(ValidateReferencesParams parameters)
        {
            parameters ??= new ValidateReferencesParams();
            if (string.IsNullOrWhiteSpace(parameters.Target))
            {
                return Response.Error("Target is required.");
            }

            if (!TryResolveValidationTarget(parameters, out UnityEngine.Object targetObject, out string targetLabel, out string error))
            {
                return Response.Error(error ?? "Target could not be resolved.");
            }

            List<UnityEngine.Object> auditTargets = ResolveAuditTargets(targetObject, parameters.ComponentName);
            if (auditTargets.Count == 0)
            {
                return Response.Error("No matching objects or components were found to validate.");
            }

            var findings = new List<object>();
            foreach (UnityEngine.Object auditTarget in auditTargets)
            {
                SerializedObject serializedObject = new(auditTarget);
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    bool missingReference = iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0;
                    bool nullReference = iterator.objectReferenceValue == null;
                    if (!missingReference && !nullReference)
                    {
                        continue;
                    }

                    findings.Add(new
                    {
                        objectName = auditTarget.name,
                        objectType = auditTarget.GetType().FullName,
                        propertyPath = iterator.propertyPath,
                        displayName = iterator.displayName,
                        isMissingReference = missingReference,
                        isNullReference = nullReference,
                        instanceID = iterator.objectReferenceInstanceIDValue
                    });

                    if (findings.Count >= PayloadBudgetPolicy.MaxDiagnosticFindings)
                    {
                        break;
                    }
                }

                if (findings.Count >= PayloadBudgetPolicy.MaxDiagnosticFindings)
                {
                    break;
                }
            }

            var payload = new
            {
                target = targetLabel,
                findingCount = findings.Count,
                findings
            };

            return Response.Success(
                $"Validated {auditTargets.Count} object(s).",
                ShapePayload(
                    "Unity.Object.ValidateReferences",
                    $"Validated {auditTargets.Count} object(s) and found {findings.Count} reference issues.",
                    payload,
                    new
                    {
                        tool = "Unity.Object.ValidateReferences",
                        args = new
                        {
                            parameters.Target,
                            parameters.SearchMethod,
                            parameters.ComponentName,
                            parameters.IncludeInactive
                        }
                    }));
        }

        static void CollectMissingScripts(Transform transform, string ownerPath, List<object> findings, int remainingBudget)
        {
            if (transform == null || remainingBudget <= 0)
            {
                return;
            }

            int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            if (missingCount > 0)
            {
                findings.Add(new
                {
                    ownerPath,
                    hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(transform),
                    missingScriptCount = missingCount
                });
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                if (findings.Count >= remainingBudget)
                    break;

                CollectMissingScripts(transform.GetChild(i), ownerPath, findings, remainingBudget);
            }
        }

        static bool TryResolveValidationTarget(ValidateReferencesParams parameters, out UnityEngine.Object targetObject, out string targetLabel, out string error)
        {
            targetObject = null;
            targetLabel = parameters.Target;
            error = null;

            string assetPath = parameters.Target.Replace('\\', '/');
            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                targetObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (targetObject != null)
                {
                    targetLabel = assetPath;
                    return true;
                }

                error = $"Asset target '{assetPath}' could not be loaded.";
                return false;
            }

            var findParams = new JObject
            {
                ["search_inactive"] = parameters.IncludeInactive
            };
            GameObject targetGo = ObjectsHelper.FindObject(parameters.Target, parameters.SearchMethod, findParams);
            if (targetGo == null)
            {
                error = $"Target '{parameters.Target}' could not be resolved.";
                return false;
            }

            targetObject = targetGo;
            targetLabel = UiDiagnosticsHelper.GetHierarchyPath(targetGo.transform);
            return true;
        }

        static List<UnityEngine.Object> ResolveAuditTargets(UnityEngine.Object targetObject, string componentName)
        {
            var results = new List<UnityEngine.Object>();
            if (targetObject == null)
            {
                return results;
            }

            if (targetObject is GameObject go)
            {
                if (string.IsNullOrWhiteSpace(componentName))
                {
                    results.AddRange(go.GetComponents<Component>().Where(component => component != null).Cast<UnityEngine.Object>());
                    return results;
                }

                if (ComponentResolver.TryResolve(componentName, out Type componentType, out _))
                {
                    Component resolved = go.GetComponent(componentType);
                    if (resolved != null)
                    {
                        results.Add(resolved);
                    }
                }
                else
                {
                    results.AddRange(go.GetComponents<Component>()
                        .Where(component => component != null &&
                                            (component.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase) ||
                                             component.GetType().FullName.Equals(componentName, StringComparison.OrdinalIgnoreCase)))
                        .Cast<UnityEngine.Object>());
                }

                return results;
            }

            results.Add(targetObject);
            return results;
        }

        static object ShapePayload(string toolName, string summary, object data, object detailRef)
        {
            return ToolResultCompactor.ShapeJsonPayload(toolName, summary, data, detailRef);
        }
    }
}
