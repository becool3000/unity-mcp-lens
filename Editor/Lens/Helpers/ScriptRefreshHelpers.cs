using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Helper utilities for script refresh and compilation operations.
    /// </summary>
    static class ScriptRefreshHelpers
    {
        /// <summary>
        /// Sanitizes and normalizes an asset path to ensure it's valid for Unity.
        /// </summary>
        /// <param name="p">The path to sanitize</param>
        /// <returns>A normalized Assets-relative path</returns>
        public static string SanitizeAssetsPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            p = p.Replace('\\', '/').Trim();
            if (p.StartsWith("unity://path/", StringComparison.OrdinalIgnoreCase))
                p = p.Substring("unity://path/".Length);
            while (p.StartsWith("Assets/Assets/", StringComparison.OrdinalIgnoreCase))
                p = p.Substring("Assets/".Length);
            if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                p = "Assets/" + p.TrimStart('/');
            return p;
        }

        /// <summary>
        /// Schedules a script refresh operation with debouncing to avoid excessive recompilation.
        /// </summary>
        /// <param name="relPath">The relative path to the script to refresh</param>
        public static void ScheduleScriptRefresh(string relPath)
        {
            var sp = SanitizeAssetsPath(relPath);
            RefreshDebounce.Schedule(sp, TimeSpan.FromMilliseconds(200));
        }

        /// <summary>
        /// Cancels all pending script refresh/compile operations.
        /// Useful for test cleanup to prevent cross-contamination between tests.
        /// </summary>
        public static void CancelPendingRefreshes()
        {
            RefreshDebounce.CancelAll();
        }

        /// <summary>
        /// Imports an asset and requests script compilation.
        /// </summary>
        /// <param name="relPath">The relative path to the script to import</param>
        /// <param name="synchronous">Whether to force synchronous import (default: true)</param>
        public static void ImportAndRequestCompile(string relPath, bool synchronous = true)
        {
            var sp = SanitizeAssetsPath(relPath);
            var opts = ImportAssetOptions.ForceUpdate;
            if (synchronous) opts |= ImportAssetOptions.ForceSynchronousImport;
            AssetDatabase.ImportAsset(sp, opts);
#if UNITY_EDITOR
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
#endif
        }

        /// <summary>
        /// Split an incoming URI or path into (name, directory) suitable for Unity.
        ///
        /// Rules:
        /// - unity://path/Assets/... → keep as Assets-relative (after decode/normalize)
        /// - file://... → percent-decode, normalize, strip host and leading slashes,
        ///   then, if any 'Assets' segment exists, return path relative to that 'Assets' root.
        ///   Otherwise, fall back to original name/dir behavior.
        /// - plain paths → decode/normalize separators; if they contain an 'Assets' segment,
        ///   return relative to 'Assets'.
        /// </summary>
        /// <param name="uri">The URI or path to split</param>
        /// <returns>A tuple containing (name, directory) where name is the filename without extension and directory is the Assets-relative path</returns>
        public static (string name, string directory) SplitUri(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return (null, null);

            string rawPath;

            if (uri.StartsWith("unity://path/", StringComparison.OrdinalIgnoreCase))
            {
                rawPath = uri.Substring("unity://path/".Length);
            }
            else if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uriObj = new Uri(uri);
                    string host = uriObj.Host?.Trim() ?? "";
                    string path = uriObj.LocalPath ?? "";

                    // Handle UNC paths: file://server/share/... -> //server/share/...
                    if (!string.IsNullOrEmpty(host) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        path = $"//{host}{path}";
                    }

                    // Use percent-decoded path
                    rawPath = WebUtility.UrlDecode(path);
                }
                catch (UriFormatException)
                {
                    // Fallback to simple substring if URI parsing fails
                    rawPath = WebUtility.UrlDecode(uri.Substring("file://".Length));
                }
            }
            else
            {
                rawPath = uri;
            }

            // Percent-decode any residual encodings and normalize separators
            rawPath = WebUtility.UrlDecode(rawPath).Replace('\\', '/');

            // Strip leading slash only for Windows drive-letter forms like "/C:/..."
            if (Application.platform == RuntimePlatform.WindowsEditor &&
                rawPath.Length >= 3 && rawPath[0] == '/' && rawPath[2] == ':')
            {
                rawPath = rawPath.Substring(1);
            }

            // Normalize path (collapse ../, ./)
            string norm;
            try
            {
                norm = Path.GetFullPath(rawPath).Replace('\\', '/');
            }
            catch
            {
                // If path normalization fails, use the raw path
                norm = rawPath;
            }

            // If an 'Assets' segment exists, compute path relative to it (case-insensitive)
            string[] parts = norm.Split(new char[] {'/'}, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p != "." && p != "")
                .ToArray();

            int? assetsIndex = null;
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], "assets", StringComparison.OrdinalIgnoreCase))
                {
                    assetsIndex = i;
                    break;
                }
            }

            string assetsRelativePath = null;
            if (assetsIndex.HasValue)
            {
                assetsRelativePath = string.Join("/", parts.Skip(assetsIndex.Value));
            }

            string effectivePath = assetsRelativePath ?? norm;

            // Extract name (filename without extension) and directory
            string name = Path.GetFileNameWithoutExtension(effectivePath);
            string directory = Path.GetDirectoryName(effectivePath)?.Replace('\\', '/');

            return (name, directory);
        }
    }
}
