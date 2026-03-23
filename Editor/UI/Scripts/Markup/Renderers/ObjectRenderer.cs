using Markdig.Renderers;
using Markdig.Syntax;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal abstract class ObjectRenderer<TObject> : MarkdownObjectRenderer<ChatMarkdownRenderer, TObject>
        where TObject : MarkdownObject
    {
    }
}
