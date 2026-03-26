using System;
using System.Collections.Generic;
using System.IO;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    /// <summary>
    /// Static helpers for asset type resolution from path/extension.
    /// Extension-to-type mapping follows Unity's Supported asset type reference (Asset importers).
    /// https://docs.unity3d.com/Manual/AssetTypes.html#asset-importers
    /// </summary>
    static class AssetTypeUtils
    {
        static readonly Dictionary<string, string> s_ExtensionToType = BuildExtensionToTypeMap();

        static Dictionary<string, string> BuildExtensionToTypeMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 3D model importers
            foreach (var ext in new[] { ".obj", ".max", ".mb", ".ma", ".lxo", ".jas", ".fbx", ".dxf", ".dae", ".c4d", ".blend", ".st", ".spm", ".3ds", ".skp", ".sbsar", ".st9" })
                map[ext] = "Mesh";

            // Audio
            foreach (var ext in new[] { ".xm", ".wav", ".s3m", ".ogg", ".mp3", ".mod", ".it", ".flac", ".aiff", ".aif", ".aac" })
                map[ext] = "AudioClip";

            // Video
            foreach (var ext in new[] { ".wmv", ".webm", ".vp8", ".ogv", ".mpeg", ".mpg", ".mp4", ".mov", ".m4v", ".dv", ".avi", ".asf" })
                map[ext] = "VideoClip";

            // Image / texture
            foreach (var ext in new[] { ".pvr", ".ktx", ".dds", ".astc", ".svg", ".tiff", ".tif", ".tga", ".psd", ".png", ".pict", ".pic", ".pct", ".jpg", ".jpeg", ".iff", ".hdr", ".gif", ".exr", ".bmp" })
                map[ext] = "Texture";

            // Native / scene / prefab / anim / material
            map[".unity"] = "Scene";
            map[".prefab"] = "Prefab";
            map[".anim"] = "AnimationClip";
            map[".mat"] = "Material";
            map[".physicmaterial"] = "PhysicsMaterial";
            map[".physicsmaterial2d"] = "PhysicsMaterial";
            map[".asset"] = "ScriptableObject";
            map[".rendertexture"] = "RenderTexture";
            map[".cubemap"] = "Cubemap";
            map[".controller"] = "AnimatorController";
            map[".overridecontroller"] = "AnimatorController";
            map[".mask"] = "AvatarMask";
            map[".fontsettings"] = "Font";
            map[".spriteatlas"] = "SpriteAtlas";
            map[".terrainlayer"] = "TerrainLayer";

            // Plug-ins / code
            map[".cs"] = "Script";
            map[".asmdef"] = "AssemblyDefinition";
            map[".asmref"] = "AssemblyDefinitionReference";

            // Shader
            foreach (var ext in new[] { ".shader", ".hlsl", ".cginc", ".cg", ".glslinc" })
                map[ext] = "Shader";
            map[".compute"] = "ComputeShader";
            map[".raytrace"] = "RayTracingShader";

            // Text / arbitrary data
            foreach (var ext in new[] { ".yaml", ".xml", ".txt", ".md", ".html", ".htm", ".csv", ".bytes", ".js", ".po", ".fnt", ".manifest", ".boo", ".json", ".config", ".rsp" })
                map[ext] = "TextAsset";

            // Fonts
            foreach (var ext in new[] { ".ttf", ".ttc", ".otf", ".dfont" })
                map[ext] = "Font";

            // Built-in scripted importers (UIToolkit, etc.)
            map[".uss"] = "StyleSheet";
            map[".uxml"] = "UIXML";
            map[".tss"] = "Theme";

            return map;
        }

        /// <summary>
        /// Returns asset type from path using extension-based mapping when runtime type is not available.
        /// Aligned with Unity Manual: Supported asset type reference (Asset importers).
        /// </summary>
        public static string GetAssetTypeFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";
            if (path.IndexOf("Packages/", StringComparison.OrdinalIgnoreCase) >= 0) return "PackageAsset";

            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return "Unknown";

            return s_ExtensionToType.TryGetValue(ext, out var type) ? type : "Unknown";
        }

        /// <summary>
        /// Curated descriptions for asset types used in the restructured graph.
        /// Types not listed get a generic "Assets of type {name}" description.
        /// </summary>
        public static Dictionary<string, string> GetAssetTypeDescriptions()
        {
            return new Dictionary<string, string>
            {
                { "Script", "C# script files (MonoScript) that contain game logic, components, and behaviors" },
                { "MonoScript", "C# script files that contain game logic, components, and behaviors" },
                { "Material", "Material assets that define surface properties for rendering" },
                { "Prefab", "Prefabricated GameObject templates that can be instantiated in scenes" },
                { "Texture", "Image files used for textures, sprites, and UI elements" },
                { "Texture2D", "2D texture image files used for materials, sprites, and UI" },
                { "Mesh", "3D model files (FBX, OBJ, etc.) containing geometry data" },
                { "AudioClip", "Audio files (WAV, MP3, OGG) used for sound effects and music" },
                { "VideoClip", "Video files used for video playback" },
                { "Sprite", "2D image assets used for 2D games and UI" },
                { "Shader", "Shader code files that define rendering behavior" },
                { "ComputeShader", "Compute shader assets for GPU computation" },
                { "RayTracingShader", "Ray tracing shader assets" },
                { "AnimationClip", "Animation data files for character and object animations" },
                { "StyleSheet", "UI StyleSheet files (USS) for UIToolkit styling" },
                { "VisualTreeAsset", "UI XML files (UXML) for UIToolkit UI definitions" },
                { "UIXML", "UI XML files (UXML) for UIToolkit UI definitions" },
                { "ThemeStyleSheet", "Theme files (TSS) for UIToolkit theming" },
                { "Theme", "Theme files (TSS) for UIToolkit theming" },
                { "PanelSettings", "UIToolkit PanelSettings configuration assets" },
                { "ScriptableObject", "ScriptableObject asset files for data containers" },
                { "PhysicsMaterial", "Physics material assets for friction and bounciness" },
                { "RenderTexture", "Render texture assets for off-screen rendering" },
                { "GameObject", "GameObject assets (prefabs or scene objects)" },
                { "Scene", "Unity scene files" },
                { "AssemblyDefinition", "Assembly definition files for organizing scripts" },
                { "AssemblyDefinitionReference", "Assembly definition reference files" },
                { "Font", "Font assets for text rendering" },
                { "TextAsset", "Text and arbitrary data files" },
                { "PackageAsset", "Assets from Unity packages" },
                { "Unknown", "Assets with unknown or unrecognized types" }
            };
        }
    }
}
