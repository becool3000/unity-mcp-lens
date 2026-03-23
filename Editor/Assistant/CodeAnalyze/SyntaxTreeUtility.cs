using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    internal static class SyntaxTreeUtility
    {
        internal static SyntaxTree AddUsingDirective(this SyntaxTree syntaxTree, string namespaceToAdd)
        {
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;

            bool usingExists = root.Usings.Any(u => u.Name.ToString() == namespaceToAdd);
            if (!usingExists)
            {
                var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceToAdd))
                    .NormalizeWhitespace()
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                var newRoot = root.AddUsings(usingDirective).NormalizeWhitespace();

                return CSharpSyntaxTree.Create(newRoot);
            }

            return syntaxTree;
        }

        internal static SyntaxTree AddUsingDirective(this SyntaxTree syntaxTree, string namespaceToAdd, string aliasName)
        {
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            var usingExists = root.Usings.Any(u => u.Name.ToString() == namespaceToAdd && u.Alias?.Name.ToString() == aliasName);
            if (!usingExists)
            {
                var nameSyntax = SyntaxFactory.ParseName(namespaceToAdd);
                var aliasIdentifier = SyntaxFactory.IdentifierName(aliasName);
                var nameEqualsSyntax = SyntaxFactory.NameEquals(aliasIdentifier);

                var usingDirective = SyntaxFactory.UsingDirective(nameSyntax)
                    .WithAlias(nameEqualsSyntax)
                    .NormalizeWhitespace()
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                var newRoot = root.AddUsings(usingDirective).NormalizeWhitespace();

                return CSharpSyntaxTree.Create(newRoot);
            }

            return syntaxTree;
        }

        internal static SyntaxTree RemoveUsingDirective(this SyntaxTree syntaxTree, string namespaceToRemove)
        {
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;

            if (root == null)
                return syntaxTree;

            var usingToRemove = root.Usings.FirstOrDefault(u => u.Name.ToString() == namespaceToRemove);
            if (usingToRemove != null)
            {
                var newRoot = root.RemoveNode(usingToRemove, SyntaxRemoveOptions.KeepNoTrivia);
                return CSharpSyntaxTree.Create(newRoot.NormalizeWhitespace());
            }

            return syntaxTree;
        }

        internal static SyntaxTree RemoveNamespaces(this SyntaxTree syntaxTree)
        {
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;

            if (root == null)
                return syntaxTree;

            var newRoot = root.RemoveNodes(root.Members.OfType<NamespaceDeclarationSyntax>(), SyntaxRemoveOptions.KeepNoTrivia);

            var namespaceMembers = root.Members.OfType<NamespaceDeclarationSyntax>()
                .SelectMany(ns => ns.Members);

            newRoot = newRoot.AddMembers(namespaceMembers.ToArray());

            return CSharpSyntaxTree.Create(newRoot.NormalizeWhitespace());
        }

        internal static SyntaxTree MoveToNamespace(this SyntaxTree syntaxTree, string classNamespace)
        {
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;

            var existingNamespace = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (existingNamespace != null)
            {
                if (existingNamespace.Name.ToString() == classNamespace)
                {
                    return syntaxTree;
                }

                var newNamespace = existingNamespace.WithName(SyntaxFactory.ParseName(classNamespace));
                return CSharpSyntaxTree.Create(root.ReplaceNode(existingNamespace, newNamespace).NormalizeWhitespace());
            }

            var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(classNamespace));

            var typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToList();
            root = root.RemoveNodes(typeDeclarations, SyntaxRemoveOptions.KeepNoTrivia);
            namespaceDeclaration = namespaceDeclaration.AddMembers(typeDeclarations.ToArray());
            return CSharpSyntaxTree.Create(root.AddMembers(namespaceDeclaration).NormalizeWhitespace());
        }

        internal static SyntaxTree RenameClassWithInterface(this SyntaxTree syntaxTree, string newClassName, Type interfaceType)
        {
            var root = syntaxTree.GetRoot();

            string interfaceName = interfaceType.Name;
            var classDeclaration = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.BaseList?.Types.Any(t => t.ToString() == interfaceName) ?? false);

            if (classDeclaration == null)
                return syntaxTree;

            var newClassDeclaration = classDeclaration.WithIdentifier(SyntaxFactory.Identifier(newClassName));
            var newRoot = root.ReplaceNode(classDeclaration, newClassDeclaration);

            return CSharpSyntaxTree.Create((CSharpSyntaxNode)newRoot);
        }

        internal static SyntaxTree InsertAtLocation(this SyntaxTree syntaxTree, Location diagnosticLocation, string toInsert)
        {
            var linePositionSpan = diagnosticLocation.GetLineSpan().Span;
            var linePositionStart = linePositionSpan.Start;

            var text = syntaxTree.ToString();
            var sourceText = SourceText.From(text);

            var textSpan = new TextSpan(sourceText.Lines.GetPosition(linePositionStart), 0);

            var newText = text.Insert(textSpan.Start, toInsert);
            return SyntaxFactory.ParseSyntaxTree(newText);
        }

        internal static SyntaxTree RemoveType(this SyntaxTree syntaxTree, string typeName)
        {
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;

            if (root == null)
            {
                return syntaxTree;
            }

            var typesToRemove = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(t => t.Identifier.ValueText == typeName);

            foreach (var typeToRemove in typesToRemove)
            {
                root = root.RemoveNode(typeToRemove, SyntaxRemoveOptions.KeepNoTrivia);
            }

            return root.SyntaxTree;
        }

        internal static IEnumerable<ClassDeclarationSyntax> ExtractTypesByInheritance<T>(this SyntaxTree syntaxTree, out IEnumerable<UsingDirectiveSyntax> usingDirectives)
        {
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root == null)
            {
                usingDirectives = Enumerable.Empty<UsingDirectiveSyntax>();
                return Enumerable.Empty<ClassDeclarationSyntax>();
            }

            usingDirectives = root.Usings;

            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => c.BaseList != null && c.BaseList.Types.Any(t => t.Type.ToString() == typeof(T).Name));

            return classes;
        }

        internal static IEnumerable<ClassDeclarationSyntax> ChangeModifiersToPublic(this IEnumerable<ClassDeclarationSyntax> classes)
        {
            foreach (var classNode in classes)
            {
                // remove all access modifiers (but keeps static, abstract, etc)
                var nonAccessModifiers = classNode.Modifiers
                    .Where(m =>
                        !m.IsKind(SyntaxKind.PublicKeyword) &&
                        !m.IsKind(SyntaxKind.InternalKeyword) &&
                        !m.IsKind(SyntaxKind.PrivateKeyword) &&
                        !m.IsKind(SyntaxKind.ProtectedKeyword));

                // force public
                var newModifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .AddRange(nonAccessModifiers);

                var updatedClass = classNode.WithModifiers(newModifiers).NormalizeWhitespace();

                yield return updatedClass;
            }
        }

        internal static List<ClassCodeTextDefinition> ToCodeTextDefinition(this IEnumerable<ClassDeclarationSyntax> classes, IEnumerable<UsingDirectiveSyntax> usingDirectives)
        {
            var declarations = new List<ClassCodeTextDefinition>();
            foreach (var classNode in classes)
            {
                var extractedCode = CSharpSyntaxTree.ParseText(classNode.ToFullString());

                foreach (var usingDirective in usingDirectives)
                    extractedCode = extractedCode.AddUsingDirective(usingDirective.Name.ToString());

                declarations.Add(new ClassCodeTextDefinition(classNode.Identifier.ToString(), extractedCode.ToString()));
            }

            return declarations;
        }

        public static bool ContainsNamespaces(this SyntaxTree syntaxTree, string[] namespaces)
        {
            var root = syntaxTree.GetCompilationUnitRoot();

            // Check for using directives
            var usingDirectives = root.Usings;
            foreach (var usingDirective in usingDirectives)
            {
                var namespaceName = usingDirective.Name.ToString();
                if (namespaces.Any(ns => namespaceName.StartsWith(ns)))
                {
                    return true;
                }
            }

            // Check for fully qualified calls
            var descendants = root.DescendantNodes().OfType<IdentifierNameSyntax>();
            foreach (var identifier in descendants)
            {
                var fullName = identifier.GetText().ToString();
                if (namespaces.Any(ns => fullName.StartsWith(ns)))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsInterface(this SyntaxTree syntaxTree, string interfaceName)
        {
            var root = syntaxTree.GetCompilationUnitRoot();

            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classDeclaration in classDeclarations)
            {
                var baseList = classDeclaration.BaseList;
                if (baseList == null) continue;

                if (baseList.Types.Select(baseType => baseType.Type.ToString()).Any(baseTypeName => baseTypeName == interfaceName))
                    return true;
            }

            return false;
        }

        public static string GetSourceCode(this CSharpCompilation compilation)
        {
            return compilation?.SyntaxTrees.FirstOrDefault()?.GetText().ToString() ?? string.Empty;
        }
    }
}
