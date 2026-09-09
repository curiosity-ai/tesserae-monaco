using System.Collections.Generic;
using System.Text;
using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>A one-based caret position, matching Monaco's <c>IPosition</c>.</summary>
    [ObjectLiteral]
    public class Position
    {
        public int lineNumber;
        public int column;
    }

    /// <summary>A one-based text range, matching Monaco's <c>IRange</c>.</summary>
    [ObjectLiteral]
    public class TextRange
    {
        public int startLineNumber;
        public int startColumn;
        public int endLineNumber;
        public int endColumn;
    }

    /// <summary>
    /// A size in pixels, matching Monaco's <c>IDimension</c> - what <c>layout(...)</c> takes when the
    /// caller has measured the container itself. See <see cref="DiffViewer.Layout"/> for the one case
    /// where measuring is not optional.
    /// </summary>
    [ObjectLiteral]
    public class EditorDimension
    {
        public double width;
        public double height;
    }

    /// <summary>A single text replacement, matching Monaco's <c>ISingleEditOperation</c>.</summary>
    [ObjectLiteral]
    public class TextEdit
    {
        public TextRange range;
        public string    text;
    }

    /// <summary>Severity of a squiggle in the editor gutter, matching Monaco's <c>MarkerSeverity</c>.</summary>
    [Enum(Emit.Value)]
    public enum MarkerSeverity
    {
        Hint    = 1,
        Info    = 2,
        Warning = 4,
        Error   = 8
    }

    /// <summary>
    /// A squiggle in the editor, matching Monaco's <c>IMarkerData</c>. All coordinates are
    /// <b>one-based</b>, as Monaco expects. Use <see cref="CodeDiagnostic"/> if your source of
    /// errors is zero-based.
    /// </summary>
    [ObjectLiteral]
    public sealed class CodeMarker
    {
        public int            startLineNumber;
        public int            startColumn;
        public int            endLineNumber;
        public int            endColumn;
        public string         message;
        public MarkerSeverity severity;
    }

    /// <summary>Matching Monaco's <c>CompletionItemKind</c> - drives the icon shown in the suggest list.</summary>
    [Enum(Emit.Value)]
    public enum CompletionItemKind
    {
        Method        = 0,
        Function      = 1,
        Constructor   = 2,
        Field         = 3,
        Variable      = 4,
        Class         = 5,
        Struct        = 6,
        Interface     = 7,
        Module        = 8,
        Property      = 9,
        Event         = 10,
        Operator      = 11,
        Unit          = 12,
        Value         = 13,
        Constant      = 14,
        Enum          = 15,
        EnumMember    = 16,
        Keyword       = 17,
        Text          = 18,
        Color         = 19,
        File          = 20,
        Reference     = 21,
        Customcolor   = 22,
        Folder        = 23,
        TypeParameter = 24,
        Snippet       = 25
    }

    /// <summary>Matching Monaco's <c>CompletionItemInsertTextRule</c>.</summary>
    [Enum(Emit.Value)]
    public enum CompletionItemInsertTextRule
    {
        /// <summary>Adjust whitespace/indentation of multiline insert texts to match the current line indentation.</summary>
        KeepWhitespace = 1,

        /// <summary><c>insertText</c> is a snippet.</summary>
        InsertAsSnippet = 4
    }

    /// <summary>Matching Monaco's <c>TrackedRangeStickiness</c>.</summary>
    [Enum(Emit.Value)]
    public enum TrackedRangeStickiness
    {
        AlwaysGrowsWhenTypingAtEdges = 0,
        NeverGrowsWhenTypingAtEdges  = 1,
        GrowsOnlyWhenTypingBefore    = 2,
        GrowsOnlyWhenTypingAfter     = 3
    }

    /// <summary>
    /// Matching Monaco's <c>IMarkdownString</c>: the documentation a hover or a completion item shows,
    /// rendered by Monaco's markdown renderer. A fenced code block is coloured by the editor's own
    /// tokenizer for its language, <c>---</c> draws a separator, and a <c>command:</c> link - see
    /// <see cref="MonacoEditor.CommandLink"/> - runs a registered command when <see cref="isTrusted"/> is set.
    /// </summary>
    [ObjectLiteral]
    public class MarkdownString
    {
        public string value;

        /// <summary>
        /// Whether <c>command:</c> links may run. Leave it off for text from a source the host does not
        /// control; Monaco then strips such links to their text.
        /// </summary>
        public bool isTrusted;

        /// <summary>
        /// Whether raw HTML in the markdown is kept rather than escaped. Monaco sanitises it against an
        /// allowlist - the structural tags, <c>href</c>, <c>title</c>, and <c>style</c> on a <c>span</c>
        /// for its colours - and drops everything else, <c>class</c> and <c>data-*</c> attributes
        /// included, so HTML cannot be styled from a stylesheet here. Off by default, and rarely worth
        /// turning on: it also makes a bare <c>&lt;T&gt;</c> in the text disappear as an unknown tag.
        /// </summary>
        public bool supportHtml;

        /// <summary>Whether <c>$(icon-name)</c> is drawn as the codicon of that name.</summary>
        public bool supportThemeIcons;
    }

    /// <summary>
    /// Builders for the markdown shapes Monaco - and this package's renderer - read something into.
    /// </summary>
    public static class MarkdownStringExtensions
    {
        /// <summary>
        /// Appends the links row that makes the type names in <paramref name="markdown"/>'s code blocks
        /// clickable - see <see cref="MonacoEditor.LinkTypesInCodeBlocks"/>: a separator, then one
        /// <c>[text](href)</c> per entry joined by <c>·</c>. Each key is the exact identifier as it appears in
        /// the block (<c>ReadOnlyNode</c>, <c>string</c>) and each value its target, normally a
        /// <see cref="MonacoEditor.CommandLink"/>. Marks the string trusted, since command links only run on
        /// one; a text with markdown in it is escaped so it stays literal. The row is exactly what a host
        /// that writes its own markdown (a server) writes by hand, so a producer on either side comes out
        /// the same.
        /// </summary>
        public static MarkdownString LinkedCodeBlocks(this MarkdownString markdown, IEnumerable<KeyValuePair<string, string>> links)
        {
            if (markdown is null || links is null) return markdown;

            var row = new List<string>();

            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link.Key) || string.IsNullOrWhiteSpace(link.Value)) continue;

                row.Add("[" + EscapeMarkdown(link.Key) + "](" + link.Value + ")");
            }

            if (row.Count == 0) return markdown;

            markdown.value     = (markdown.value ?? "").TrimEnd() + "\n\n---\n\n" + string.Join(" · ", row);
            markdown.isTrusted = true;

            return markdown;
        }

        /// <summary>
        /// The common case of <see cref="LinkedCodeBlocks(MarkdownString, IEnumerable{KeyValuePair{string, string}})"/>:
        /// every name links to the one command <paramref name="commandId"/>, with the name as its argument -
        /// the handler registered through <see cref="MonacoEditor.RegisterCommand{T}"/> then receives which
        /// type was clicked.
        /// </summary>
        public static MarkdownString LinkedCodeBlocks(this MarkdownString markdown, string commandId, params string[] typeNames)
        {
            if (markdown is null || string.IsNullOrWhiteSpace(commandId) || typeNames is null) return markdown;

            var links = new List<KeyValuePair<string, string>>();

            foreach (var name in typeNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                links.Add(new KeyValuePair<string, string>(name, MonacoEditor.CommandLink(commandId, name)));
            }

            return LinkedCodeBlocks(markdown, links);
        }

        // Monaco's own escape set plus the angle brackets, since a bare <T> reads as an HTML tag. An escaped
        // character comes out of the renderer as itself, which is what the block's tokens are compared to.
        private static string EscapeMarkdown(string text)
        {
            var escaped = new StringBuilder();

            foreach (var c in text)
            {
                if (MARKDOWN_SPECIALS.IndexOf(c) >= 0) escaped.Append('\\');

                escaped.Append(c);
            }

            return escaped.ToString();
        }

        private const string MARKDOWN_SPECIALS = "\\`*_{}[]()#+-!~<>";
    }

    /// <summary>One entry in the suggest list, matching Monaco's <c>CompletionItem</c>.</summary>
    [ObjectLiteral]
    public class CompletionItem
    {
        public string                       label;
        public string                       detail;
        public string                       insertText;
        public string                       sortText;
        public string                       filterText;
        public CompletionItemKind           kind;
        public MarkdownString               documentation;
        public bool                         preselect;
        public CompletionItemInsertTextRule insertTextRules;
        public TextRange                    range;
    }

    /// <summary>The value a completion provider resolves to, matching Monaco's <c>CompletionList</c>.</summary>
    [ObjectLiteral]
    public class CompletionList
    {
        public CompletionItem[] suggestions;
        public bool             incomplete;
    }

    /// <summary>The value a hover provider resolves to, matching Monaco's <c>Hover</c>.</summary>
    [ObjectLiteral]
    public class Hover
    {
        public TextRange        range;
        public MarkdownString[] contents;
    }

    /// <summary>An entry from <c>monaco.languages.getLanguages()</c>.</summary>
    [ObjectLiteral]
    public class LanguageInfo
    {
        public string   id         { get; set; }
        public string[] aliases    { get; set; }
        public string[] extensions { get; set; }
    }
}
