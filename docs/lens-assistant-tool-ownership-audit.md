# Lens Assistant Tool Ownership Audit

Lens owns MCP-first Unity editor capabilities. This audit maps the old Assistant tool surface into canonical Lens tools without restoring Assistant adapters, `AgentTool` discovery, or broad old-name aliases.

Status values:

- `covered`: Existing Lens capability already covers the old behavior.
- `ported`: Added or expanded a canonical Lens tool in this pass.
- `exclude/defer`: Intentionally not owned by Lens yet, or not a Unity MCP bridge responsibility.

| Old Assistant tool | Status | Canonical Lens surface | Notes |
| --- | --- | --- | --- |
| `Unity.GetProjectData` | ported | `Unity.Project.GetInfo` | Project summary, settings, dependencies, packages, and guidelines sections. |
| `Unity.GetProjectOverview` | ported | `Unity.Project.GetInfo` | Covered by `summary`, `settings`, and `packages`. |
| `Unity.GetUnityVersion` | ported | `Unity.Project.GetInfo` | Covered by `unityVersion`. |
| `Unity.GetProjectSettings` | ported | `Unity.Project.GetInfo` | Compact ProjectSettings preview. |
| `Unity.GetDependency` | ported | `Unity.Project.GetInfo` | Covered by dependencies manifest section. |
| `Unity.GetUnityDependenciesTool` | ported | `Unity.Project.GetInfo` | Covered by dependencies and registered package metadata. |
| `Unity.GetStaticProjectSettingsTool` | ported | `Unity.Project.GetInfo` | Covered by settings preview. |
| `Unity.GetUserGuidelines` | ported | `Unity.Project.GetInfo` | Compact settings-backed guideline lookup. |
| `Unity.PackageManager.GetData` | ported | `Unity.Project.GetPackages` | Read-only package listing. |
| `Unity.PackageManager.ExecuteAction` | ported | `Unity.Project.ManagePackages` | Full/admin pack only for add, remove, embed, resolve. |
| `Unity.FindFiles` | covered | `Unity.ListResources` | Resource listing keeps foundation read-only. |
| `Unity.GetFileContent` | covered | `Unity.ReadResource` | ReadResource owns file reads and compaction. |
| `Unity.GetTextAssetContent` | covered | `Unity.ReadResource` | Text assets are normal resource reads. |
| `Unity.GetImageAssetContent` | covered | `Unity.ReadResource` and `Unity.Asset.Search` | Metadata/search owned by asset search; binary inlining remains excluded. |
| `Unity.SaveFile` | ported | `Unity.Resource.Write` | Requires SHA precondition for overwrites. |
| `Unity.DeleteFile` | ported | `Unity.Resource.Delete` | Full/admin pack only. |
| `Unity.CodeEdit` | covered | `Unity.ApplyTextEdits` and `Unity.ScriptApplyEdits` | Keep canonical edit tools, no old alias. |
| `Unity.FindProjectAssets` | ported | `Unity.Asset.Search` | Read-only canonical asset search. |
| `Unity.GetAssetLabels` | ported | `Unity.Asset.Search` | Labels are included in search results. |
| `Unity.FindSceneObjects` | ported | `Unity.ManageScene` | `FindObjects` action. |
| `Unity.GetSceneInfo` | ported | `Unity.ManageScene` | `GetInfo` action. |
| `Unity.GetObjectData` | covered | `Unity.ManageGameObject` | `find`, `get_component`, and `get_components` actions. |
| `Unity.GameObject.CreateGameObject` | covered | `Unity.ManageGameObject` | `create` action. |
| `Unity.GameObject.ModifyGameObject` | covered | `Unity.ManageGameObject` | `modify` action. |
| `Unity.GameObject.RemoveGameObject` | covered | `Unity.ManageGameObject` | `delete` action. |
| `Unity.GameObject.AddComponent` | covered | `Unity.ManageGameObject` | `add_component` action. |
| `Unity.GameObject.RemoveComponent` | covered | `Unity.ManageGameObject` | `remove_component` action. |
| `Unity.GameObject.SetComponentProperty` | covered | `Unity.ManageGameObject` | `set_component_property` action. |
| `Unity.GameObject.GetComponentProperties` | covered | `Unity.ManageGameObject` | `get_component` and `get_components`. |
| `Unity.GameObject.GetSelection` | ported | `Unity.ManageGameObject` | `get_selection` action. |
| `Unity.GameObject.GetGameObjectBounds` | ported | `Unity.ManageGameObject` | `get_bounds` action. |
| `Unity.GameObject.GetBuiltinAssets` | ported | `Unity.ManageGameObject` | `get_builtin_assets` action. |
| `Unity.GameObject.ManageTag` | covered | `Unity.ManageEditor` | Tag actions stay on editor control surface. |
| `Unity.GameObject.ManageLayer` | covered | `Unity.ManageEditor` | Layer actions stay on editor control surface. |
| `Unity.GameObject.ManagePrefab` | covered | `Unity.ManageAsset` and `Unity.Prefab.SetSerializedProperties` | Prefab ownership remains asset/prefab-scoped. |
| `Unity.Camera.GetVisibleObjects` | ported | `Unity.ManageScene` | `GetVisibleObjects` action. |
| `Unity.Camera.Capture` | ported | `Unity.Scene.CaptureView` | Camera and game-view capture modes. |
| `Unity.SceneView.Capture2DScene` | ported | `Unity.Scene.CaptureView` | Scene-view capture mode. |
| `Unity.SceneView.CaptureMultiAngleSceneView` | ported | `Unity.Scene.CaptureView` | Multi-angle capture mode. |
| `Unity.FindPanelSettings` | ported | `Unity.UI.Toolkit` | `find_panel_settings` action. |
| `Unity.FindOrCreateDefaultPanelSettings` | ported | `Unity.UI.Toolkit` | `ensure_panel_settings` action. |
| `Unity.ValidateUIAsset` | ported | `Unity.UI.Toolkit` | `validate_asset` action. |
| `Unity.SaveAndValidateUIAsset` | ported | `Unity.UI.Toolkit` | `save_and_validate_asset` action. |
| `Unity.GenerateUxmlSchemas` | ported | `Unity.UI.Toolkit` | Reflection-backed when Unity exposes the API, compact unavailable result otherwise. |
| `Unity.GetUIAssetPreview` | ported | `Unity.UI.Toolkit` | `get_asset_preview` writes image artifact metadata. |
| `Unity.GetConsoleLogs` | covered | `Unity.ReadConsole` | No old alias. |
| `Unity.EnterPlayMode` | covered | `Unity.ManageEditor` | Play action. |
| `Unity.ExitPlayMode` | covered | `Unity.ManageEditor` | Stop action. |
| `Unity.RunCommand` | covered | `Unity.RunCommand` | MCP-owned support. |
| `Unity.RunCommandValidator` | ported | `Unity.RunCommand` | `mode: validate`. |
| `Unity.Skill.ReadSkillBody` | exclude/defer | none | Codex/Assistant skill concern, not Unity bridge ownership. |
| `Unity.Skill.ReadSkillResource` | exclude/defer | none | Codex/Assistant skill concern, not Unity bridge ownership. |
| `Unity.Web.Fetch` | exclude/defer | none | External MCP agents already have web access; Lens stays Unity-focused. |

Excluded Assistant areas not represented by public Lens tools in this pass:

- Cloud asset generation tools.
- Sample integration tools.
- Assistant profiler UI renderers, pick-session controls, and chat link handlers.
- Assistant UI, ACP, Gateway, and FunctionCalling dependencies.
