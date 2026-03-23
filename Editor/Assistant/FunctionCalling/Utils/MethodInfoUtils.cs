using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.FunctionCalling
{
    static class MethodInfoUtils
    {
        public static bool TryGetLocation(this MethodInfo method, out string path, out int line)
        {
            path = null;
            line = 0;

            if (method == null || method.DeclaringType == null)
                return false;

            try
            {
                // ------------------------------------------------------------
                // 1. Resolve async/iterator state machines
                // ------------------------------------------------------------

                var asyncAttr = method.GetCustomAttribute<AsyncStateMachineAttribute>();
                var iteratorAttr = method.GetCustomAttribute<IteratorStateMachineAttribute>();

                if (asyncAttr != null)
                {
                    var smType = asyncAttr.StateMachineType;
                    method = smType.GetMethod("MoveNext",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                else if (iteratorAttr != null)
                {
                    var smType = iteratorAttr.StateMachineType;
                    method = smType.GetMethod("MoveNext",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                // ------------------------------------------------------------
                // 2. Open the assembly with Cecil
                // ------------------------------------------------------------

                var asmPath = AssemblyUtils.GetAssemblyPath(method.DeclaringType.Assembly);
                if (string.IsNullOrEmpty(asmPath) || !File.Exists(asmPath))
                    return false;

                var readerParams = new ReaderParameters { ReadSymbols = true };
                using var asm = AssemblyDefinition.ReadAssembly(asmPath, readerParams);

                var typeName = method.DeclaringType.FullName.Replace('+', '/');
                var type = FindTypeByFullName(asm.MainModule, typeName);
                if (type == null)
                    return false;

                // Find the method in Cecil
                var paramCount = method.GetParameters().Length;
                var cecilMethod = type.Methods
                    .FirstOrDefault(m => m.Name == method.Name && m.Parameters.Count == paramCount);

                if (cecilMethod == null || !cecilMethod.HasBody)
                    return false;

                // ------------------------------------------------------------
                // 3. Extract the first meaningful sequence point
                // ------------------------------------------------------------

                const int hiddenSequencePoint = 0xFEEFEE;

                var seqPoint = cecilMethod.DebugInformation.SequencePoints
                    .Where(sp => sp != null
                        && sp.Document != null
                        && !string.IsNullOrEmpty(sp.Document.Url)
                        && sp.StartLine > 0
                        && sp.StartLine != hiddenSequencePoint)
                    .OrderBy(sp => sp.StartLine)
                    .FirstOrDefault();

                if (seqPoint == null)
                    return false;

                path = seqPoint.Document.Url;
                line = seqPoint.StartLine;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetSource(this MethodInfo method, out string sourceCode)
        {
            sourceCode = null;
            if (!method.TryGetLocation(out var filePath, out var startLine))
                return false;

            var sourceText = File.ReadAllText(filePath);

            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetRoot();

            var methodNodes = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var node in methodNodes)
            {
                if (node.Identifier.Text != method.Name)
                    continue;

                sourceCode = node.ToFullString();
                return true;
            }

            return false;
        }
        
        static TypeDefinition FindTypeByFullName(ModuleDefinition module, string fullName)
        {
            foreach (var type in module.Types)
            {
                var result = FindTypeRecursive(type, fullName);
                if (result != null)
                    return result;
            }
            return null;

            static TypeDefinition FindTypeRecursive(TypeDefinition type, string fullName)
            {
                if (type.FullName == fullName)
                    return type;

                foreach (var nested in type.NestedTypes)
                {
                    var found = FindTypeRecursive(nested, fullName);
                    if (found != null)
                        return found;
                }

                return null;
            }
        }
    }
}
