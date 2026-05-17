#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Models.Assets;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Adapters.Unity.Assets
{
    sealed class UnityAssetSpriteSheetBindingAdapter
    {
        sealed class ParsedTextureSettings
        {
            public bool HasFilterMode { get; set; }
            public FilterMode FilterMode { get; set; }
            public bool HasCompression { get; set; }
            public TextureImporterCompression Compression { get; set; }
            public bool HasWrapMode { get; set; }
            public TextureWrapMode WrapMode { get; set; }
        }

        public bool TryImportSpriteSheetAndBind(
            AssetSpriteSheetAndBindRequest request,
            bool previewOnly,
            out object data,
            out bool willModify,
            out string error)
        {
            data = null;
            willModify = false;
            error = null;

            string assetPath = NormalizeAssetPath(request.AssetPath);
            string targetAssetPath = NormalizeAssetPath(request.TargetAssetPath);
            string targetFieldName = request.TargetFieldName?.Trim();

            if (!IsMutableAssetsPath(assetPath))
            {
                error = $"Sprite-sheet import can only mutate explicit Assets/ paths. Got '{assetPath}'.";
                return false;
            }

            if (!IsMutableAssetsPath(targetAssetPath))
            {
                error = $"Sprite binding can only mutate explicit Assets/ target assets. Got '{targetAssetPath}'.";
                return false;
            }

            if (!TryParseTextureSettings(request, out ParsedTextureSettings settings, out error))
                return false;

            if (!previewOnly)
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                error = $"Texture importer not found for '{assetPath}'.";
                return false;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                error = $"Texture asset '{assetPath}' could not be loaded.";
                return false;
            }

            string spriteNamePrefix = string.IsNullOrWhiteSpace(request.SpriteNamePrefix)
                ? texture.name
                : request.SpriteNamePrefix.Trim();

            SpriteMetaData[] plannedMetadata = BuildSpritesheet(
                texture.width,
                texture.height,
                request.FrameCount,
                request.FrameWidth,
                request.FrameHeight,
                request.PaddingX,
                request.PaddingY,
                request.OffsetX,
                request.OffsetY,
                spriteNamePrefix);

            if (plannedMetadata.Length < request.FrameCount)
            {
                error = $"Requested {request.FrameCount} frame(s), but only {plannedMetadata.Length} fit inside '{assetPath}' ({texture.width}x{texture.height}).";
                return false;
            }

#pragma warning disable CS0618
            SpriteMetaData[] existingImporterMetadata = importer.spritesheet ?? Array.Empty<SpriteMetaData>();
#pragma warning restore CS0618
            plannedMetadata = PreserveExistingSpriteNamesForMatchingSlices(
                plannedMetadata,
                existingImporterMetadata,
                out int preservedExistingSpriteNameCount);

            Object targetAsset = AssetDatabase.LoadMainAssetAtPath(targetAssetPath);
            if (targetAsset == null)
            {
                error = $"Target asset '{targetAssetPath}' could not be loaded.";
                return false;
            }

            if (targetAsset is not ScriptableObject)
            {
                error = $"Target asset '{targetAssetPath}' is not a ScriptableObject.";
                return false;
            }

            SerializedObject targetObject = new(targetAsset);
            SerializedProperty targetProperty = targetObject.FindProperty(targetFieldName);
            if (targetProperty == null)
            {
                error = $"Serialized field '{targetFieldName}' was not found on '{targetAssetPath}'.";
                return false;
            }

            if (!TryValidateSpriteArrayProperty(targetAsset.GetType(), targetFieldName, targetProperty, out string elementTypeName, out error))
                return false;

            Object[] previousReferences = ReadArrayReferences(targetProperty);
            Sprite[] existingSprites = LoadSprites(assetPath);
            object importerBefore = DescribeImporter(importer, existingSprites.Length);
            object plannedImporter = DescribePlannedImporter(request, settings, plannedMetadata.Length);
            Object[] plannedReferences = ResolveSpritesForMetadata(existingSprites, plannedMetadata).Cast<Object>().ToArray();
            bool plannedReferencesResolved = plannedReferences.Length == plannedMetadata.Length && plannedReferences.All(reference => reference != null);
            bool importerWillModify = ImporterWillModify(importer, plannedMetadata, request, settings);
            bool bindingWillModify = plannedReferencesResolved
                ? !AreReferenceArraysEqual(previousReferences, plannedReferences)
                : !AreReferenceNamesEqual(previousReferences, plannedMetadata.Select(metadata => metadata.name).ToArray());

            bool importerApplied = false;
            bool bindingApplied = false;
            Sprite[] readbackSprites = existingSprites;
            Object[] readbackReferences = previousReferences;
            var warnings = new List<string>();

            if (!plannedReferencesResolved && previewOnly)
            {
                warnings.Add("Planned sprite subassets are not all present yet; apply will reimport the texture before binding.");
            }

            willModify = importerWillModify || bindingWillModify;

            if (!previewOnly)
            {
                if (importerWillModify)
                {
                    ApplyImporterSettings(importer, plannedMetadata, request, settings);
                    importer.SaveAndReimport();
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    importerApplied = true;
                }

                readbackSprites = LoadSprites(assetPath);
                Sprite[] spritesToBind = ResolveSpritesForMetadata(readbackSprites, plannedMetadata);
                if (spritesToBind.Length != plannedMetadata.Length || spritesToBind.Any(sprite => sprite == null))
                {
                    error = $"After import, only {spritesToBind.Count(sprite => sprite != null)} of {plannedMetadata.Length} planned sprite subassets could be resolved.";
                    return false;
                }

                targetObject = new SerializedObject(targetAsset);
                targetProperty = targetObject.FindProperty(targetFieldName);
                if (targetProperty == null)
                {
                    error = $"Serialized field '{targetFieldName}' was not found on '{targetAssetPath}' after import.";
                    return false;
                }

                previousReferences = ReadArrayReferences(targetProperty);
                Object[] spriteReferences = spritesToBind.Cast<Object>().ToArray();
                bindingApplied = !AreReferenceArraysEqual(previousReferences, spriteReferences);
                if (bindingApplied)
                {
                    targetProperty.arraySize = spriteReferences.Length;
                    for (int i = 0; i < spriteReferences.Length; i++)
                    {
                        SerializedProperty element = targetProperty.GetArrayElementAtIndex(i);
                        if (element == null || element.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            error = $"Serialized field '{targetFieldName}' does not expose object-reference array elements.";
                            return false;
                        }

                        element.objectReferenceValue = spriteReferences[i];
                    }

                    targetObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(targetAsset);
                }

                targetObject.UpdateIfRequiredOrScript();
                SerializedProperty readbackProperty = targetObject.FindProperty(targetFieldName);
                readbackReferences = readbackProperty == null ? Array.Empty<Object>() : ReadArrayReferences(readbackProperty);

                if (importerApplied || bindingApplied)
                    AssetDatabase.SaveAssets();
            }

            string[] spriteNames = plannedMetadata.Select(metadata => metadata.name).ToArray();
            data = new
            {
                mode = previewOnly ? "preview" : "apply",
                previewOnly,
                applied = !previewOnly && (importerApplied || bindingApplied),
                willModify,
                assetPath,
                textureGuid = AssetDatabase.AssetPathToGUID(assetPath),
                textureName = texture.name,
                textureSize = new { width = texture.width, height = texture.height },
                targetAssetPath,
                targetAssetGuid = AssetDatabase.AssetPathToGUID(targetAssetPath),
                targetAssetType = targetAsset.GetType().FullName,
                targetFieldName,
                targetFieldElementType = elementTypeName,
                requestedFrameCount = request.FrameCount,
                importedSpriteCount = previewOnly ? plannedMetadata.Length : readbackSprites.Length,
                spriteNames,
                preservedExistingSpriteNameCount,
                targetFieldReadbackCount = readbackReferences.Length,
                targetAssetDirty = EditorUtility.IsDirty(targetAsset),
                saved = !previewOnly && (importerApplied || bindingApplied),
                warnings = warnings.ToArray(),
                importer = new
                {
                    willModify = importerWillModify,
                    applied = importerApplied,
                    before = importerBefore,
                    planned = plannedImporter
                },
                binding = new
                {
                    willModify = bindingWillModify,
                    applied = bindingApplied,
                    previousCount = previousReferences.Length,
                    readbackCount = readbackReferences.Length,
                    previousReferences = previousReferences.Select(DescribeReference).ToArray(),
                    readbackReferences = readbackReferences.Select(DescribeReference).ToArray()
                },
                sprites = plannedMetadata.Select(metadata => DescribeSpritePlan(assetPath, metadata)).ToArray()
            };

            return true;
        }

        public bool TryVerifySpriteArrayBinding(
            AssetSpriteArrayBindingVerifyRequest request,
            out object data,
            out bool passed,
            out string error)
        {
            data = null;
            passed = false;
            error = null;

            string targetAssetPath = NormalizeAssetPath(request.TargetAssetPath);
            string targetFieldName = request.TargetFieldName?.Trim();
            Object targetAsset = AssetDatabase.LoadMainAssetAtPath(targetAssetPath);
            if (targetAsset == null)
            {
                error = $"Target asset '{targetAssetPath}' could not be loaded.";
                return false;
            }

            if (targetAsset is not ScriptableObject)
            {
                error = $"Target asset '{targetAssetPath}' is not a ScriptableObject.";
                return false;
            }

            SerializedObject serializedObject = new(targetAsset);
            SerializedProperty property = serializedObject.FindProperty(targetFieldName);
            if (property == null)
            {
                error = $"Serialized field '{targetFieldName}' was not found on '{targetAssetPath}'.";
                return false;
            }

            if (!TryValidateSpriteArrayProperty(targetAsset.GetType(), targetFieldName, property, out string elementTypeName, out error))
                return false;

            Object[] references = ReadArrayReferences(property);
            Sprite[] sprites = references.OfType<Sprite>().ToArray();
            string[] spriteNames = sprites.Select(sprite => sprite.name).ToArray();
            string[] textureNames = sprites
                .Select(sprite => sprite.texture != null ? sprite.texture.name : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] textureGuids = sprites
                .Select(sprite => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture)))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var failedReasons = new List<string>();
            if (references.Any(reference => reference == null))
                failedReasons.Add("Array contains null reference entries.");
            if (references.Any(reference => reference != null && reference is not Sprite))
                failedReasons.Add("Array contains non-Sprite object references.");

            if (request.ExpectedCount.HasValue && references.Length != request.ExpectedCount.Value)
                failedReasons.Add($"Expected {request.ExpectedCount.Value} array element(s), found {references.Length}.");

            if (!string.IsNullOrWhiteSpace(request.ExpectedTextureName) &&
                (sprites.Length == 0 || sprites.Any(sprite => !string.Equals(sprite.texture != null ? sprite.texture.name : null, request.ExpectedTextureName, StringComparison.Ordinal))))
            {
                failedReasons.Add($"Not all sprites use texture '{request.ExpectedTextureName}'.");
            }

            if (!string.IsNullOrWhiteSpace(request.ExpectedTextureGuid) &&
                (sprites.Length == 0 || sprites.Any(sprite => !string.Equals(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture)), request.ExpectedTextureGuid, StringComparison.OrdinalIgnoreCase))))
            {
                failedReasons.Add($"Not all sprites use texture GUID '{request.ExpectedTextureGuid}'.");
            }

            string[] expectedSpriteNames = request.ExpectedSpriteNames ?? Array.Empty<string>();
            if (expectedSpriteNames.Length > 0 && !spriteNames.SequenceEqual(expectedSpriteNames, StringComparer.Ordinal))
                failedReasons.Add("Sprite names do not match the expected ordered sprite-name list.");

            passed = failedReasons.Count == 0;
            data = new
            {
                targetAssetPath,
                targetAssetGuid = AssetDatabase.AssetPathToGUID(targetAssetPath),
                targetAssetType = targetAsset.GetType().FullName,
                targetFieldName,
                targetFieldElementType = elementTypeName,
                passed,
                actualCount = references.Length,
                nonNullSpriteCount = sprites.Length,
                expectedCount = request.ExpectedCount,
                expectedTextureName = request.ExpectedTextureName,
                expectedTextureGuid = request.ExpectedTextureGuid,
                expectedSpriteNames,
                spriteNames,
                textureNames,
                textureGuids,
                failedReasons = failedReasons.ToArray(),
                references = references.Select(DescribeReference).ToArray()
            };
            return true;
        }

        static bool TryParseTextureSettings(AssetSpriteSheetAndBindRequest request, out ParsedTextureSettings settings, out string error)
        {
            settings = new ParsedTextureSettings();
            error = null;

            if (!string.IsNullOrWhiteSpace(request.FilterMode))
            {
                if (!Enum.TryParse(request.FilterMode.Trim(), true, out FilterMode filterMode))
                {
                    error = $"Unsupported filterMode '{request.FilterMode}'.";
                    return false;
                }

                settings.HasFilterMode = true;
                settings.FilterMode = filterMode;
            }

            if (!string.IsNullOrWhiteSpace(request.Compression))
            {
                if (!Enum.TryParse(request.Compression.Trim(), true, out TextureImporterCompression compression))
                {
                    error = $"Unsupported compression '{request.Compression}'.";
                    return false;
                }

                settings.HasCompression = true;
                settings.Compression = compression;
            }

            if (!string.IsNullOrWhiteSpace(request.WrapMode))
            {
                if (!Enum.TryParse(request.WrapMode.Trim(), true, out TextureWrapMode wrapMode))
                {
                    error = $"Unsupported wrapMode '{request.WrapMode}'.";
                    return false;
                }

                settings.HasWrapMode = true;
                settings.WrapMode = wrapMode;
            }

            return true;
        }

        static bool ImporterWillModify(TextureImporter importer, SpriteMetaData[] plannedMetadata, AssetSpriteSheetAndBindRequest request, ParsedTextureSettings settings)
        {
            if (importer.textureType != TextureImporterType.Sprite)
                return true;

            if (importer.spriteImportMode != SpriteImportMode.Multiple)
                return true;

            if (request.AlphaIsTransparency.HasValue && importer.alphaIsTransparency != request.AlphaIsTransparency.Value)
                return true;

            if (request.MipmapEnabled.HasValue && importer.mipmapEnabled != request.MipmapEnabled.Value)
                return true;

            if (request.PixelsPerUnit.HasValue && Math.Abs(importer.spritePixelsPerUnit - request.PixelsPerUnit.Value) > 0.0001f)
                return true;

            if (settings.HasFilterMode && importer.filterMode != settings.FilterMode)
                return true;

            if (settings.HasCompression && importer.textureCompression != settings.Compression)
                return true;

            if (settings.HasWrapMode && importer.wrapMode != settings.WrapMode)
                return true;

            return !SpriteSheetsEqual(importer, plannedMetadata);
        }

        static SpriteMetaData[] PreserveExistingSpriteNamesForMatchingSlices(
            SpriteMetaData[] plannedMetadata,
            SpriteMetaData[] existingMetadata,
            out int preservedNameCount)
        {
            preservedNameCount = 0;
            plannedMetadata ??= Array.Empty<SpriteMetaData>();
            existingMetadata ??= Array.Empty<SpriteMetaData>();

            if (plannedMetadata.Length == 0 || plannedMetadata.Length != existingMetadata.Length)
                return plannedMetadata;

            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            var adjustedMetadata = new SpriteMetaData[plannedMetadata.Length];
            for (int i = 0; i < plannedMetadata.Length; i++)
            {
                SpriteMetaData planned = plannedMetadata[i];
                SpriteMetaData existing = existingMetadata[i];
                if (!Approximately(existing.rect, planned.rect) ||
                    !Approximately(existing.pivot, planned.pivot) ||
                    existing.alignment != planned.alignment ||
                    string.IsNullOrWhiteSpace(existing.name) ||
                    !existingNames.Add(existing.name))
                {
                    preservedNameCount = 0;
                    return plannedMetadata;
                }

                if (!string.Equals(existing.name, planned.name, StringComparison.Ordinal))
                    preservedNameCount++;

                planned.name = existing.name;
                adjustedMetadata[i] = planned;
            }

            return preservedNameCount > 0 ? adjustedMetadata : plannedMetadata;
        }

        static void ApplyImporterSettings(TextureImporter importer, SpriteMetaData[] plannedMetadata, AssetSpriteSheetAndBindRequest request, ParsedTextureSettings settings)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            if (request.AlphaIsTransparency.HasValue)
                importer.alphaIsTransparency = request.AlphaIsTransparency.Value;
            if (request.MipmapEnabled.HasValue)
                importer.mipmapEnabled = request.MipmapEnabled.Value;
            if (request.PixelsPerUnit.HasValue && request.PixelsPerUnit.Value > 0.0001f)
                importer.spritePixelsPerUnit = request.PixelsPerUnit.Value;
            if (settings.HasFilterMode)
                importer.filterMode = settings.FilterMode;
            if (settings.HasCompression)
                importer.textureCompression = settings.Compression;
            if (settings.HasWrapMode)
                importer.wrapMode = settings.WrapMode;

