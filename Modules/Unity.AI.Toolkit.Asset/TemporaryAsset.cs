using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Asset
{
    [Serializable]
    class TemporaryAsset : IDisposable
    {
        static readonly Dictionary<string, int> k_ReferenceCount = new();

        internal class Scope : IDisposable
        {
            public List<TemporaryAsset> assets { get; }

            public Scope(IEnumerable<TemporaryAsset> assets) => this.assets = assets.ToList();

            public void Dispose()
            {
                foreach (var asset in assets)
                {
                    asset?.Dispose();
                }
                assets.Clear();
            }
        }

        public AssetReference asset { get; }

        string tempFolder { get; }

        bool m_Disposed;

        readonly bool m_Disposable;
        readonly string m_AssetKey;

        public TemporaryAsset(AssetReference asset, string tempFolder = "", bool persistent = false)
        {
            this.asset = asset;
            this.tempFolder = tempFolder;
            m_Disposable = !persistent;
            m_AssetKey = asset.GetPath();

            if (!m_Disposable || string.IsNullOrEmpty(m_AssetKey))
                return;

            var count = k_ReferenceCount.GetValueOrDefault(m_AssetKey, 0);
            k_ReferenceCount[m_AssetKey] = count + 1;
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            if (!m_Disposable)
            {
                m_Disposed = true;
                return;
            }

            var shouldDelete = false;

            if (!string.IsNullOrEmpty(m_AssetKey) && k_ReferenceCount.TryGetValue(m_AssetKey, out var count))
            {
                count--;
                shouldDelete = count <= 0;
                if (shouldDelete)
                    k_ReferenceCount.Remove(m_AssetKey);
                else
                    k_ReferenceCount[m_AssetKey] = count;
            }

            try
            {
                if (!shouldDelete)
                    return;


                if (asset.Exists())
                {
                    if (AssetDatabase.AssetPathExists(asset.GetPath()))
                        AssetDatabase.DeleteAsset(asset.GetPath());
                    else
                        File.Delete(asset.GetPath());
                }
                if (!string.IsNullOrEmpty(tempFolder) && AssetDatabase.AssetPathExists(tempFolder))
                    AssetDatabase.DeleteAsset(tempFolder);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error cleaning up temporary asset '{asset}': {ex}");
            }
            finally
            {
                m_Disposed = true;
            }
        }
    }
}
