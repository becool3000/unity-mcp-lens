#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Services.Components
{
    sealed class ComponentSearchRequest
    {
        public string query { get; set; }
        public string[] providers { get; set; } = Array.Empty<string>();
        public bool includeComponents { get; set; } = true;
        public bool includePrefabs { get; set; } = true;
        public bool includePresets { get; set; } = true;
        public bool includeMissingPackages { get; set; } = true;
        public int maxResults { get; set; } = 30;
        public int maxAssetScans { get; set; } = 120;
    }

    sealed class ComponentResolveCapabilityRequest
    {
        public string intent { get; set; }
        public string context { get; set; }
        public bool includePrefabs { get; set; } = true;
        public bool includePresets { get; set; } = true;
        public bool includeMissingPackages { get; set; } = true;
        public int maxResults { get; set; } = 20;
        public int maxAssetScans { get; set; } = 120;
    }

    sealed class ComponentInspectSchemaRequest
    {
        public string componentName { get; set; }
        public string target { get; set; }
        public string searchMethod { get; set; } = "by_name";
        public bool includeInactive { get; set; } = true;
        public int componentIndex { get; set; }
        public bool includeDefaults { get; set; }
        public bool includeReadOnly { get; set; }
        public int maxFields { get; set; } = 120;
    }

    sealed class SceneFindComponentsRequest
    {
        public string componentName { get; set; }
        public string query { get; set; }
        public string intent { get; set; }
        public string scene { get; set; }
        public bool includeInactive { get; set; }
        public int maxResults { get; set; } = 50;
        public string[] propertyPaths { get; set; } = Array.Empty<string>();
    }

    sealed class AuthoringSuggestReusePlanRequest
    {
        public string intent { get; set; }
        public string context { get; set; }
        public bool includeSceneSearch { get; set; } = true;
        public bool includePrefabs { get; set; } = true;
        public bool includePresets { get; set; } = true;
        public bool includeMissingPackages { get; set; } = true;
        public int maxResults { get; set; } = 12;
    }

    sealed class ComponentDiscoveryService
    {
        sealed class ProviderInfo
        {
            public string provider { get; set; }
            public string assemblyName { get; set; }
            public string assetPath { get; set; }
            public string packageId { get; set; }
            public string packageName { get; set; }
            public string packageVersion { get; set; }
        }

        sealed class Candidate
        {
            public string resultKind { get; set; }
            public string provider { get; set; }
            public string name { get; set; }
            public string displayName { get; set; }
            public string typeName { get; set; }
            public string assemblyName { get; set; }
            public string assetPath { get; set; }
            public string guid { get; set; }
            public string packageId { get; set; }
            public string packageName { get; set; }
            public string packageVersion { get; set; }
            public string recommendedVersion { get; set; }
            public string compatibility { get; set; }
            public string installRisk { get; set; }
            public string compileImportImpact { get; set; }
            public string fallbackPlan { get; set; }
            public double confidence { get; set; }
            public string reason { get; set; }
            public bool serializedSchemaAvailable { get; set; }
            public string[] setupRequirements { get; set; } = Array.Empty<string>();
            public string[] matchedTerms { get; set; } = Array.Empty<string>();
            public string[] componentTypes { get; set; } = Array.Empty<string>();
        }

        sealed class CapabilityDefinition
        {
            public string id { get; init; }
            public string[] terms { get; init; }
            public CapabilityComponent[] components { get; init; } = Array.Empty<CapabilityComponent>();
            public MissingPackageSpec[] missingPackages { get; init; } = Array.Empty<MissingPackageSpec>();
            public string[] setupRequirements { get; init; } = Array.Empty<string>();
            public string fallbackPlan { get; init; }
        }

        sealed class CapabilityComponent
        {
            public string name { get; init; }
            public string typeName { get; init; }
            public string packageId { get; init; }
            public double confidence { get; init; }
            public string reason { get; init; }
            public string[] setupRequirements { get; init; } = Array.Empty<string>();
        }

        sealed class MissingPackageSpec
        {
            public string packageId { get; init; }
            public string packageName { get; init; }
            public string recommendedVersion { get; init; }
            public string compatibility { get; init; }
            public string installRisk { get; init; }
            public string compileImportImpact { get; init; }
            public string fallbackPlan { get; init; }
        }

        static readonly CapabilityDefinition[] k_Capabilities =
        {
            new()
            {
                id = "follow_camera",
                terms = new[] { "follow camera", "camera follow", "third person camera", "chase camera", "tracking camera", "camera target" },
                components = new[]
                {
                    new CapabilityComponent
                    {
                        name = "Cinemachine Camera",
                        typeName = "Unity.Cinemachine.CinemachineCamera",
                        packageId = "com.unity.cinemachine",
                        confidence = 0.94,
                        reason = "Package-backed Unity camera follow/composition component."
                    },
                    new CapabilityComponent
                    {
                        name = "Cinemachine Virtual Camera",
                        typeName = "Cinemachine.CinemachineVirtualCamera",
                        packageId = "com.unity.cinemachine",
                        confidence = 0.9,
                        reason = "Older Cinemachine camera authoring component."
                    },
                    new CapabilityComponent
                    {
                        name = "Position Constraint",
                        typeName = "UnityEngine.Animations.PositionConstraint",
                        confidence = 0.64,
                        reason = "Built-in constraint can make an object follow another transform."
                    },
                    new CapabilityComponent
                    {
                        name = "Parent Constraint",
                        typeName = "UnityEngine.Animations.ParentConstraint",
                        confidence = 0.62,
                        reason = "Built-in constraint can copy target transform motion with authorable offsets."
                    },
                    new CapabilityComponent
                    {
                        name = "Camera",
                        typeName = "UnityEngine.Camera",
                        confidence = 0.42,
                        reason = "Built-in camera is necessary for rendering but does not solve following by itself."
                    }
                },
                missingPackages = new[]
                {
                    CinemachinePackage("Best standard Unity-native follow-camera solution when not installed.")
                },
                setupRequirements = new[] { "Bind target/follow references after selecting the component.", "Verify composition in Play Mode." },
                fallbackPlan = "Use built-in constraint components for simple transform following; write a custom script only if camera behavior needs custom logic unsupported by Cinemachine or constraints."
            },
            new()
            {
                id = "input",
                terms = new[] { "input", "controls", "player input", "gamepad", "keyboard input", "input actions" },
                components = new[]
                {
                    new CapabilityComponent
                    {
                        name = "Player Input",
                        typeName = "UnityEngine.InputSystem.PlayerInput",
                        packageId = "com.unity.inputsystem",
                        confidence = 0.9,
                        reason = "Package-backed Input System component for binding actions to players."
                    },
                    new CapabilityComponent
                    {
                        name = "Input System UI Input Module",
                        typeName = "UnityEngine.InputSystem.UI.InputSystemUIInputModule",
                        packageId = "com.unity.inputsystem",
                        confidence = 0.8,
                        reason = "Package-backed UI event input module."
                    }
                },
                missingPackages = new[]
                {
                    new MissingPackageSpec
                    {
                        packageId = "com.unity.inputsystem",
                        packageName = "Input System",
                        recommendedVersion = "project-compatible latest",
                        compatibility = "Requires active input handling setting review.",
                        installRisk = "Medium",
                        compileImportImpact = "Package import and possible script reload; may require PlayerSettings input backend change.",
                        fallbackPlan = "Use legacy input APIs only when the project intentionally remains on the old input backend."
                    }
                },
                fallbackPlan = "Inspect existing InputAction assets and PlayerInput components before generating scripts."
            },
            new()
            {
                id = "ui",
                terms = new[] { "ui", "button", "canvas", "menu", "hud", "screen", "layout" },
                components = new[]
                {
                    new CapabilityComponent { name = "Canvas", typeName = "UnityEngine.Canvas", packageId = "com.unity.ugui", confidence = 0.82, reason = "Durable uGUI root surface." },
                    new CapabilityComponent { name = "Button", typeName = "UnityEngine.UI.Button", packageId = "com.unity.ugui", confidence = 0.78, reason = "Durable uGUI button component." },
                    new CapabilityComponent { name = "Event System", typeName = "UnityEngine.EventSystems.EventSystem", packageId = "com.unity.ugui", confidence = 0.74, reason = "Scene-level UI event dispatcher." },
                    new CapabilityComponent { name = "UIDocument", typeName = "UnityEngine.UIElements.UIDocument", confidence = 0.62, reason = "UI Toolkit scene entry component." }
                },
                missingPackages = new[]
                {
                    new MissingPackageSpec
                    {
                        packageId = "com.unity.ugui",
                        packageName = "Unity UI",
                        recommendedVersion = "project-compatible latest",
                        compatibility = "Built into many Unity templates; available as com.unity.ugui in newer versions.",
                        installRisk = "Low",
                        compileImportImpact = "Package import may refresh UI assemblies.",
                        fallbackPlan = "Use UI Toolkit when that is the established project UI stack."
                    }
                },
                fallbackPlan = "Prefer Canvas/EventSystem/UI Toolkit assets and existing UI prefabs before scripts."
            },
            new()
            {
                id = "text",
                terms = new[] { "text", "label", "font", "tmp", "textmeshpro", "caption" },
                components = new[]
                {
                    new CapabilityComponent { name = "TextMeshPro UGUI", typeName = "TMPro.TextMeshProUGUI", packageId = "com.unity.textmeshpro", confidence = 0.88, reason = "Package-backed high-quality UI text component." },
                    new CapabilityComponent { name = "TextMeshPro", typeName = "TMPro.TextMeshPro", packageId = "com.unity.textmeshpro", confidence = 0.82, reason = "Package-backed world-space text component." },
                    new CapabilityComponent { name = "uGUI Text", typeName = "UnityEngine.UI.Text", packageId = "com.unity.ugui", confidence = 0.5, reason = "Legacy uGUI text component." }
                },
                missingPackages = new[]
                {
                    new MissingPackageSpec
                    {
                        packageId = "com.unity.textmeshpro",
                        packageName = "TextMeshPro",
                        recommendedVersion = "project-compatible latest",
                        compatibility = "Usually bundled with modern Unity projects.",
                        installRisk = "Low",
                        compileImportImpact = "Package import may add TMP resources.",
                        fallbackPlan = "Use existing uGUI Text only for legacy UI consistency."
                    }
                },
                fallbackPlan = "Use existing TMP components/assets before writing text-rendering scripts."
            },
            new()
            {
                id = "navigation",
                terms = new[] { "navmesh", "navigation", "pathfinding", "agent", "ai move" },
                components = new[]
                {
                    new CapabilityComponent { name = "NavMesh Agent", typeName = "UnityEngine.AI.NavMeshAgent", confidence = 0.84, reason = "Built-in navigation agent component." },
                    new CapabilityComponent { name = "NavMesh Surface", typeName = "Unity.AI.Navigation.NavMeshSurface", packageId = "com.unity.ai.navigation", confidence = 0.8, reason = "Package-backed authored NavMesh baking surface." }
                },
                missingPackages = new[]
                {
                    new MissingPackageSpec
                    {
                        packageId = "com.unity.ai.navigation",
                        packageName = "AI Navigation",
                        recommendedVersion = "project-compatible latest",
                        compatibility = "Useful for authored NavMesh surfaces in-package.",
                        installRisk = "Low",
                        compileImportImpact = "Package import and assembly refresh.",
                        fallbackPlan = "Use built-in NavMeshAgent with existing baked navigation data."
                    }
                },
                fallbackPlan = "Use NavMeshAgent and existing/baked NavMesh assets before custom pathfinding scripts."
            },
            new()
            {
                id = "physics",
                terms = new[] { "physics", "rigidbody", "collider", "gravity", "trigger", "collision" },
                components = new[]
                {
                    new CapabilityComponent { name = "Rigidbody", typeName = "UnityEngine.Rigidbody", confidence = 0.8, reason = "Built-in 3D physics body." },
                    new CapabilityComponent { name = "Box Collider", typeName = "UnityEngine.BoxCollider", confidence = 0.74, reason = "Built-in 3D collision shape." },
                    new CapabilityComponent { name = "Rigidbody 2D", typeName = "UnityEngine.Rigidbody2D", confidence = 0.72, reason = "Built-in 2D physics body." }
                },
                fallbackPlan = "Configure built-in physics components before writing simulation scripts."
            },
            new()
            {
                id = "animation",
                terms = new[] { "animation", "animator", "state machine", "clip", "timeline" },
                components = new[]
                {
                    new CapabilityComponent { name = "Animator", typeName = "UnityEngine.Animator", confidence = 0.82, reason = "Built-in Mecanim animation controller component." },
                    new CapabilityComponent { name = "Playable Director", typeName = "UnityEngine.Playables.PlayableDirector", packageId = "com.unity.timeline", confidence = 0.72, reason = "Timeline playback component for authored sequences." }
                },
                missingPackages = new[]
                {
                    new MissingPackageSpec
                    {
                        packageId = "com.unity.timeline",
                        packageName = "Timeline",
                        recommendedVersion = "project-compatible latest",
                        compatibility = "Package-backed timeline authoring.",
                        installRisk = "Low",
                        compileImportImpact = "Package import and assembly refresh.",
                        fallbackPlan = "Use Animator and AnimationClip assets for simple animation."
                    }
                },
                fallbackPlan = "Prefer Animator, clips, Timeline, presets, and existing animation prefabs before scripts."
            },
            new()
            {
                id = "audio",
                terms = new[] { "audio", "sound", "music", "sfx", "listener" },
                components = new[]
                {
                    new CapabilityComponent { name = "Audio Source", typeName = "UnityEngine.AudioSource", confidence = 0.8, reason = "Built-in authored audio playback component." },
                    new CapabilityComponent { name = "Audio Listener", typeName = "UnityEngine.AudioListener", confidence = 0.7, reason = "Built-in scene listener component." }
                },
                fallbackPlan = "Use AudioSource/AudioMixer authored assets before custom audio scripts."
            },
            new()
            {
                id = "render_pipeline",
                terms = new[] { "urp", "universal render pipeline", "render pipeline", "post processing", "camera data" },
                components = new[]
                {
                    new CapabilityComponent { name = "Universal Additional Camera Data", typeName = "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", packageId = "com.unity.render-pipelines.universal", confidence = 0.82, reason = "URP package component for camera rendering features." }
                },
                missingPackages = new[]
                {
                    new MissingPackageSpec
                    {
                        packageId = "com.unity.render-pipelines.universal",
                        packageName = "Universal RP",
                        recommendedVersion = "project-compatible latest",
                        compatibility = "Render-pipeline migration can be project-wide.",
                        installRisk = "High",
                        compileImportImpact = "Package import plus render pipeline asset/settings work.",
                        fallbackPlan = "Use the current project render pipeline unless the task explicitly requires URP features."
                    }
                },
                fallbackPlan = "Inspect current render pipeline settings and camera components before installing packages or scripts."
            }
        };

        static Dictionary<Type, string> s_ScriptPathByType;
        static PackageInfo[] s_Packages;
        static MethodInfo s_FindPackageForAssemblyMethod;

        public object Search(ComponentSearchRequest request)
        {
            request ??= new ComponentSearchRequest();
            int maxResults = Math.Clamp(request.maxResults, 1, 200);
            int maxAssetScans = Math.Clamp(request.maxAssetScans, 0, 500);
            var providerFilter = NormalizeProviderFilter(request.providers);
            var candidates = new List<Candidate>();

            if (request.includeComponents)
                candidates.AddRange(SearchComponentTypes(request.query, providerFilter));
            if (request.includePrefabs)
                candidates.AddRange(SearchPrefabAssets(request.query, providerFilter, maxAssetScans));
            if (request.includePresets)
                candidates.AddRange(SearchPresetAssets(request.query, providerFilter, maxAssetScans));
            if (request.includeMissingPackages)
                candidates.AddRange(SearchMissingPackages(request.query, providerFilter));

            var ordered = candidates
                .Where(candidate => candidate.confidence > 0 || string.IsNullOrWhiteSpace(request.query))
                .OrderByDescending(candidate => candidate.confidence)
                .ThenBy(candidate => candidate.provider, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.displayName ?? candidate.name, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(ToCandidateData)
                .ToArray();

            return new
            {
                query = request.query,
                providers = providerFilter.Length == 0 ? new[] { "all" } : providerFilter,
                includeComponents = request.includeComponents,
                includePrefabs = request.includePrefabs,
                includePresets = request.includePresets,
                includeMissingPackages = request.includeMissingPackages,
                resultCount = ordered.Length,
                results = ordered
            };
        }

        public object ResolveCapability(ComponentResolveCapabilityRequest request)
        {
            var data = ResolveCapabilityData(request);
            return new
            {
                intent = data.intent,
                capabilityMatches = data.capabilityMatches,
                resultCount = data.results.Length,
                results = data.results.Select(ToCandidateData).ToArray(),
                newScriptAppearsNecessary = data.newScriptAppearsNecessary,
                reuseInsufficiencyReport = data.reuseInsufficiencyReport
            };
        }

        public object InspectSchema(ComponentInspectSchemaRequest request)
        {
            request ??= new ComponentInspectSchemaRequest();
            int maxFields = Math.Clamp(request.maxFields, 1, 500);
            if (string.IsNullOrWhiteSpace(request.componentName))
            {
                return new
                {
                    status = "failed",
                    errorKind = "component_required",
                    error = "componentName is required."
                };
            }

            if (!UnityComponentResolver.TryResolve(request.componentName, out Type componentType, out string typeError))
            {
                return new
                {
                    status = "failed",
                    errorKind = "component_not_found",
                    componentName = request.componentName,
                    error = typeError
                };
            }

            Component component = null;
            GameObject temporaryObject = null;
            object targetSummary = null;
            string schemaSource = "temporary_component";
            var warnings = new List<string>();

            try
            {
                if (!string.IsNullOrWhiteSpace(request.target))
                {
                    var findParams = new Newtonsoft.Json.Linq.JObject
                    {
                        ["search_inactive"] = request.includeInactive,
                        ["searchInactive"] = request.includeInactive
                    };
                    GameObject target = ObjectsHelper.FindObject(request.target, request.searchMethod, findParams);
                    if (target == null)
                    {
                        return new
                        {
                            status = "failed",
                            errorKind = "target_not_found",
                            componentName = request.componentName,
                            target = request.target,
                            error = "Target GameObject could not be resolved."
                        };
                    }

                    Component[] matches = target.GetComponents(componentType);
                    int componentIndex = Math.Max(0, request.componentIndex);
                    if (matches.Length <= componentIndex || matches[componentIndex] == null)
                    {
                        return new
                        {
                            status = "failed",
                            errorKind = "component_not_found_on_target",
                            componentName = request.componentName,
                            target = request.target,
                            componentIndex,
                            error = "Resolved target does not have the requested component at the requested index."
                        };
                    }

                    component = matches[componentIndex];
                    schemaSource = "scene_component";
                    targetSummary = DescribeGameObject(target);
                }
                else if (!TryCreateTemporaryComponent(componentType, out temporaryObject, out component, out string createWarning))
                {
                    return new
                    {
                        status = "failed",
                        errorKind = "schema_unavailable",
                        componentName = request.componentName,
                        resolvedType = componentType.FullName,
                        provider = DescribeProvider(componentType).provider,
                        serializedSchemaAvailability = "unavailable",
                        error = createWarning
                    };
                }
                else if (!string.IsNullOrWhiteSpace(createWarning))
                {
                    warnings.Add(createWarning);
                }

                var fields = ReadSerializedSchema(component, request.includeDefaults, request.includeReadOnly, maxFields, out int totalFieldCount, out int omittedFieldCount, warnings);
                ProviderInfo provider = DescribeProvider(componentType);
                return new
                {
                    status = "ready",
                    componentName = request.componentName,
                    resolvedType = componentType.FullName,
                    componentType = DescribeComponentType(componentType, provider),
                    provider = provider.provider,
                    schemaSource,
                    target = targetSummary,
                    includeDefaults = request.includeDefaults,
                    includeReadOnly = request.includeReadOnly,
                    serializedSchemaAvailability = fields.Length > 0 ? "available" : "empty",
                    totalFieldCount,
                    returnedFieldCount = fields.Length,
                    omittedFieldCount,
                    warnings = warnings.ToArray(),
                    fields
                };
            }
            finally
            {
                if (temporaryObject != null)
                    UnityEngine.Object.DestroyImmediate(temporaryObject);
            }
        }

        public object FindSceneComponents(SceneFindComponentsRequest request)
        {
            request ??= new SceneFindComponentsRequest();
            int maxResults = Math.Clamp(request.maxResults, 1, 500);
            string query = FirstNonEmpty(request.query, request.intent, request.componentName);
            var candidateTypes = ResolveSceneCandidateTypes(request.componentName, request.intent, request.query);
            var inactiveMode = request.includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            GameObject[] allObjects = UnityApiAdapter.FindObjectsByType<GameObject>(inactiveMode);
            var matches = new List<object>();
            int scannedComponentCount = 0;

            foreach (GameObject gameObject in allObjects.OrderBy(go => UiDiagnosticsHelper.GetHierarchyPath(go.transform), StringComparer.Ordinal))
            {
                if (!MatchesScene(gameObject, request.scene))
                    continue;

                foreach (Component component in gameObject.GetComponents<Component>())
                {
                    scannedComponentCount++;
                    if (component == null)
                        continue;

                    double score = ScoreSceneComponent(component.GetType(), query, candidateTypes, out string matchKind, out string reason);
                    if (score <= 0)
                        continue;

                    matches.Add(BuildSceneComponentMatch(component, score, matchKind, reason, request.propertyPaths));
                    if (matches.Count >= maxResults)
                        break;
                }

                if (matches.Count >= maxResults)
                    break;
            }

            return new
            {
                query,
                componentName = request.componentName,
                intent = request.intent,
                scene = string.IsNullOrWhiteSpace(request.scene) ? null : request.scene,
                includeInactive = request.includeInactive,
                candidateTypes = candidateTypes.Select(type => type.FullName).Distinct().ToArray(),
                scannedComponentCount,
                matchCount = matches.Count,
                truncated = matches.Count >= maxResults,
                matches = matches.ToArray()
            };
        }

        public object SuggestReusePlan(AuthoringSuggestReusePlanRequest request)
        {
            request ??= new AuthoringSuggestReusePlanRequest();
            int maxResults = Math.Clamp(request.maxResults, 1, 50);
            var resolveRequest = new ComponentResolveCapabilityRequest
            {
                intent = request.intent,
                context = request.context,
                includePrefabs = request.includePrefabs,
                includePresets = request.includePresets,
                includeMissingPackages = request.includeMissingPackages,
                maxResults = maxResults,
                maxAssetScans = 120
            };

            var resolved = ResolveCapabilityData(resolveRequest);
            object sceneFind = null;
            int sceneMatchCount = 0;
            if (request.includeSceneSearch && !string.IsNullOrWhiteSpace(request.intent))
            {
                sceneFind = FindSceneComponents(new SceneFindComponentsRequest
                {
                    intent = request.intent,
                    query = request.intent,
                    includeInactive = true,
                    maxResults = maxResults
                });

                sceneMatchCount = ExtractInt(sceneFind, "matchCount");
            }

            Candidate bestReusable = resolved.results
                .Where(candidate => !string.Equals(candidate.provider, "missing package", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.confidence)
                .FirstOrDefault();
            bool hasReusable = bestReusable != null && bestReusable.confidence >= 0.45;
            bool hasSceneCandidate = sceneMatchCount > 0;
            bool newScriptNecessary = !hasReusable && !hasSceneCandidate;

            var plan = new List<object>
            {
                new
                {
                    step = 1,
                    phase = "discovery",
                    tool = "Unity.Component.ResolveCapability",
                    purpose = "Rank built-in, installed-package, project, prefab, preset, and missing-package options for the requested intent.",
                    status = resolved.results.Length > 0 ? "completed" : "no_candidates"
                }
            };

            if (request.includeSceneSearch)
            {
                plan.Add(new
                {
                    step = 2,
                    phase = "discovery",
                    tool = "Unity.Scene.FindComponents",
                    purpose = "Find existing scene components that already solve or partially solve the need.",
                    status = hasSceneCandidate ? "scene_candidates_found" : "no_scene_candidates"
                });
            }

            if (bestReusable != null)
            {
                plan.Add(new
                {
                    step = 3,
                    phase = "inspection",
                    tool = "Unity.Component.InspectSchema",
                    targetComponent = bestReusable.typeName,
                    purpose = "Inspect serialized fields before any authoring mutation.",
                    status = bestReusable.serializedSchemaAvailable ? "recommended" : "schema_may_be_unavailable"
                });
                plan.Add(new
                {
                    step = 4,
                    phase = "preview",
                    tool = "Unity.GameObject.PreviewComponentChanges / Unity.Scene.PreviewAssignObjectReferences",
                    purpose = "Preview adding/configuring the existing component and object references before apply.",
                    status = "recommended"
                });
            }

            plan.Add(new
            {
                step = plan.Count + 1,
                phase = "policy",
                tool = "custom script generation",
                purpose = "Generate a new script only after reuse insufficiency is explicit.",
                status = newScriptNecessary ? "allowed_with_reuse_insufficiency_report" : "not_recommended"
            });

            return new
            {
                intent = request.intent,
                context = request.context,
                includeSceneSearch = request.includeSceneSearch,
                recommendedFirst = bestReusable == null ? null : ToCandidateData(bestReusable),
                newScriptAppearsNecessary = newScriptNecessary,
                customScriptGeneration = new
                {
                    allowed = newScriptNecessary,
                    requiresReuseInsufficiencyReport = true
                },
                reusePlan = plan.ToArray(),
                capabilityResults = resolved.results.Take(maxResults).Select(ToCandidateData).ToArray(),
                sceneFind,
                reuseInsufficiencyReport = newScriptNecessary
                    ? BuildInsufficiencyReport(request.intent, resolved.results, sceneMatchCount)
                    : new
                    {
                        required = false,
                        reason = hasSceneCandidate
                            ? "Existing scene components may already solve or partially solve the need."
                            : "At least one reusable component/prefab/preset/package-backed option was found."
                    }
            };
        }

        ResolveCapabilityData ResolveCapabilityData(ComponentResolveCapabilityRequest request)
        {
            request ??= new ComponentResolveCapabilityRequest();
            int maxResults = Math.Clamp(request.maxResults, 1, 100);
            string intent = request.intent ?? string.Empty;
            var candidates = new List<Candidate>();
            var capabilityMatches = MatchCapabilities(intent)
                .OrderByDescending(match => match.score)
                .Take(5)
                .ToArray();

            foreach (var match in capabilityMatches)
            {
                foreach (CapabilityComponent component in match.definition.components)
                {
                    if (UnityComponentResolver.TryResolve(component.typeName, out Type type, out _))
                    {
                        var candidate = BuildTypeCandidate(
                            type,
                            intent,
                            Math.Max(component.confidence, match.score),
                            component.reason);
                        candidate.setupRequirements = MergeRequirements(component.setupRequirements, match.definition.setupRequirements);
                        candidates.Add(candidate);
                    }
                    else if (request.includeMissingPackages && !string.IsNullOrWhiteSpace(component.packageId))
                    {
                        var missing = match.definition.missingPackages.FirstOrDefault(package => string.Equals(package.packageId, component.packageId, StringComparison.OrdinalIgnoreCase));
                        if (missing != null && !IsPackageInstalled(missing.packageId))
                            candidates.Add(BuildMissingPackageCandidate(missing, Math.Max(0.45, component.confidence - 0.12), component.reason));
                    }
                }

                if (request.includeMissingPackages)
                {
                    foreach (var missing in match.definition.missingPackages)
                    {
                        if (!IsPackageInstalled(missing.packageId) && candidates.All(candidate => !string.Equals(candidate.packageId, missing.packageId, StringComparison.OrdinalIgnoreCase)))
                            candidates.Add(BuildMissingPackageCandidate(missing, Math.Max(0.4, match.score - 0.08), missing.fallbackPlan));
                    }
                }
            }

            var search = Search(new ComponentSearchRequest
            {
                query = intent,
                includeComponents = true,
                includePrefabs = request.includePrefabs,
                includePresets = request.includePresets,
                includeMissingPackages = request.includeMissingPackages,
                maxResults = maxResults,
                maxAssetScans = request.maxAssetScans
            });
            foreach (Candidate candidate in RehydrateCandidates(search))
                candidates.Add(candidate);

            var deduped = DeduplicateCandidates(candidates)
                .OrderByDescending(candidate => candidate.confidence)
                .ThenBy(candidate => candidate.provider, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.displayName ?? candidate.name, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToArray();

            bool newScriptNecessary = !deduped.Any(candidate =>
                !string.Equals(candidate.provider, "missing package", StringComparison.OrdinalIgnoreCase) &&
                candidate.confidence >= 0.45);

            return new ResolveCapabilityData
            {
                intent = intent,
                capabilityMatches = capabilityMatches.Select(match => new
                {
                    capability = match.definition.id,
                    confidence = Round(match.score),
                    matchedTerms = match.matchedTerms
                }).ToArray(),
                results = deduped,
                newScriptAppearsNecessary = newScriptNecessary,
                reuseInsufficiencyReport = newScriptNecessary
                    ? BuildInsufficiencyReport(intent, deduped, sceneMatchCount: 0)
                    : new { required = false, reason = "Reusable component, prefab, preset, or package-backed options were found." }
            };
        }

        sealed class ResolveCapabilityData
        {
            public string intent { get; set; }
            public object[] capabilityMatches { get; set; }
            public Candidate[] results { get; set; }
            public bool newScriptAppearsNecessary { get; set; }
            public object reuseInsufficiencyReport { get; set; }
        }

        IEnumerable<Candidate> SearchComponentTypes(string query, string[] providerFilter)
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (!IsDiscoverableComponentType(type))
                    continue;

                var candidate = BuildTypeCandidate(type, query, ScoreText(query, TypeSearchText(type)), "Component type matched the search query.");
                if (MatchesProviderFilter(candidate.provider, providerFilter))
                    yield return candidate;
            }
        }

        IEnumerable<Candidate> SearchPrefabAssets(string query, string[] providerFilter, int maxAssetScans)
        {
            if (!MatchesProviderFilter("prefab", providerFilter))
                yield break;

            string filter = string.IsNullOrWhiteSpace(query) ? "t:Prefab" : $"{query} t:Prefab";
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { "Assets" }).Take(maxAssetScans))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                GameObject prefabRoot = null;
                try
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    Component[] components = prefabRoot.GetComponentsInChildren<Component>(true)
                        .Where(component => component != null)
                        .ToArray();
                    string searchText = $"{Path.GetFileNameWithoutExtension(path)} {path} {string.Join(" ", components.Select(component => component.GetType().Name))}";
                    double score = Math.Max(0.35, ScoreText(query, searchText));
                    if (score <= 0 && !string.IsNullOrWhiteSpace(query))
                        continue;

                    yield return new Candidate
                    {
                        resultKind = "prefab",
                        provider = "prefab",
                        name = Path.GetFileNameWithoutExtension(path),
                        displayName = Path.GetFileNameWithoutExtension(path),
                        assetPath = path,
                        guid = guid,
                        confidence = score,
                        reason = "Prefab asset can be reused or instantiated before writing a script.",
                        serializedSchemaAvailable = components.Length > 0,
                        componentTypes = components.Select(component => component.GetType().FullName).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray()
                    };
                }
                finally
                {
                    if (prefabRoot != null)
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        IEnumerable<Candidate> SearchPresetAssets(string query, string[] providerFilter, int maxAssetScans)
        {
            if (!MatchesProviderFilter("preset", providerFilter))
                yield break;

            string filter = string.IsNullOrWhiteSpace(query) ? "t:Preset" : $"{query} t:Preset";
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { "Assets", "Packages" }).Take(maxAssetScans))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                UnityEngine.Object preset = AssetDatabase.LoadMainAssetAtPath<UnityEngine.Object>(path);
                string targetTypeName = TryReadPresetTargetTypeName(preset);
                string searchText = $"{Path.GetFileNameWithoutExtension(path)} {path} {targetTypeName}";
                double score = Math.Max(0.32, ScoreText(query, searchText));
                if (score <= 0 && !string.IsNullOrWhiteSpace(query))
                    continue;

                yield return new Candidate
                {
                    resultKind = "preset",
                    provider = "preset",
                    name = Path.GetFileNameWithoutExtension(path),
                    displayName = Path.GetFileNameWithoutExtension(path),
                    assetPath = path,
                    guid = guid,
                    typeName = targetTypeName,
                    confidence = score,
                    reason = "Preset can copy known serialized defaults onto compatible components.",
                    serializedSchemaAvailable = !string.IsNullOrWhiteSpace(targetTypeName),
                    componentTypes = string.IsNullOrWhiteSpace(targetTypeName) ? Array.Empty<string>() : new[] { targetTypeName }
                };
            }
        }

        IEnumerable<Candidate> SearchMissingPackages(string query, string[] providerFilter)
        {
            if (!MatchesProviderFilter("missing package", providerFilter))
                yield break;

            foreach (CapabilityDefinition capability in k_Capabilities)
            {
                double score = ScoreCapability(query, capability, out _);
                if (score <= 0 && !string.IsNullOrWhiteSpace(query))
                    continue;

                foreach (MissingPackageSpec missing in capability.missingPackages)
                {
                    if (IsPackageInstalled(missing.packageId))
                        continue;

                    yield return BuildMissingPackageCandidate(missing, Math.Max(0.35, score), missing.fallbackPlan);
                }
            }
        }

        Candidate BuildTypeCandidate(Type type, string query, double score, string reason)
        {
            ProviderInfo provider = DescribeProvider(type);
            return new Candidate
            {
                resultKind = "component",
                provider = provider.provider,
                name = type.Name,
                displayName = ObjectNames.NicifyVariableName(type.Name),
                typeName = type.FullName,
                assemblyName = provider.assemblyName,
                assetPath = provider.assetPath,
                packageId = provider.packageId,
                packageName = provider.packageName,
                packageVersion = provider.packageVersion,
                confidence = Round(score),
                reason = reason,
                serializedSchemaAvailable = !type.IsAbstract && !type.ContainsGenericParameters,
                matchedTerms = MatchedTerms(query, TypeSearchText(type)),
                setupRequirements = BuildSetupRequirements(type, provider)
            };
        }

        static Candidate BuildMissingPackageCandidate(MissingPackageSpec spec, double confidence, string reason)
        {
            return new Candidate
            {
                resultKind = "package",
                provider = "missing package",
                name = spec.packageName,
                displayName = spec.packageName,
                packageId = spec.packageId,
                packageName = spec.packageName,
                recommendedVersion = spec.recommendedVersion,
                compatibility = spec.compatibility,
                installRisk = spec.installRisk,
                compileImportImpact = spec.compileImportImpact,
                fallbackPlan = spec.fallbackPlan,
                confidence = Round(confidence),
                reason = reason,
                serializedSchemaAvailable = false,
                setupRequirements = new[] { "Preview package installation and compile/import impact before installing." }
            };
        }

        static object ToCandidateData(Candidate candidate)
        {
            if (candidate == null)
                return null;

            return new
            {
                candidate.resultKind,
                candidate.provider,
                candidate.name,
                candidate.displayName,
                candidate.typeName,
                candidate.assemblyName,
                candidate.assetPath,
                candidate.guid,
                candidate.packageId,
                candidate.packageName,
                candidate.packageVersion,
                candidate.recommendedVersion,
                candidate.compatibility,
                candidate.installRisk,
                candidate.compileImportImpact,
                candidate.fallbackPlan,
                confidence = Round(candidate.confidence),
                candidate.reason,
                candidate.serializedSchemaAvailable,
                candidate.setupRequirements,
                candidate.matchedTerms,
                candidate.componentTypes
            };
        }

        static IEnumerable<Candidate> RehydrateCandidates(object searchResult)
        {
            var root = Newtonsoft.Json.Linq.JObject.FromObject(searchResult ?? new { });
            foreach (Newtonsoft.Json.Linq.JObject item in root["results"]?.Children<Newtonsoft.Json.Linq.JObject>() ?? Enumerable.Empty<Newtonsoft.Json.Linq.JObject>())
            {
                yield return new Candidate
                {
                    resultKind = item.Value<string>("resultKind"),
                    provider = item.Value<string>("provider"),
                    name = item.Value<string>("name"),
                    displayName = item.Value<string>("displayName"),
                    typeName = item.Value<string>("typeName"),
                    assemblyName = item.Value<string>("assemblyName"),
                    assetPath = item.Value<string>("assetPath"),
                    guid = item.Value<string>("guid"),
                    packageId = item.Value<string>("packageId"),
                    packageName = item.Value<string>("packageName"),
                    packageVersion = item.Value<string>("packageVersion"),
                    recommendedVersion = item.Value<string>("recommendedVersion"),
                    compatibility = item.Value<string>("compatibility"),
                    installRisk = item.Value<string>("installRisk"),
                    compileImportImpact = item.Value<string>("compileImportImpact"),
                    fallbackPlan = item.Value<string>("fallbackPlan"),
                    confidence = item.Value<double?>("confidence") ?? 0,
                    reason = item.Value<string>("reason"),
                    serializedSchemaAvailable = item.Value<bool?>("serializedSchemaAvailable") ?? false,
                    setupRequirements = item["setupRequirements"]?.Values<string>().ToArray() ?? Array.Empty<string>(),
                    matchedTerms = item["matchedTerms"]?.Values<string>().ToArray() ?? Array.Empty<string>(),
                    componentTypes = item["componentTypes"]?.Values<string>().ToArray() ?? Array.Empty<string>()
                };
            }
        }

        static IEnumerable<Candidate> DeduplicateCandidates(IEnumerable<Candidate> candidates)
        {
            var byKey = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            foreach (Candidate candidate in candidates.Where(candidate => candidate != null))
            {
                string key = FirstNonEmpty(candidate.typeName, candidate.assetPath, candidate.packageId, candidate.name);
                if (string.IsNullOrWhiteSpace(key))
                    key = Guid.NewGuid().ToString("N");

                if (!byKey.TryGetValue(key, out Candidate existing) || candidate.confidence > existing.confidence)
                    byKey[key] = candidate;
            }

            return byKey.Values;
        }

        Type[] ResolveSceneCandidateTypes(string componentName, string intent, string query)
        {
            var types = new List<Type>();
            if (!string.IsNullOrWhiteSpace(componentName) && UnityComponentResolver.TryResolve(componentName, out Type explicitType, out _))
                types.Add(explicitType);

            string intentText = FirstNonEmpty(intent, query);
            if (!string.IsNullOrWhiteSpace(intentText))
            {
                var resolved = ResolveCapabilityData(new ComponentResolveCapabilityRequest
                {
                    intent = intentText,
                    includePrefabs = false,
                    includePresets = false,
                    includeMissingPackages = false,
                    maxResults = 16
                });

                foreach (Candidate candidate in resolved.results)
                {
                    if (!string.IsNullOrWhiteSpace(candidate.typeName) &&
                        UnityComponentResolver.TryResolve(candidate.typeName, out Type type, out _) &&
                        !types.Contains(type))
                    {
                        types.Add(type);
                    }
                }
            }

            return types.ToArray();
        }

        double ScoreSceneComponent(Type componentType, string query, Type[] candidateTypes, out string matchKind, out string reason)
        {
            matchKind = "text";
            reason = "Component name matched query.";
            if (candidateTypes != null)
            {
                foreach (Type candidateType in candidateTypes)
                {
                    if (candidateType != null && candidateType.IsAssignableFrom(componentType))
                    {
                        matchKind = "capability_component";
                        reason = "Component matched a resolved capability candidate.";
                        return 0.92;
                    }
                }
            }

            double score = ScoreText(query, TypeSearchText(componentType));
            if (score > 0)
                return Math.Max(0.25, score);

            return 0;
        }

        object BuildSceneComponentMatch(Component component, double confidence, string matchKind, string reason, string[] propertyPaths)
        {
            GameObject gameObject = component.gameObject;
            ProviderInfo provider = DescribeProvider(component.GetType());
            return new
            {
                objectPath = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                objectName = gameObject.name,
                objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                sceneName = gameObject.scene.name,
                scenePath = gameObject.scene.path,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                component = new
                {
                    typeName = component.GetType().FullName,
                    shortTypeName = component.GetType().Name,
                    instanceID = component.GetInstanceID(),
                    enabled = TryReadEnabled(component),
                    provider = provider.provider,
                    provider.packageId,
                    provider.packageName,
                    provider.assetPath,
                    serializedSchemaAvailable = true
                },
                confidence = Round(confidence),
                matchKind,
                reason,
                fieldValues = ReadPropertyValues(component, propertyPaths)
            };
        }

        object[] ReadPropertyValues(Component component, string[] propertyPaths)
        {
            if (propertyPaths == null || propertyPaths.Length == 0)
                return Array.Empty<object>();

            var rows = new List<object>();
            using (SerializedObject serializedObject = new SerializedObject(component))
            {
                foreach (string propertyPath in propertyPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
                {
                    SerializedProperty property = serializedObject.FindProperty(propertyPath);
                    rows.Add(property == null
                        ? new { propertyPath, resolved = false, error = "property_not_found" }
                        : new
                        {
                            propertyPath,
                            resolved = true,
                            propertyType = property.propertyType.ToString(),
                            valueType = property.type,
                            value = ReadSerializedPropertyValue(property)
                        });
                }
            }

            return rows.ToArray();
        }

        object[] ReadSerializedSchema(Component component, bool includeDefaults, bool includeReadOnly, int maxFields, out int totalFieldCount, out int omittedFieldCount, List<string> warnings)
        {
            var fields = new List<object>();
            totalFieldCount = 0;
            omittedFieldCount = 0;

            try
            {
                using (SerializedObject serializedObject = new SerializedObject(component))
                {
                    SerializedProperty iterator = serializedObject.GetIterator();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (string.Equals(iterator.name, "m_Script", StringComparison.Ordinal))
                            continue;

                        if (!includeReadOnly && !iterator.editable)
                            continue;

                        bool isArray = IsArrayProperty(iterator);
                        if (iterator.propertyType == SerializedPropertyType.Generic && iterator.hasVisibleChildren && !isArray)
                        {
                            enterChildren = true;
                            continue;
                        }

                        totalFieldCount++;
                        if (fields.Count >= maxFields)
                        {
                            omittedFieldCount++;
                            continue;
                        }

                        SerializedProperty copy = iterator.Copy();
                        fields.Add(BuildSchemaField(copy, includeDefaults));
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Serialized schema read failed: {ex.Message}");
            }

            return fields.ToArray();
        }

        static object BuildSchemaField(SerializedProperty property, bool includeDefaults)
        {
            var data = new Dictionary<string, object>
            {
                ["path"] = property.propertyPath,
                ["displayName"] = property.displayName,
                ["name"] = property.name,
                ["propertyType"] = property.propertyType.ToString(),
                ["valueType"] = property.type,
                ["editable"] = property.editable,
                ["isArray"] = IsArrayProperty(property),
                ["hasVisibleChildren"] = property.hasVisibleChildren,
                ["objectReferenceType"] = property.propertyType == SerializedPropertyType.ObjectReference
                    ? ExtractObjectReferenceTypeName(property.type)
                    : null
            };

            if (property.propertyType == SerializedPropertyType.Enum)
                data["enumDisplayNames"] = property.enumDisplayNames ?? Array.Empty<string>();

            if (includeDefaults)
                data["defaultValue"] = ReadSerializedPropertyValue(property);

            return data;
        }

        static bool TryCreateTemporaryComponent(Type componentType, out GameObject gameObject, out Component component, out string warning)
        {
            gameObject = null;
            component = null;
            warning = null;

            try
            {
                if (componentType == typeof(RectTransform))
                    gameObject = new GameObject("LensComponentSchemaProbe", typeof(RectTransform));
                else
                    gameObject = new GameObject("LensComponentSchemaProbe");

                gameObject.hideFlags = HideFlags.HideAndDontSave;
                if (componentType == typeof(Transform))
                {
                    component = gameObject.transform;
                    return true;
                }

                component = gameObject.GetComponent(componentType);
                if (component == null)
                    component = gameObject.AddComponent(componentType);

                if (component == null)
                {
                    warning = $"Unity returned null when adding '{componentType.FullName}'.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                if (gameObject != null)
                    UnityEngine.Object.DestroyImmediate(gameObject);

                gameObject = null;
                component = null;
                warning = $"Could not create temporary component '{componentType.FullName}' for schema inspection: {ex.Message}";
                return false;
            }
        }

        ProviderInfo DescribeProvider(Type type)
        {
            string assemblyName = type?.Assembly.GetName().Name;
            string scriptPath = TryGetScriptPath(type);
            PackageInfo package = TryFindPackage(type, scriptPath);

            if (!string.IsNullOrWhiteSpace(scriptPath) && scriptPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return new ProviderInfo
                {
                    provider = "project script",
                    assemblyName = assemblyName,
                    assetPath = scriptPath
                };
            }

            if (package != null)
            {
                return new ProviderInfo
                {
                    provider = "installed package",
                    assemblyName = assemblyName,
                    assetPath = scriptPath,
                    packageId = package.name,
                    packageName = package.displayName,
                    packageVersion = package.version
                };
            }

            var knownPackage = TryFindKnownPackageForAssembly(assemblyName);
            if (knownPackage != null)
            {
                return new ProviderInfo
                {
                    provider = "installed package",
                    assemblyName = assemblyName,
                    assetPath = scriptPath,
                    packageId = knownPackage.name,
                    packageName = knownPackage.displayName,
                    packageVersion = knownPackage.version
                };
            }

            if (!string.IsNullOrWhiteSpace(scriptPath) && scriptPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return new ProviderInfo
                {
                    provider = "installed package",
                    assemblyName = assemblyName,
                    assetPath = scriptPath,
                    packageId = ResolvePackageIdFromAssetPath(scriptPath)
                };
            }

            if (IsBuiltInUnityType(type, assemblyName))
            {
                return new ProviderInfo
                {
                    provider = "built-in",
                    assemblyName = assemblyName
                };
            }

            return new ProviderInfo
            {
                provider = "project script",
                assemblyName = assemblyName,
                assetPath = scriptPath
            };
        }

        PackageInfo TryFindPackage(Type type, string scriptPath)
        {
            if (type == null)
                return null;

            try
            {
                s_FindPackageForAssemblyMethod ??= typeof(PackageInfo).GetMethod("FindForAssembly", BindingFlags.Public | BindingFlags.Static);
                if (s_FindPackageForAssemblyMethod != null)
                {
                    var package = s_FindPackageForAssemblyMethod.Invoke(null, new object[] { type.Assembly }) as PackageInfo;
                    if (package != null)
                        return package;
                }
            }
            catch
            {
                // Fall through to asset-path/package-list matching for older editor/package APIs.
            }

            if (!string.IsNullOrWhiteSpace(scriptPath))
            {
                foreach (PackageInfo package in GetPackages())
                {
                    string assetPath = (package.assetPath ?? string.Empty).Replace('\\', '/').TrimEnd('/');
                    if (!string.IsNullOrWhiteSpace(assetPath) &&
                        scriptPath.StartsWith(assetPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        return package;
                    }

                    if (scriptPath.StartsWith("Packages/" + package.name + "/", StringComparison.OrdinalIgnoreCase))
                        return package;
                }
            }

            return null;
        }

        static PackageInfo TryFindKnownPackageForAssembly(string assemblyName)
        {
            string packageId = assemblyName switch
            {
                "Unity.Cinemachine" or "Cinemachine" => "com.unity.cinemachine",
                "Unity.InputSystem" => "com.unity.inputsystem",
                "Unity.TextMeshPro" => "com.unity.textmeshpro",
                "UnityEngine.UI" => "com.unity.ugui",
                "Unity.AI.Navigation" => "com.unity.ai.navigation",
                "Unity.Timeline" => "com.unity.timeline",
                "Unity.RenderPipelines.Universal.Runtime" => "com.unity.render-pipelines.universal",
                _ => null
            };

            return string.IsNullOrWhiteSpace(packageId)
                ? null
                : GetPackages().FirstOrDefault(package => string.Equals(package.name, packageId, StringComparison.OrdinalIgnoreCase));
        }

        static PackageInfo[] GetPackages()
        {
            if (s_Packages != null)
                return s_Packages;

            try
            {
                s_Packages = PackageInfo.GetAllRegisteredPackages() ?? Array.Empty<PackageInfo>();
            }
            catch
            {
                s_Packages = Array.Empty<PackageInfo>();
            }

            return s_Packages;
        }

        static bool IsPackageInstalled(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) &&
                GetPackages().Any(package => string.Equals(package.name, packageId, StringComparison.OrdinalIgnoreCase));
        }

        static string ResolvePackageIdFromAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return null;

            string remainder = assetPath.Substring("Packages/".Length);
            int slash = remainder.IndexOf('/');
            return slash > 0 ? remainder.Substring(0, slash) : remainder;
        }

        static bool IsBuiltInUnityType(Type type, string assemblyName)
        {
            return type != null &&
                ((assemblyName?.StartsWith("UnityEngine.", StringComparison.Ordinal) ?? false) ||
                 string.Equals(assemblyName, "UnityEngine", StringComparison.Ordinal) ||
                 (type.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) ?? false));
        }

        static string TryGetScriptPath(Type type)
        {
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
                return null;

            s_ScriptPathByType ??= BuildScriptPathCache();
            return s_ScriptPathByType.TryGetValue(type, out string path) ? path : null;
        }

        static Dictionary<Type, string> BuildScriptPathCache()
        {
            var results = new Dictionary<Type, string>();
            try
            {
                foreach (MonoScript script in MonoImporter.GetAllRuntimeMonoScripts())
                {
                    Type type = null;
                    try
                    {
                        type = script.GetClass();
                    }
                    catch
                    {
                        // Ignore scripts Unity cannot load into a class.
                    }

                    if (type == null || !typeof(Component).IsAssignableFrom(type))
                        continue;

                    string path = AssetDatabase.GetAssetPath(script);
                    if (!string.IsNullOrWhiteSpace(path) && !results.ContainsKey(type))
                        results[type] = path.Replace('\\', '/');
                }
            }
            catch
            {
                // MonoScript enumeration can fail during reload; provider fallback still works.
            }

            return results;
        }

        static object DescribeComponentType(Type type, ProviderInfo provider)
        {
            return new
            {
                name = type.Name,
                displayName = ObjectNames.NicifyVariableName(type.Name),
                typeName = type.FullName,
                provider = provider.provider,
                provider.assemblyName,
                provider.assetPath,
                provider.packageId,
                provider.packageName,
                provider.packageVersion,
                addable = !type.IsAbstract && !type.ContainsGenericParameters,
                isMonoBehaviour = typeof(MonoBehaviour).IsAssignableFrom(type),
                isBuiltIn = string.Equals(provider.provider, "built-in", StringComparison.OrdinalIgnoreCase)
            };
        }

        static object DescribeGameObject(GameObject gameObject)
        {
            return gameObject == null ? null : new
            {
                name = gameObject.name,
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                sceneName = gameObject.scene.name,
                scenePath = gameObject.scene.path
            };
        }

        static string[] BuildSetupRequirements(Type type, ProviderInfo provider)
        {
            var requirements = new List<string>();
            if (!string.Equals(provider.provider, "built-in", StringComparison.OrdinalIgnoreCase))
                requirements.Add("Ensure the provider assembly/package is present and scripts compile.");
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
                requirements.Add("Inspect serialized schema before setting fields.");
            if (typeof(Behaviour).IsAssignableFrom(type))
                requirements.Add("Check enabled state after authoring.");
            return requirements.Distinct().ToArray();
        }

        static (CapabilityDefinition definition, double score, string[] matchedTerms)[] MatchCapabilities(string intent)
        {
            return k_Capabilities
                .Select(definition =>
                {
                    double score = ScoreCapability(intent, definition, out string[] matchedTerms);
                    return (definition, score, matchedTerms);
                })
                .Where(match => match.score > 0)
                .OrderByDescending(match => match.score)
                .ToArray();
        }

        static double ScoreCapability(string query, CapabilityDefinition definition, out string[] matchedTerms)
        {
            matchedTerms = Array.Empty<string>();
            if (definition == null)
                return 0;
            if (string.IsNullOrWhiteSpace(query))
                return 0.1;

            string normalizedQuery = NormalizeText(query);
            var matches = definition.terms
                .Where(term => normalizedQuery.Contains(NormalizeText(term)) || TermTokens(term).All(token => normalizedQuery.Contains(token)))
                .ToArray();
            matchedTerms = matches;
            if (matches.Length > 0)
                return Math.Min(0.96, 0.65 + matches.Max(term => Math.Min(0.3, term.Length / 80.0)));

            string joinedTerms = string.Join(" ", definition.terms);
            return ScoreText(query, joinedTerms) * 0.8;
        }

        static double ScoreText(string query, string searchText)
        {
            if (string.IsNullOrWhiteSpace(query))
                return 0.25;
            if (string.IsNullOrWhiteSpace(searchText))
                return 0;

            string normalizedQuery = NormalizeText(query);
            string normalizedText = NormalizeText(searchText);
            if (normalizedText.Contains(normalizedQuery))
                return 0.95;

            string[] tokens = TermTokens(query);
            if (tokens.Length == 0)
                return 0;

            int matched = tokens.Count(token => normalizedText.Contains(token));
            if (matched == 0)
                return 0;

            double coverage = matched / (double)tokens.Length;
            return Math.Round(Math.Min(0.9, 0.25 + coverage * 0.55), 3);
        }

        static string[] MatchedTerms(string query, string searchText)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(searchText))
                return Array.Empty<string>();

            string normalizedText = NormalizeText(searchText);
            return TermTokens(query)
                .Where(token => normalizedText.Contains(token))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        static string TypeSearchText(Type type)
        {
            if (type == null)
                return string.Empty;

            return $"{type.Name} {ObjectNames.NicifyVariableName(type.Name)} {type.FullName} {type.Assembly.GetName().Name}";
        }

        static bool IsDiscoverableComponentType(Type type)
        {
            return type != null &&
                typeof(Component).IsAssignableFrom(type) &&
                !type.IsAbstract &&
                !type.ContainsGenericParameters &&
                (type.IsPublic || type.IsNestedPublic);
        }

        static string[] NormalizeProviderFilter(IEnumerable<string> providers)
        {
            return (providers ?? Array.Empty<string>())
                .Where(provider => !string.IsNullOrWhiteSpace(provider))
                .Select(provider => provider.Trim().Replace("_", " ").Replace("-", " ").ToLowerInvariant())
                .Where(provider => provider != "all")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static bool MatchesProviderFilter(string provider, string[] providerFilter)
        {
            if (providerFilter == null || providerFilter.Length == 0)
                return true;

            string normalized = (provider ?? string.Empty).Trim().Replace("_", " ").Replace("-", " ").ToLowerInvariant();
            return providerFilter.Any(filter => string.Equals(filter, normalized, StringComparison.OrdinalIgnoreCase));
        }

        static string NormalizeText(string value)
        {
            return (value ?? string.Empty)
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Replace('.', ' ')
                .ToLowerInvariant();
        }

        static string[] TermTokens(string value)
        {
            return NormalizeText(value)
                .Split(new[] { ' ', '/', '\\', ':', ';', ',', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 1)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        static bool MatchesScene(GameObject gameObject, string scene)
        {
            return gameObject != null &&
                (string.IsNullOrWhiteSpace(scene) ||
                    string.Equals(gameObject.scene.name, scene, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(gameObject.scene.path, scene, StringComparison.OrdinalIgnoreCase));
        }

        static bool? TryReadEnabled(Component component)
        {
            return component switch
            {
                Behaviour behaviour => behaviour.enabled,
                Renderer renderer => renderer.enabled,
                Collider collider => collider.enabled,
                Collider2D collider2D => collider2D.enabled,
                _ => null
            };
        }

        static bool IsArrayProperty(SerializedProperty property)
        {
            return property != null && property.isArray && property.propertyType == SerializedPropertyType.Generic;
        }

        static object ReadSerializedPropertyValue(SerializedProperty property)
        {
            try
            {
                return property.propertyType switch
                {
                    SerializedPropertyType.Integer or SerializedPropertyType.LayerMask or SerializedPropertyType.Character or SerializedPropertyType.ArraySize or SerializedPropertyType.FixedBufferSize => property.intValue,
                    SerializedPropertyType.Boolean => property.boolValue,
                    SerializedPropertyType.Float => property.floatValue,
                    SerializedPropertyType.String => property.stringValue,
                    SerializedPropertyType.Color => new { r = property.colorValue.r, g = property.colorValue.g, b = property.colorValue.b, a = property.colorValue.a },
                    SerializedPropertyType.ObjectReference or SerializedPropertyType.ExposedReference => DescribeUnityObject(property.objectReferenceValue),
                    SerializedPropertyType.Enum => new
                    {
                        enumValueIndex = property.enumValueIndex,
                        enumValue = property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                            ? property.enumDisplayNames[property.enumValueIndex]
                            : null
                    },
                    SerializedPropertyType.Vector2 => new { x = property.vector2Value.x, y = property.vector2Value.y },
                    SerializedPropertyType.Vector3 => new { x = property.vector3Value.x, y = property.vector3Value.y, z = property.vector3Value.z },
                    SerializedPropertyType.Vector4 => new { x = property.vector4Value.x, y = property.vector4Value.y, z = property.vector4Value.z, w = property.vector4Value.w },
                    SerializedPropertyType.Rect => new { x = property.rectValue.x, y = property.rectValue.y, width = property.rectValue.width, height = property.rectValue.height },
                    SerializedPropertyType.Bounds => new { center = property.boundsValue.center.ToString("F3"), size = property.boundsValue.size.ToString("F3") },
                    SerializedPropertyType.Quaternion => new { x = property.quaternionValue.x, y = property.quaternionValue.y, z = property.quaternionValue.z, w = property.quaternionValue.w },
                    SerializedPropertyType.Vector2Int => new { x = property.vector2IntValue.x, y = property.vector2IntValue.y },
                    SerializedPropertyType.Vector3Int => new { x = property.vector3IntValue.x, y = property.vector3IntValue.y, z = property.vector3IntValue.z },
                    SerializedPropertyType.RectInt => new { x = property.rectIntValue.x, y = property.rectIntValue.y, width = property.rectIntValue.width, height = property.rectIntValue.height },
                    SerializedPropertyType.BoundsInt => new { center = property.boundsIntValue.center.ToString(), size = property.boundsIntValue.size.ToString() },
                    SerializedPropertyType.AnimationCurve => new { keyCount = property.animationCurveValue?.length ?? 0 },
                    SerializedPropertyType.ManagedReference => new { managedReferenceType = property.managedReferenceFullTypename },
                    SerializedPropertyType.Generic when IsArrayProperty(property) => new { kind = "array", size = property.arraySize },
                    _ => null
                };
            }
            catch (Exception ex)
            {
                return new { errorKind = ex.GetType().Name, error = ex.Message };
            }
        }

        static object DescribeUnityObject(UnityEngine.Object value)
        {
            if (value == null)
                return null;

            string assetPath = AssetDatabase.GetAssetPath(value);
            return new
            {
                name = value.name,
                type = value.GetType().FullName,
                instanceID = value.GetInstanceID(),
                assetPath = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath,
                guid = string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath)
            };
        }

        static string ExtractObjectReferenceTypeName(string serializedType)
        {
            if (string.IsNullOrWhiteSpace(serializedType))
                return null;

            const string prefix = "PPtr<$";
            if (serializedType.StartsWith(prefix, StringComparison.Ordinal) && serializedType.EndsWith(">", StringComparison.Ordinal))
                return serializedType.Substring(prefix.Length, serializedType.Length - prefix.Length - 1);

            return serializedType;
        }

        static string TryReadPresetTargetTypeName(UnityEngine.Object preset)
        {
            if (preset == null)
                return null;

            foreach (string methodName in new[] { "GetTargetFullTypeName", "GetTargetTypeName" })
            {
                try
                {
                    MethodInfo method = preset.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                    object value = method?.Invoke(preset, null);
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        return value.ToString();
                }
                catch
                {
                    // Try the next known API shape.
                }
            }

            return null;
        }

        static MissingPackageSpec CinemachinePackage(string fallbackPlan)
        {
            return new MissingPackageSpec
            {
                packageId = "com.unity.cinemachine",
                packageName = "Cinemachine",
                recommendedVersion = "project-compatible latest",
                compatibility = "Best checked through package preview; Cinemachine 3 uses Unity.Cinemachine types, older projects use Cinemachine.* types.",
                installRisk = "Low",
                compileImportImpact = "Package import and assembly refresh; existing camera scripts may compile against older Cinemachine APIs.",
                fallbackPlan = fallbackPlan
            };
        }

        static string[] MergeRequirements(params string[][] requirementSets)
        {
            return (requirementSets ?? Array.Empty<string[]>())
                .SelectMany(set => set ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        static object BuildInsufficiencyReport(string intent, IEnumerable<Candidate> candidates, int sceneMatchCount)
        {
            var candidateArray = (candidates ?? Array.Empty<Candidate>()).ToArray();
            return new
            {
                required = true,
                intent,
                checkedProviders = new[] { "built-in", "installed package", "project script", "prefab", "preset", "missing package", "scene" },
                sceneMatchCount,
                reusableCandidateCount = candidateArray.Count(candidate => !string.Equals(candidate.provider, "missing package", StringComparison.OrdinalIgnoreCase)),
                missingPackageCount = candidateArray.Count(candidate => string.Equals(candidate.provider, "missing package", StringComparison.OrdinalIgnoreCase)),
                reason = "No installed, project, prefab, preset, or existing scene component candidate met the reuse threshold. A custom script may be considered after reviewing missing package options.",
                nextChecks = new[]
                {
                    "Inspect relevant prefabs/presets if project naming is domain-specific.",
                    "Preview package installation when a missing package is a standard Unity solution.",
                    "Document why existing component schemas cannot represent the requested behavior before creating a script."
                }
            };
        }

        static int ExtractInt(object data, string propertyName)
        {
            try
            {
                return Newtonsoft.Json.Linq.JObject.FromObject(data ?? new { }).Value<int?>(propertyName) ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        static string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        static double Round(double value)
        {
            return Math.Round(Math.Clamp(value, 0, 1), 3);
        }
    }
}
