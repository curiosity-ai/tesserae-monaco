using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>Which lane of the overview ruler a decoration paints in, matching Monaco's <c>OverviewRulerLane</c>.</summary>
    [Enum(Emit.Value)]
    public enum OverviewRulerLane
    {
        Left   = 1,
        Center = 2,
        Right  = 4,
        Full   = 7
    }

    /// <summary>Where a decoration paints in the minimap, matching Monaco's <c>MinimapPosition</c>.</summary>
    [Enum(Emit.Value)]
    public enum MinimapPosition
    {
        Inline = 1,
        Gutter = 2
    }

    /// <summary>Where the caret may stop around injected text, matching Monaco's <c>InjectedTextCursorStops</c>.</summary>
    [Enum(Emit.Value)]
    public enum InjectedTextCursorStops
    {
        Both  = 0,
        Right = 1,
        Left  = 2,
        None  = 3
    }

    /// <summary>
    /// A mark in the overview ruler - the thin strip right of the scrollbar - matching Monaco's
    /// <c>IModelDecorationOverviewRulerOptions</c>.
    ///
    /// <see cref="position"/> has no meaningful zero, so set it: an unset lane paints nothing, which
    /// looks exactly like a decoration that failed to apply.
    /// </summary>
    [ObjectLiteral]
    public class OverviewRulerOptions
    {
        /// <summary>A CSS colour, or a theme colour id such as <c>"editorError.foreground"</c>.</summary>
        public string            color;
        public OverviewRulerLane position;
    }

    /// <summary>A mark in the minimap, matching Monaco's <c>IModelDecorationMinimapOptions</c>.</summary>
    [ObjectLiteral]
    public class DecorationMinimapOptions
    {
        /// <summary>A CSS colour, or a theme colour id.</summary>
        public string          color;
        public MinimapPosition position;
    }

    /// <summary>
    /// Text Monaco paints inside the line without it being part of the document, matching Monaco's
    /// <c>InjectedTextOptions</c> - the mechanism behind inlay hints. Attach via
    /// <see cref="DecorationOptions.before"/> or <see cref="DecorationOptions.after"/>.
    /// </summary>
    [ObjectLiteral]
    public class InjectedTextOptions
    {
        public string                  content;
        public string                  inlineClassName;
        public bool                    inlineClassNameAffectsLetterSpacing;
        public InjectedTextCursorStops cursorStops;
    }

    /// <summary>
    /// How a decorated range is painted, matching Monaco's <c>IModelDecorationOptions</c>.
    ///
    /// Every field is optional. The class names are ordinary CSS classes resolved against the page's
    /// stylesheets, so the host supplies the styling - this package ships none, the same way it ships
    /// no language intelligence.
    ///
    /// Note the defaults that fall out of an unset field: <see cref="stickiness"/> is
    /// <see cref="TrackedRangeStickiness.AlwaysGrowsWhenTypingAtEdges"/>, which is Monaco's own
    /// default, and <see cref="zIndex"/> is 0.
    /// </summary>
    [ObjectLiteral]
    public class DecorationOptions
    {
        /// <summary>Class applied to the whole decorated range.</summary>
        public string className;

        /// <summary>Class applied to the text of the range, for colour and weight.</summary>
        public string inlineClassName;

        /// <summary>Whether <see cref="inlineClassName"/> may change letter spacing - Monaco needs to know, to re-measure.</summary>
        public bool inlineClassNameAffectsLetterSpacing;

        /// <summary>Decorate the entire line rather than just the range.</summary>
        public bool isWholeLine;

        /// <summary>Class for an icon in the glyph margin. Needs <c>glyphMargin: true</c> on the editor.</summary>
        public string glyphMarginClassName;

        /// <summary>Tooltip shown over the glyph-margin icon.</summary>
        public MarkdownString glyphMarginHoverMessage;

        /// <summary>Tooltip shown over the decorated range.</summary>
        public MarkdownString hoverMessage;

        /// <summary>Class applied to the line number.</summary>
        public string lineNumberClassName;

        /// <summary>Class for the narrow strip between the line numbers and the text.</summary>
        public string linesDecorationsClassName;

        /// <summary>As <see cref="linesDecorationsClassName"/>, but only on the range's first line.</summary>
        public string firstLineDecorationClassName;

        /// <summary>Class for the whole margin.</summary>
        public string marginClassName;

        /// <summary>Class for generated content placed before the range.</summary>
        public string beforeContentClassName;

        /// <summary>Class for generated content placed after the range.</summary>
        public string afterContentClassName;

        /// <summary>Class for the block covering the range's lines.</summary>
        public string blockClassName;

        /// <summary>
        /// Keep painting the decoration when its range collapses.
        ///
        /// Required for <see cref="before"/> and <see cref="after"/> to appear at all when the range is
        /// empty - which an insertion point always is. Monaco treats an empty range as collapsed and
        /// paints nothing for it otherwise, so injected text silently does not render.
        /// </summary>
        public bool showIfCollapsed;

        /// <summary>
        /// A label for the decoration, surfaced in Monaco's own diagnostics. Optional, and useful when
        /// several sources decorate one document.
        /// </summary>
        public string description;

        /// <summary>How the range grows as the user types at its edges.</summary>
        public TrackedRangeStickiness stickiness;

        /// <summary>Paint order against other decorations on the same range.</summary>
        public int zIndex;

        /// <summary>A mark in the minimap.</summary>
        public DecorationMinimapOptions minimap;

        /// <summary>A mark in the overview ruler.</summary>
        public OverviewRulerOptions overviewRuler;

        /// <summary>
        /// Text painted before the range without being part of the document. Needs
        /// <see cref="showIfCollapsed"/> when the range is empty.
        /// </summary>
        public InjectedTextOptions before;

        /// <summary>
        /// Text painted after the range without being part of the document. Needs
        /// <see cref="showIfCollapsed"/> when the range is empty.
        ///
        /// For annotations a language backend produces, prefer <c>OnInlayHints</c>: Monaco owns their
        /// placement and refresh, and asks only for the visible range.
        /// </summary>
        public InjectedTextOptions after;
    }

    /// <summary>
    /// One decoration to apply: a range and how to paint it. Matches Monaco's
    /// <c>IModelDeltaDecoration</c>.
    /// </summary>
    [ObjectLiteral]
    public class TextDecoration
    {
        public TextRange         range;
        public DecorationOptions options;
    }

    /// <summary>
    /// Shorthands for the decorations a host actually reaches for, so the common cases don't need
    /// the full <see cref="DecorationOptions"/> shape spelled out.
    /// </summary>
    public static class Decoration
    {
        /// <summary>Decorates a whole line.</summary>
        public static TextDecoration Line(int lineNumber, string className)
        {
            return new TextDecoration
            {
                range   = Ranges.Line(lineNumber),
                options = new DecorationOptions { className = className, isWholeLine = true }
            };
        }

        /// <summary>Decorates a range's text.</summary>
        public static TextDecoration Range(TextRange range, string inlineClassName)
        {
            return new TextDecoration
            {
                range   = range,
                options = new DecorationOptions { inlineClassName = inlineClassName }
            };
        }

        /// <summary>Puts an icon in the glyph margin next to a line. Needs <c>GlyphMargin(true)</c> on the editor.</summary>
        public static TextDecoration Glyph(int lineNumber, string glyphMarginClassName, string tooltip = null)
        {
            return new TextDecoration
            {
                range   = Ranges.Line(lineNumber),
                options = new DecorationOptions
                {
                    glyphMarginClassName    = glyphMarginClassName,
                    glyphMarginHoverMessage = tooltip is null ? null : new MarkdownString { value = tooltip }
                }
            };
        }

        /// <summary>Marks a range in the overview ruler, for a hit that should be findable while scrolled away.</summary>
        public static TextDecoration RulerMark(TextRange range, string color, OverviewRulerLane lane = OverviewRulerLane.Right)
        {
            return new TextDecoration
            {
                range   = range,
                options = new DecorationOptions
                {
                    overviewRuler = new OverviewRulerOptions { color = color, position = lane }
                }
            };
        }

        /// <summary>
        /// Paints read-only text after a position, without it becoming part of the document.
        ///
        /// <c>showIfCollapsed</c> is set for you: the range is an insertion point, so it is empty, and
        /// Monaco paints nothing for a collapsed decoration unless told to - which makes a note left
        /// without it look like a decoration that never applied.
        ///
        /// Reach for <c>OnInlayHints</c> instead when the annotations come from a language backend.
        /// </summary>
        public static TextDecoration InlineNote(Position position, string content, string className = null)
        {
            return new TextDecoration
            {
                range   = Ranges.At(position),
                options = new DecorationOptions
                {
                    after           = new InjectedTextOptions { content = content, inlineClassName = className },
                    showIfCollapsed = true
                }
            };
        }
    }

    /// <summary>Building <see cref="TextRange"/>s without spelling out four fields every time.</summary>
    public static class Ranges
    {
        /// <summary>A whole line. The end column is deliberately large: Monaco clamps it to the line's length.</summary>
        public static TextRange Line(int lineNumber)
        {
            return new TextRange
            {
                startLineNumber = lineNumber,
                startColumn     = 1,
                endLineNumber   = lineNumber,
                endColumn       = 1_000_000
            };
        }

        /// <summary>A span of whole lines.</summary>
        public static TextRange Lines(int startLineNumber, int endLineNumber)
        {
            return new TextRange
            {
                startLineNumber = startLineNumber,
                startColumn     = 1,
                endLineNumber   = endLineNumber,
                endColumn       = 1_000_000
            };
        }

        /// <summary>An explicit range, in Monaco's one-based coordinates.</summary>
        public static TextRange Of(int startLineNumber, int startColumn, int endLineNumber, int endColumn)
        {
            return new TextRange
            {
                startLineNumber = startLineNumber,
                startColumn     = startColumn,
                endLineNumber   = endLineNumber,
                endColumn       = endColumn
            };
        }

        /// <summary>The empty range at a position - an insertion point.</summary>
        public static TextRange At(Position position)
        {
            return new TextRange
            {
                startLineNumber = position.lineNumber,
                startColumn     = position.column,
                endLineNumber   = position.lineNumber,
                endColumn       = position.column
            };
        }
    }
}
