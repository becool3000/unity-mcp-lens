using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Context
{
    partial class StaticDataBasedContextRetrievalBuilder : IContextRetrievalBuilder
    {
        [ContextRetrievalBuilder]
        static IContextRetrievalBuilder GetInstance()
        {
            const string dbGuid = "57f04a3abf12c4e6089d7cb7fe95aed1";
            var dbPath = AssetDatabase.GUIDToAssetPath(dbGuid);

            return new StaticDataBasedContextRetrievalBuilder(dbPath);
        }

        readonly Selector[] m_Selectors;

        StaticDataBasedContextRetrievalBuilder(string csvFile)
        {
            m_Selectors = AssetDatabase
                .LoadAssetAtPath<TextAsset>(csvFile)
                .text
                .Split("\n")
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split(";"))
                .Select(
                    fields => (type: fields[0], path: fields[1], description: fields[2],
                        parameters: fields[3].Split(",")))
                .Select(t => new Selector(t.type, t.path, t.description, t.parameters))
                .ToArray();
        }

        public IEnumerable<IContextSelection> GetSelectors() => m_Selectors;
    }
}