#pragma warning disable CS0618
            importer.spritesheet = plannedMetadata;
#pragma warning restore CS0618
        }

        static bool SpriteSheetsEqual(TextureImporter importer, SpriteMetaData[] plannedMetadata)
        {
#pragma warning disable CS0618
            SpriteMetaData[] existing = importer.spritesheet ?? Array.Empty<SpriteMetaData>();
#pragma warning restore CS0618
            if (existing.Length != plannedMetadata.Length)
                return false;

            for (int i = 0; i < existing.Length; i++)
            {
                if (!string.Equals(existing[i].name, plannedMetadata[i].name, StringComparison.Ordinal))
                    return false;
                if (!Approximately(existing[i].rect, plannedMetadata[i].rect))
                    return false;
                if (!Approximately(existing[i].pivot, plannedMetadata[i].pivot))
                    return false;
                if (existing[i].alignment != plannedMetadata[i].alignment)
                    return false;
            }

            return true;
        }

        static SpriteMetaData[] BuildSpritesheet(
            int textureWidth,
            int textureHeight,
            int frameCount,
            int frameWidth,
            int frameHeight,
            int paddingX,
            int paddingY,
            int offsetX,
            int offsetY,
            string spriteNamePrefix)
        {
            var metadata = new List<SpriteMetaData>();
            int index = 0;
            int nameDigits = Math.Max(2, frameCount.ToString().Length);
            int stepX = Math.Max(1, frameWidth + Math.Max(0, paddingX));
            int stepY = Math.Max(1, frameHeight + Math.Max(0, paddingY));
            int startX = Math.Max(0, offsetX);
            int startY = textureHeight - Math.Max(0, offsetY) - frameHeight;

            for (int y = startY; y >= 0 && metadata.Count < frameCount; y -= stepY)
            {
                for (int x = startX; x + frameWidth <= textureWidth && metadata.Count < frameCount; x += stepX)
                {
                    metadata.Add(new SpriteMetaData
                    {
                        alignment = (int)SpriteAlignment.Center,
                        border = Vector4.zero,
                        name = $"{spriteNamePrefix}_{index.ToString($"D{nameDigits}")}",
                        pivot = new Vector2(0.5f, 0.5f),
                        rect = new Rect(x, y, frameWidth, frameHeight)
                    });
                    index++;
                }
            }

            return metadata.ToArray();
        }

        static Sprite[] LoadSprites(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderByDescending(sprite => sprite.rect.y)
                .ThenBy(sprite => sprite.rect.x)
                .ThenBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToArray();
        }

        static Sprite[] ResolveSpritesForMetadata(Sprite[] sprites, SpriteMetaData[] metadata)
        {
            var byName = sprites
                .GroupBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            return metadata
                .Select(item => byName.TryGetValue(item.name, out Sprite sprite) ? sprite : null)
                .ToArray();
        }

        static bool TryValidateSpriteArrayProperty(Type ownerType, string propertyPath, SerializedProperty property, out string elementTypeName, out string error)
        {
            elementTypeName = null;
            error = null;

            if (property == null || !property.isArray)
            {
                error = $"Serialized field '{propertyPath}' is not an array or list.";
                return false;
            }

            Type elementType = TryGetFieldElementType(ownerType, propertyPath);
            elementTypeName = elementType?.FullName ?? "unknown";
            if (elementType != null && !elementType.IsAssignableFrom(typeof(Sprite)))
            {
                error = $"Serialized field '{propertyPath}' cannot accept Sprite references (element type is '{elementType.FullName}').";
                return false;
            }

            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                if (element == null || element.propertyType != SerializedPropertyType.ObjectReference)
                {
                    error = $"Serialized field '{propertyPath}' does not expose object-reference array elements.";
                    return false;
                }
            }

            return true;
        }

        static Type TryGetFieldElementType(Type ownerType, string propertyPath)
        {
            string fieldName = propertyPath;
            int dotIndex = propertyPath.IndexOf('.');
            if (dotIndex >= 0)
                fieldName = propertyPath.Substring(0, dotIndex);

            FieldInfo field = null;
            for (Type current = ownerType; current != null && field == null; current = current.BaseType)
            {
                field = current.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            Type fieldType = field?.FieldType;
            if (fieldType == null)
                return null;

            if (fieldType.IsArray)
                return fieldType.GetElementType();

            if (fieldType.IsGenericType)
            {
                Type genericType = fieldType.GetGenericTypeDefinition();
                if (genericType == typeof(List<>) || genericType == typeof(IList<>))
                    return fieldType.GetGenericArguments()[0];
            }

            return null;
        }

        static Object[] ReadArrayReferences(SerializedProperty property)
        {
            if (property == null || !property.isArray)
                return Array.Empty<Object>();

            var references = new Object[property.arraySize];
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                references[i] = element != null && element.propertyType == SerializedPropertyType.ObjectReference
                    ? element.objectReferenceValue
                    : null;
            }

            return references;
        }

        static bool AreReferenceArraysEqual(Object[] left, Object[] right)
        {
            left ??= Array.Empty<Object>();
            right ??= Array.Empty<Object>();
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        static bool AreReferenceNamesEqual(Object[] references, string[] names)
        {
            references ??= Array.Empty<Object>();
            names ??= Array.Empty<string>();
            if (references.Length != names.Length)
                return false;

            for (int i = 0; i < references.Length; i++)
            {
                if (!string.Equals(references[i] != null ? references[i].name : null, names[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        static object DescribeImporter(TextureImporter importer, int spriteCount)
        {
            return new
            {
                textureType = importer.textureType.ToString(),
                spriteImportMode = importer.spriteImportMode.ToString(),
                alphaIsTransparency = importer.alphaIsTransparency,
                mipmapEnabled = importer.mipmapEnabled,
                filterMode = importer.filterMode.ToString(),
                compression = importer.textureCompression.ToString(),
                wrapMode = importer.wrapMode.ToString(),
                pixelsPerUnit = importer.spritePixelsPerUnit,
                spriteCount
            };
        }

        static object DescribePlannedImporter(AssetSpriteSheetAndBindRequest request, ParsedTextureSettings settings, int spriteCount)
        {
            return new
            {
                textureType = TextureImporterType.Sprite.ToString(),
                spriteImportMode = SpriteImportMode.Multiple.ToString(),
                alphaIsTransparency = request.AlphaIsTransparency,
                mipmapEnabled = request.MipmapEnabled,
                filterMode = settings.HasFilterMode ? settings.FilterMode.ToString() : null,
                compression = settings.HasCompression ? settings.Compression.ToString() : null,
                wrapMode = settings.HasWrapMode ? settings.WrapMode.ToString() : null,
                pixelsPerUnit = request.PixelsPerUnit,
                spriteCount
            };
        }

        static object DescribeSpritePlan(string assetPath, SpriteMetaData metadata)
        {
            return new
            {
                name = metadata.name,
                assetPath,
                guid = AssetDatabase.AssetPathToGUID(assetPath),
                rect = new { x = metadata.rect.x, y = metadata.rect.y, width = metadata.rect.width, height = metadata.rect.height },
                pivot = new { x = metadata.pivot.x, y = metadata.pivot.y }
            };
        }

        static object DescribeReference(Object reference)
        {
            if (reference == null)
                return null;

            string path = AssetDatabase.GetAssetPath(reference);
            string guid = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path);
            if (reference is Sprite sprite)
            {
                string texturePath = sprite.texture != null ? AssetDatabase.GetAssetPath(sprite.texture) : null;
                return new
                {
                    name = sprite.name,
                    type = sprite.GetType().FullName,
                    path,
                    guid,
                    textureName = sprite.texture != null ? sprite.texture.name : null,
                    texturePath,
                    textureGuid = string.IsNullOrWhiteSpace(texturePath) ? null : AssetDatabase.AssetPathToGUID(texturePath),
                    rect = new { x = sprite.rect.x, y = sprite.rect.y, width = sprite.rect.width, height = sprite.rect.height }
                };
            }

            return new
            {
                name = reference.name,
                type = reference.GetType().FullName,
                path,
                guid
            };
        }

        static bool Approximately(Rect left, Rect right)
        {
            return Mathf.Abs(left.x - right.x) < 0.001f &&
                Mathf.Abs(left.y - right.y) < 0.001f &&
                Mathf.Abs(left.width - right.width) < 0.001f &&
                Mathf.Abs(left.height - right.height) < 0.001f;
        }

        static bool Approximately(Vector2 left, Vector2 right)
        {
            return Mathf.Abs(left.x - right.x) < 0.001f &&
                Mathf.Abs(left.y - right.y) < 0.001f;
        }

        static string NormalizeAssetPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string path = value.Trim().Replace('\\', '/');
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return "Assets/" + path.TrimStart('/');
        }

        static bool IsMutableAssetsPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("/../", StringComparison.Ordinal) &&
                !path.EndsWith("/..", StringComparison.Ordinal);
        }
    }
}
