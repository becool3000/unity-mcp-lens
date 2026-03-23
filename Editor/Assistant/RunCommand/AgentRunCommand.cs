using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Unity.AI.Assistant.Editor.CodeAnalyze;
using Unity.AI.Assistant.Agent.Dynamic.Extension.Editor;
using Unity.AI.Assistant.Editor.Utils;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.RunCommand
{
    class AgentRunCommand
    {
        IRunCommand m_ActionInstance;
        private RunCommandMetadata m_Metadata;

        public string Script { get; set; }
        public CompilationErrors CompilationErrors { get; set; }

        public bool Unsafe => m_Metadata.IsUnsafe;

        public bool CompilationSuccess;
        internal CSharpCompilation Compilation { get; set; }

        public void Initialize(CSharpCompilation compilation)
        {
            Compilation = compilation;

            m_Metadata = RunCommandCodeAnalyzer.AnalyzeCommandAndExtractMetadata(Compilation);
        }
      
        public bool Execute(out ExecutionResult executionResult, string title)
        {
            executionResult = new ExecutionResult(title);

            if (m_ActionInstance == null)
                return false;

            executionResult.Start();

            try
            {
                m_ActionInstance.Execute(executionResult);

                // Unsafe actions usually mean deleting things - so we need to update the project view afterwards
                if (Unsafe)
                {
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception e)
            {
                executionResult.LogError(e.ToString());
            }

            executionResult.End();

            return true;
        }

        public bool HasUnauthorizedNamespaceUsage()
        {
            return RunCommandCodeAnalyzer.HasUnauthorizedNamespaceUsage(Script);
        }

        public void SetInstance(IRunCommand commandInstance)
        {
            m_ActionInstance = commandInstance;
        }
    }
}
