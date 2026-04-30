#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class UiFontTextTools
    {
        const string VisualTextAuditToolName = "Unity.UI.VisualTextAudit";
        const string PreviewImportAndBindToolName = "Unity.Font.PreviewImportAndBindUiFont";
        const string ApplyImportAndBindToolName = "Unity.Font.ApplyImportAndBindUiFont";

        const string VisualTextAuditDescription = @"Audits legacy uGUI Text components for visible text, font binding, alpha, active state, and simple clipping evidence.

Phase 19 v1 targets legacy UnityEngine.UI.Text. TextMeshPro rows are reported as unsupported instead of silently ignored.";

        const string PreviewImportAndBindDescription = @"Previews importing a legacy Font asset and binding it to legacy uGUI Text components without mutation.

TextMeshPro binding is explicitly unsupported in Phase 19 v1.";

        const string ApplyImportAndBindDescription = @"Imports a legacy Font asset if needed and binds it to legacy uGUI Text components in open scenes or prefab assets.

TextMeshPro binding is explicitly unsupported in Phase 19 v1.";

        [McpSchema(VisualTextAuditToolName)]
        public static object GetVisualTextAuditSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    prefabPath = new { type = "string", description = "Optional prefab asset path to audit. If omitted, open scenes are audited." },
                    target = new { type = "string", description = "Optional root GameObject name/path under which Text components are audited." },
                    searchMethod = new { type = "string", description = "by_name or by_path.", @default = "by_name" },
                    includeInactive = new { type = "boolean", description = "Include inactive objects.", @default = true },
                    maxItems = new { type = "integer", description = "Maximum issue rows to include inline.", @default = 50 },
                    includeDetails = new { type = "boolean", description = "Keep full rows behind detailRef when compacted.", @default = true }
                }
            };
        }

        [McpSchema(PreviewImportAndBindToolName)]
        public static object GetPreviewImportAndBindSchema()
        {
            return BuildImportAndBindSchema();
        }

        [McpSchema(ApplyImportAndBindToolName)]
        public static object GetApplyImportAndBindSchema()
        {
            return BuildImportAndBindSchema();
        }

        [McpTool(VisualTextAuditToolName, VisualTextAuditDescription, "Visual Text Audit", Groups = new[] { "ui" }, EnabledByDefault = true)]
        public static object VisualTextAudit(JObject parameters)
        {
            parameters ??= new JObject();
            var timing = new ToolOperationTiming(VisualTextAuditToolName, "visual_text_audit", GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            object data;
            bool success = true;
            string message = "Audited UI text visibility.";
            string errorKind = null;

            try
            {
                UiTextAuditRequest request;
                using (timing.Measure("normalization"))
                    request = NormalizeAuditRequest(parameters);

                using (timing.Measure("service"))
                {
                    // Keep service and adapter stages distinct for TSAM coverage.
                }

                using (timing.Measure("adapter"))
                    data = BuildAuditData(request);
            }
            catch (Exception ex)
            {
                success = false;
                message = $"Visual text audit failed: {ex.Message}";
                errorKind = ex.GetType().Name;
                data = new { errorKind };
            }

            return ShapeResponse(VisualTextAuditToolName, success, message, data, timing, errorKind);
        }

        [McpTool(PreviewImportAndBindToolName, PreviewImportAndBindDescription, "Preview Import And Bind UI Font", Groups = new[] { "ui", "assets" }, EnabledByDefault = true)]
        public static object PreviewImportAndBindUiFont(JObject parameters)
        {
            return HandleImportAndBind(PreviewImportAndBindToolName, "preview_import_bind_ui_font", parameters, apply: false);
        }

        [McpTool(ApplyImportAndBindToolName, ApplyImportAndBindDescription, "Apply Import And Bind UI Font", Groups = new[] { "ui", "assets" }, EnabledByDefault = true)]
        public static object ApplyImportAndBindUiFont(JObject parameters)
        {
            return HandleImportAndBind(ApplyImportAndBindToolName, "apply_import_bind_ui_font", parameters, apply: true);
        }

        static object HandleImportAndBind(string toolName, string action, JObject parameters, bool apply)
        {
            parameters ??= new JObject();
            var timing = new ToolOperationTiming(toolName, action, GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            object data;
            bool success = true;
            string message = apply ? "Applied UI font binding." : "Previewed UI font binding.";
            string errorKind = null;

            try
            {
                UiFontBindRequest request;
                using (timing.Measure("normalization"))
                    request = NormalizeFontBindRequest(parameters);

                using (timing.Measure("service"))
                {
                    // Keep service and adapter stages distinct for TSAM coverage.
                }

                using (timing.Measure("adapter"))
                    data = BuildFontBindData(request, apply);
            }
            catch (Exception ex)
            {
                success = false;
                message = $"UI font binding failed: {ex.Message}";
                errorKind = ex.GetType().Name;
                data = new { errorKind };
            }

            return ShapeResponse(toolName, success, message, data, timing, errorKind);
        }

        static object BuildImportAndBindSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    fontAssetPath = new { type = "string", description = "Project-relative Font asset path, for example Assets/Fonts/NotoSans-Regular.ttf." },
                    targets = new
                    {
                        type = "array",
                        description = "Optional scene/prefab target roots. If omitted, all open-scene legacy Text components are considered.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                assetPath = new { type = "string", description = "Optional prefab asset path. If omitted, open scenes are searched." },
                                target = new { type = "string", description = "Root GameObject name/path." },
                                searchMethod = new { type = "string", description = "by_name or by_path.", @default = "by_name" },
                                targetPath = new { type = "string", description = "Relative child path under the root target.", @default = "." },
                                includeInactive = new { type = "boolean", description = "Include inactive objects.", @default = true }
                            }
                        }
                    },
                    includeInactive = new { type = "boolean", description = "Default includeInactive value for targets.", @default = true }
                },
                required = new[] { "fontAssetPath" }
            };
        }

        static object BuildAuditData(UiTextAuditRequest request)
        {
            var rows = new List<object>();
            var issues = new List<object>();
            int tmpUnsupportedCount = 0;
            int textCount = 0;

            foreach (var scope in ResolveScopes(request.PrefabPath))
            {
                try
                {
                    foreach (var root in ResolveRoots(scope.Root, request.Target, request.SearchMethod, request.IncludeInactive))
                    {
                        foreach (var tmp in root.GetComponentsInChildren<Component>(request.IncludeInactive).Where(IsTmpTextComponent))
                        {
                            tmpUnsupportedCount++;
                            issues.Add(new { code = "tmp_unsupported", severity = "warning", path = GetPath(tmp.transform), message = "TextMeshPro audit is unsupported in Phase 19 v1." });
                        }

                        foreach (var text in root.GetComponentsInChildren<Text>(request.IncludeInactive))
                        {
                            textCount++;
                            var row = BuildTextAuditRow(text);
                            rows.Add(row);
                            foreach (var issue in BuildTextIssues(text, row))
                                issues.Add(issue);
                        }
                    }
                }
                finally
                {
                    scope.Dispose();
                }
            }

            bool passed = !issues.Any(issue => !string.Equals((string)JObject.FromObject(issue)["severity"], "info", StringComparison.OrdinalIgnoreCase));
            return new
            {
                passed,
                textCount,
                tmpUnsupportedCount,
                issueCount = issues.Count,
                issues = issues.Take(request.MaxItems).ToArray(),
                omittedIssueCount = Math.Max(0, issues.Count - request.MaxItems),
                rows
            };
        }

        static object BuildFontBindData(UiFontBindRequest request, bool apply)
        {
            if (string.IsNullOrWhiteSpace(request.FontAssetPath))
                throw new ArgumentException("fontAssetPath is required.");

            string fontPath = NormalizeAssetPath(request.FontAssetPath);
            if (apply && System.IO.File.Exists(fontPath))
                AssetDatabase.ImportAsset(fontPath, ImportAssetOptions.ForceUpdate);

            Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            bool fontExists = font != null;
            var rows = new List<object>();
            var issues = new List<object>();
            bool willModify = false;
            bool applied = false;

            if (!fontExists)
                issues.Add(new { code = "font_not_found", severity = "error", message = $"Font asset '{fontPath}' could not be loaded as UnityEngine.Font." });

            var targets = request.Targets.Length == 0
                ? new[] { new UiFontBindTarget { IncludeInactive = request.IncludeInactive } }
                : request.Targets;

            foreach (var target in targets)
            {
                foreach (var scope in ResolveScopes(target.AssetPath))
                {
                    bool scopeChanged = false;
                    try
                    {
                        foreach (var root in ResolveRoots(scope.Root, target.Target, target.SearchMethod ?? "by_name", target.IncludeInactive ?? request.IncludeInactive))
                        {
                            var bindRoot = ResolveTargetPath(root, target.TargetPath, target.IncludeInactive ?? request.IncludeInactive);
                            if (bindRoot == null)
                            {
                                issues.Add(new { code = "target_path_not_found", severity = "error", path = target.TargetPath, message = "Target path was not found under the resolved root." });
                                continue;
                            }

                            foreach (var tmp in bindRoot.GetComponentsInChildren<Component>(target.IncludeInactive ?? request.IncludeInactive).Where(IsTmpTextComponent))
                            {
                                issues.Add(new { code = "tmp_unsupported", severity = "warning", path = GetPath(tmp.transform), message = "TextMeshPro font binding is unsupported in Phase 19 v1." });
                            }

                            foreach (var text in bindRoot.GetComponentsInChildren<Text>(target.IncludeInactive ?? request.IncludeInactive))
                            {
                                string currentPath = text.font != null ? AssetDatabase.GetAssetPath(text.font) : null;
                                bool rowWillModify = fontExists && !string.Equals(currentPath, fontPath, StringComparison.OrdinalIgnoreCase);
                                willModify |= rowWillModify;
                                if (apply && rowWillModify)
                                {
                                    Undo.RecordObject(text, "Bind UI Font");
                                    text.font = font;
                                    EditorUtility.SetDirty(text);
                                    scopeChanged = true;
                                    applied = true;
                                }

                                rows.Add(new
                                {
                                    scope = scope.ScopePath,
                                    path = GetPath(text.transform),
                                    text = text.text,
                                    currentFontPath = currentPath,
                                    requestedFontPath = fontPath,
                                    willModify = rowWillModify,
                                    applied = apply && rowWillModify
                                });
                            }
                        }

                        if (apply && scopeChanged)
                            scope.Save();
                    }
                    finally
                    {
                        scope.Dispose();
                    }
                }
            }

            if (apply && applied)
                AssetDatabase.SaveAssets();

            return new
            {
                fontAssetPath = fontPath,
                fontExists,
                apply,
                applied,
                willModify,
                bindingCount = rows.Count,
                changedBindingCount = rows.Count(row => JObject.FromObject(row)["willModify"]?.Value<bool>() == true),
                rows,
                issues
            };
        }

        static object BuildTextAuditRow(Text text)
        {
            RectTransform rect = text.rectTransform;
            Rect localRect = rect.rect;
            float effectiveAlpha = text.color.a * (text.canvasRenderer != null ? text.canvasRenderer.GetAlpha() : 1f);
            string fontPath = text.font != null ? AssetDatabase.GetAssetPath(text.font) : null;
            bool hasText = !string.IsNullOrWhiteSpace(text.text);
            bool hasPositiveRect = localRect.width > 0.5f && localRect.height > 0.5f;
            bool visible = text.isActiveAndEnabled && text.gameObject.activeInHierarchy && hasText && text.font != null && effectiveAlpha > 0.01f && hasPositiveRect;
            return new
            {
                path = GetPath(text.transform),
                text = text.text,
                activeInHierarchy = text.gameObject.activeInHierarchy,
                enabled = text.enabled,
                visible,
                alpha = effectiveAlpha,
                fontName = text.font != null ? text.font.name : null,
                fontPath,
                rect = new { x = localRect.x, y = localRect.y, width = localRect.width, height = localRect.height },
                color = new { r = text.color.r, g = text.color.g, b = text.color.b, a = text.color.a }
            };
        }

        static IEnumerable<object> BuildTextIssues(Text text, object row)
        {
            JObject obj = JObject.FromObject(row);
            string path = (string)obj["path"];
            if (string.IsNullOrWhiteSpace((string)obj["text"]))
                yield return new { code = "empty_text", severity = "warning", path, message = "Text component has empty text." };
            if (text.font == null)
                yield return new { code = "missing_font", severity = "error", path, message = "Text component has no Font assigned." };
            if (!text.isActiveAndEnabled || !text.gameObject.activeInHierarchy)
                yield return new { code = "inactive_text", severity = "warning", path, message = "Text component is inactive or disabled." };
            if (((float)obj["alpha"]) <= 0.01f)
                yield return new { code = "transparent_text", severity = "warning", path, message = "Text effective alpha is near zero." };
            var rect = (JObject)obj["rect"];
            if ((float)rect["width"] <= 0.5f || (float)rect["height"] <= 0.5f)
                yield return new { code = "zero_rect", severity = "warning", path, message = "Text RectTransform has near-zero width or height." };
        }

        static object ShapeResponse(string toolName, bool success, string message, object data, ToolOperationTiming timing, string errorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                object shapedData = success
                    ? ToolResultCompactor.ShapeStructuredPayload(toolName, data, BuildCompactData(toolName, data), detailRefMeta: new { kind = $"{toolName}_full_result" }, payloadClass: "ui_font_text")
                    : data;
                response = success ? Response.Success(message, shapedData) : Response.Error(message, data);
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static object BuildCompactData(string toolName, object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            if (string.Equals(toolName, VisualTextAuditToolName, StringComparison.Ordinal))
            {
                return new
                {
                    passed = root["passed"],
                    textCount = root["textCount"],
                    tmpUnsupportedCount = root["tmpUnsupportedCount"],
                    issueCount = root["issueCount"],
                    issues = root["issues"],
                    omittedIssueCount = root["omittedIssueCount"]
                };
            }

            JArray rows = root["rows"] as JArray ?? new JArray();
            var changed = rows.OfType<JObject>().Where(row => row["willModify"]?.Value<bool>() == true || row["applied"]?.Value<bool>() == true).ToArray();
            return new
            {
                fontAssetPath = root["fontAssetPath"],
                fontExists = root["fontExists"],
                apply = root["apply"],
                applied = root["applied"],
                willModify = root["willModify"],
                bindingCount = root["bindingCount"],
                changedBindingCount = root["changedBindingCount"],
                changedBindings = changed,
                issues = root["issues"]
            };
        }

        static IEnumerable<TextScope> ResolveScopes(string assetPath)
        {
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                string normalized = NormalizeAssetPath(assetPath);
                GameObject root = PrefabUtility.LoadPrefabContents(normalized);
                yield return new TextScope(root, normalized, isPrefab: true);
                yield break;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                    yield return new TextScope(root, scene.path, isPrefab: false);
            }
        }

        static IEnumerable<GameObject> ResolveRoots(GameObject scopeRoot, string target, string searchMethod, bool includeInactive)
        {
            if (scopeRoot == null)
                yield break;
            if (string.IsNullOrWhiteSpace(target))
            {
                yield return scopeRoot;
                yield break;
            }

            if (string.Equals(searchMethod, "by_path", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Transform transform in scopeRoot.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (string.Equals(GetPath(transform), target, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(GetRelativePath(scopeRoot.transform, transform), target, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return transform.gameObject;
                    }
                }
                yield break;
            }

            foreach (Transform transform in scopeRoot.GetComponentsInChildren<Transform>(includeInactive))
            {
                if (string.Equals(transform.name, target, StringComparison.OrdinalIgnoreCase))
                    yield return transform.gameObject;
            }
        }

        static GameObject ResolveTargetPath(GameObject root, string targetPath, bool includeInactive)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetPath) || targetPath == ".")
                return root;

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(includeInactive))
            {
                if (string.Equals(GetRelativePath(root.transform, transform), targetPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(GetPath(transform), targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return transform.gameObject;
                }
            }

            return null;
        }

        static bool IsTmpTextComponent(Component component)
        {
            if (component == null)
                return false;
            string fullName = component.GetType().FullName ?? string.Empty;
            return fullName.Contains("TMPro.", StringComparison.Ordinal) && fullName.EndsWith("TMP_Text", StringComparison.Ordinal);
        }

        static string NormalizeAssetPath(string value)
        {
            string path = value?.Replace("\\", "/") ?? string.Empty;
            int assetsIndex = path.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
                return path.Substring(assetsIndex);
            return path;
        }

        static string GetPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;
            var names = new Stack<string>();
            for (Transform current = transform; current != null; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names);
        }

        static string GetRelativePath(Transform root, Transform transform)
        {
            string full = GetPath(transform);
            string rootPath = GetPath(root);
            if (full.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                return ".";
            if (full.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase))
                return full.Substring(rootPath.Length + 1);
            return full;
        }

        static UiTextAuditRequest NormalizeAuditRequest(JObject parameters)
        {
            return new UiTextAuditRequest
            {
                PrefabPath = GetString(parameters, "prefabPath", "PrefabPath"),
                Target = GetString(parameters, "target", "Target"),
                SearchMethod = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_name",
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                MaxItems = Math.Max(1, GetInt(parameters, 50, "maxItems", "MaxItems"))
            };
        }

        static UiFontBindRequest NormalizeFontBindRequest(JObject parameters)
        {
            return new UiFontBindRequest
            {
                FontAssetPath = GetString(parameters, "fontAssetPath", "FontAssetPath"),
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                Targets = GetToken(parameters, "targets", "Targets")?.ToObject<UiFontBindTarget[]>() ?? Array.Empty<UiFontBindTarget>()
            };
        }

        static string GetString(JObject parameters, params string[] names)
        {
            foreach (string name in names)
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token?.Type == JTokenType.Null ? null : token?.ToString();
            return null;
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            foreach (string name in names)
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token;
            return null;
        }

        static bool GetBool(JObject parameters, bool defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    continue;
                return token.Type == JTokenType.Boolean ? token.Value<bool>() : bool.TryParse(token.ToString(), out bool parsed) ? parsed : defaultValue;
            }
            return defaultValue;
        }

        static int GetInt(JObject parameters, int defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (!parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    continue;
                return token.Type == JTokenType.Integer ? token.Value<int>() : int.TryParse(token.ToString(), out int parsed) ? parsed : defaultValue;
            }
            return defaultValue;
        }

        static int GetUtf8ByteCount(string value) => Encoding.UTF8.GetByteCount(value ?? string.Empty);

        sealed class UiTextAuditRequest
        {
            public string PrefabPath;
            public string Target;
            public string SearchMethod = "by_name";
            public bool IncludeInactive = true;
            public int MaxItems = 50;
        }

        sealed class UiFontBindRequest
        {
            public string FontAssetPath;
            public bool IncludeInactive = true;
            public UiFontBindTarget[] Targets = Array.Empty<UiFontBindTarget>();
        }

        sealed class UiFontBindTarget
        {
            public string assetPath { get; set; }
            public string AssetPath { get => assetPath; set => assetPath = value; }
            public string target { get; set; }
            public string Target { get => target; set => target = value; }
            public string searchMethod { get; set; } = "by_name";
            public string SearchMethod { get => searchMethod; set => searchMethod = value; }
            public string targetPath { get; set; } = ".";
            public string TargetPath { get => targetPath; set => targetPath = value; }
            public bool? includeInactive { get; set; }
            public bool? IncludeInactive { get => includeInactive; set => includeInactive = value; }
        }

        sealed class TextScope : IDisposable
        {
            public readonly GameObject Root;
            public readonly string ScopePath;
            readonly bool m_IsPrefab;

            public TextScope(GameObject root, string scopePath, bool isPrefab)
            {
                Root = root;
                ScopePath = scopePath;
                m_IsPrefab = isPrefab;
            }

            public void Save()
            {
                if (m_IsPrefab)
                {
                    PrefabUtility.SaveAsPrefabAsset(Root, ScopePath);
                    return;
                }

                EditorSceneManager.MarkSceneDirty(Root.scene);
                EditorSceneManager.SaveScene(Root.scene);
            }

            public void Dispose()
            {
                if (m_IsPrefab && Root != null)
                    PrefabUtility.UnloadPrefabContents(Root);
            }
        }
    }
}
