using Transpose;
using Transpose.Core;

namespace Tesserae.Monaco
{
    // ---------------------------------------------------------------------------------------------
    // Signature help (parameter hints)
    // ---------------------------------------------------------------------------------------------

    /// <summary>One parameter in a signature, matching Monaco's <c>ParameterInformation</c>.</summary>
    [ObjectLiteral]
    public class ParameterInformation
    {
        /// <summary>
        /// The parameter's text. Monaco highlights the active parameter by finding this substring
        /// inside the signature's own label, so it has to match it exactly.
        /// </summary>
        public string         label;

        public MarkdownString documentation;
    }

    /// <summary>One overload, matching Monaco's <c>SignatureInformation</c>.</summary>
    [ObjectLiteral]
    public class SignatureInformation
    {
        /// <summary>The whole signature as one line, e.g. <c>"Substring(int start, int length)"</c>.</summary>
        public string                 label;

        public MarkdownString         documentation;
        public ParameterInformation[] parameters;
    }

    /// <summary>
    /// The parameter hints to show, matching Monaco's <c>SignatureHelp</c>. Both indexes are
    /// zero-based: <see cref="activeParameter"/> is which argument the caret is in.
    /// </summary>
    [ObjectLiteral]
    public class SignatureHelp
    {
        public SignatureInformation[] signatures;
        public int                    activeSignature;
        public int                    activeParameter;
    }

    // ---------------------------------------------------------------------------------------------
    // Code actions (quick fixes)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A quick fix or refactoring offered in the lightbulb menu, matching Monaco's <c>CodeAction</c>.
    ///
    /// Give it <see cref="edits"/> to change the document. <see cref="kind"/> decides where it appears:
    /// <c>"quickfix"</c> is the lightbulb, <c>"refactor"</c> the refactor menu, and
    /// <c>"source.fixAll"</c> runs on save in hosts that support it.
    /// </summary>
    [ObjectLiteral]
    public class CodeAction
    {
        /// <summary>The menu text.</summary>
        public string title;

        /// <summary>The edits to apply, in Monaco's one-based coordinates.</summary>
        public TextEdit[] edits;

        /// <summary>
        /// Monaco's action kind: <c>"quickfix"</c> for the lightbulb, <c>"refactor"</c> for the refactor
        /// menu. Left unset the provider fills in <c>"quickfix"</c>.
        /// </summary>
        public string kind;

        /// <summary>Marks this as the action to run when the user accepts without choosing.</summary>
        public bool isPreferred;

        /// <summary>The markers this action fixes, so Monaco attaches it to the right squiggle.</summary>
        public CodeMarker[] diagnostics;
    }

    /// <summary>
    /// What the user asked a code action for: the range they were on, and the squiggles under it.
    /// Filter on <see cref="Markers"/> to offer a fix only for a problem you actually reported.
    /// </summary>
    public sealed class CodeActionContext
    {
        /// <summary>The whole document.</summary>
        public string Text { get; }

        /// <summary>The range the lightbulb was raised for - often empty, at the caret.</summary>
        public TextRange Range { get; }

        /// <summary>The markers overlapping <see cref="Range"/>.</summary>
        public CodeMarker[] Markers { get; }

