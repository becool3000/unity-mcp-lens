using Unity.AI.Assistant.Editor.CodeAnalyze;

namespace Unity.AI.Assistant.Editor.CodeBlock
{
    static class CodeBlockValidatorUtils
    {
        const string k_ValidatorAssemblyName = "Unity.Assistant.CodeGen";
        static readonly DynamicAssemblyBuilder m_Builder = new(k_ValidatorAssemblyName);

        public static bool ValidateCode(string code, out string localFixedCode, out CompilationErrors compilationErrors)
        {
            bool success = m_Builder.TryCompileCode(code, out compilationErrors, out var compilation);
            localFixedCode = compilation.GetSourceCode();

            return success;
        }
    }
}
