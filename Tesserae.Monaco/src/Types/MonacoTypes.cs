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
    /// Matching Monaco's <c>IMarkdownString</c>. Set <see cref="supportHtml"/> and
    /// <see cref="isTrusted"/> to render HTML inside hovers and completion details.
    /// </summary>
    [ObjectLiteral]
    public class MarkdownString
    {
        public bool   isTrusted;
        public bool   supportHtml;
        public string value;
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