        internal CodeActionContext(string text, TextRange range, CodeMarker[] markers)
        {
            Text    = text;
            Range   = range;
            Markers = markers ?? new CodeMarker[0];
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Navigation
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A place in a document, matching Monaco's <c>Location</c> - what "go to definition" and "find
    /// all references" return.
    ///
    /// Leave <see cref="uri"/> null for a location in the document the request came from, which is the
    /// common case for a single-file host; set it to another model's
    /// <see cref="CodeModel.Uri"/> to send the user to a different file.
    /// </summary>
    [ObjectLiteral]
    public class CodeLocation
    {
        /// <summary>The target model's URI, or null for the document the request came from.</summary>
        public string    uri;

        public TextRange range;
    }

    /// <summary>Why a symbol occurrence is highlighted, matching Monaco's <c>DocumentHighlightKind</c>.</summary>
    [Enum(Emit.Value)]
    public enum DocumentHighlightKind
    {
        Text  = 0,
        Read  = 1,
        Write = 2
    }

    /// <summary>One highlighted occurrence, matching Monaco's <c>DocumentHighlight</c>.</summary>
    [ObjectLiteral]
    public class DocumentHighlight
    {
        public TextRange             range;
        public DocumentHighlightKind kind;
    }

    // ---------------------------------------------------------------------------------------------
    // Symbols
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// What kind of thing a symbol is, matching Monaco's <c>SymbolKind</c> - it picks the icon in the
    /// outline and the quick-open list.
    /// </summary>
    [Enum(Emit.Value)]
    public enum SymbolKind
    {
        File          = 0,
        Module        = 1,
        Namespace     = 2,
        Package       = 3,
        Class         = 4,
        Method        = 5,
        Property      = 6,
        Field         = 7,
        Constructor   = 8,
        Enum          = 9,
        Interface     = 10,
        Function      = 11,
        Variable      = 12,
        Constant      = 13,
        String        = 14,
        Number        = 15,
        Boolean       = 16,
        Array         = 17,
        Object        = 18,
        Key           = 19,
        Null          = 20,
        EnumMember    = 21,
        Struct        = 22,
        Event         = 23,
        Operator      = 24,
        TypeParameter = 25
    }

    /// <summary>
    /// An entry in the outline, matching Monaco's <c>DocumentSymbol</c> - what Ctrl+Shift+O lists and
    /// what the breadcrumbs are built from.
    ///
    /// <see cref="range"/> is everything the symbol covers, including its body;
    /// <see cref="selectionRange"/> is just its name, and is where the caret lands. It has to be inside
    /// <see cref="range"/> or Monaco discards the symbol.
    /// </summary>
    [ObjectLiteral]
    public class DocumentSymbol
    {
        public string           name;
        public string           detail;
        public SymbolKind       kind;
        public TextRange        range;
        public TextRange        selectionRange;
        public DocumentSymbol[] children;

        /// <summary>Monaco requires this array to exist, even empty. The provider fills it in when unset.</summary>
        public object[] tags;
    }

    // ---------------------------------------------------------------------------------------------
    // Inlay hints, code lenses, folding, links, colours
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether an inlay hint reads as a type or a parameter name, matching Monaco's <c>InlayHintKind</c>.</summary>
    [Enum(Emit.Value)]
    public enum InlayHintKind
    {
        Type      = 1,
        Parameter = 2
    }

    /// <summary>
    /// Read-only text Monaco paints between the code - an inferred type, a parameter name - matching
    /// Monaco's <c>InlayHint</c>. It is not part of the document and cannot be selected.
    /// </summary>
    [ObjectLiteral]
    public class InlayHint
    {
        public Position       position;
        public string         label;
        public InlayHintKind  kind;
        public MarkdownString tooltip;
        public bool           paddingLeft;
        public bool           paddingRight;
    }

    /// <summary>
    /// A clickable annotation above a line, matching Monaco's <c>CodeLens</c>. <see cref="title"/> is
    /// the text; clicking runs the handler the provider was registered with.
    /// </summary>
    [ObjectLiteral]
    public class CodeLensItem
    {
        public TextRange range;

        /// <summary>The text shown above the line, e.g. <c>"3 references"</c>.</summary>
        public string title;

        /// <summary>Optional tooltip.</summary>
        public string tooltip;
    }

    /// <summary>What a folded region is, matching Monaco's <c>FoldingRangeKind</c> values.</summary>
    public static class FoldingRangeKind
    {
        public const string Comment = "comment";
        public const string Imports = "imports";
        public const string Region  = "region";
    }

    /// <summary>
    /// A foldable region, matching Monaco's <c>FoldingRange</c>. Lines are one-based and both ends are
    /// inclusive; the fold hides everything after <see cref="start"/> up to and including
    /// <see cref="end"/>.
    /// </summary>
    [ObjectLiteral]
    public class FoldingRange
    {
        public int    start;
        public int    end;

        /// <summary>One of <see cref="FoldingRangeKind"/>, or null for a plain region.</summary>
        public string kind;
    }

    /// <summary>A clickable link in the text, matching Monaco's <c>ILink</c>.</summary>
    [ObjectLiteral]
    public class DocumentLink
    {
        public TextRange range;

        /// <summary>Where the link goes.</summary>
        public string    url;

        /// <summary>Overrides the "follow link" tooltip.</summary>
        public string    tooltip;
    }

    /// <summary>An RGBA colour with components from 0 to 1, matching Monaco's <c>IColor</c>.</summary>
    [ObjectLiteral]
    public class ColorValue
    {
        public double red;
        public double green;
        public double blue;
        public double alpha;
    }

    /// <summary>
    /// A colour literal Monaco should show a swatch and picker for, matching Monaco's
    /// <c>IColorInformation</c>.
    /// </summary>
    [ObjectLiteral]
    public class ColorInformation
    {
        public ColorValue color;
        public TextRange  range;
    }

    // ---------------------------------------------------------------------------------------------
    // Semantic tokens
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The vocabulary a semantic-tokens provider encodes against, matching Monaco's
    /// <c>SemanticTokensLegend</c>. The indexes into these arrays are what the token data refers to,
    /// and the type names are what the theme's rules are matched against.
    /// </summary>
    [ObjectLiteral]
    public class SemanticTokensLegend
    {
        public string[] tokenTypes;
        public string[] tokenModifiers;
    }

    /// <summary>
    /// Server-driven highlighting on top of the Monarch tokenizer, matching Monaco's
    /// <c>SemanticTokens</c>.
    ///
    /// <see cref="Data"/> is Monaco's packed encoding: five integers per token -
    /// <c>deltaLine, deltaStart, length, tokenTypeIndex, tokenModifierSet</c> - where the deltas are
    /// relative to the previous token, and the first token's are relative to the start of the
    /// document. <see cref="SemanticTokenBuilder"/> does the packing.
    /// </summary>
    public sealed class SemanticTokens
    {
        /// <summary>The packed tokens. Converted to an <see cref="es5.Uint32Array"/> before Monaco sees it.</summary>
        public uint[] Data { get; set; }

        public SemanticTokens() { }

        public SemanticTokens(uint[] data)
        {
            Data = data;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Inline completions
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// One ghost-text suggestion, matching Monaco's <c>InlineCompletion</c> - the shape an
    /// AI-completion host produces.
    ///
    /// Leave <see cref="range"/> null to insert at the caret; set it to replace what is already there,
    /// which is what makes a suggestion that rewrites the current word possible.
    /// </summary>
    [ObjectLiteral]
    public class InlineCompletion
    {
        public string    insertText;
        public TextRange range;

        /// <summary>What the suggest widget filters on, when the two differ.</summary>
        public string    filterText;
    }
}
