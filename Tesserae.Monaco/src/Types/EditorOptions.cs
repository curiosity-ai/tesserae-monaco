using System;
using Transpose;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Monaco's <c>IStandaloneEditorConstructionOptions</c>.
    ///
    /// An <c>[ObjectLiteral]</c> emits only the fields that are actually assigned, so the same type
    /// serves both construction and <c>updateOptions</c>, where anything left alone must stay
    /// untouched: <c>new EditorOptions { readOnly = true }</c> is exactly <c>{ readOnly: true }</c>.
    ///
    /// A subset of Monaco's options - the ones this package sets, plus the ones a host is likely to
    /// want. For anything else, a component's <c>SetRawOption(name, value)</c> sets it by name; that is
    /// the one place a JavaScript option name is still a string.
    /// </summary>
    [ObjectLiteral]
    public class EditorOptions
    {
        public string      value;
        public string      language;
        public string      theme;
        public bool        readOnly;
        public bool        automaticLayout;

        public string      fontFamily;
        public double      fontSize;
        public bool        fontLigatures;
        public double      lineHeight;
        public double      letterSpacing;

        public string      lineNumbers;
        public bool        glyphMargin;
        public bool        folding;
        public bool        roundedSelection;
        public string      renderLineHighlight;
        public string      renderWhitespace;
        public bool        renderControlCharacters;
        public string      occurrencesHighlight;
        public bool        contextmenu;
        public bool        links;
        public bool        dragAndDrop;
        public bool        mouseWheelZoom;
        public bool        formatOnPaste;
        public bool        formatOnType;

        public string      wordWrap;
        public string      wrappingIndent;
        public bool        scrollBeyondLastLine;

        /// <summary>How many columns past the longest line the editor can scroll.</summary>
        public double      scrollBeyondLastColumn;

        /// <summary>Automatic indentation: <c>"none"</c>, <c>"keep"</c>, <c>"brackets"</c>, <c>"advanced"</c> or <c>"full"</c>.</summary>
        public string      autoIndent;

        public string      cursorStyle;
        public string      cursorBlinking;
        public bool        smoothScrolling;
        public bool        domReadOnly;
        public string      ariaLabel;
        public string      accessibilitySupport;
        public bool        stickyTabStops;
        public int[]       rulers;

        /// <summary>Text shown when the document is empty.</summary>
        public string      placeholder;

        /// <summary>Markdown shown when the user types into a read-only editor, instead of nothing happening.</summary>
        public MarkdownString readOnlyMessage;

        public string      snippetSuggestions;
        public double      suggestFontSize;
        public string      acceptSuggestionOnEnter;
        public string      tabCompletion;

        /// <summary>
        /// Monaco also accepts a per-context object here; this declares the boolean form, which is
        /// the common one. Reach the object form through <c>SetRawOption</c>.
        /// </summary>
        public bool        quickSuggestions;

        public double      quickSuggestionsDelay;

        /// <summary>Whether punctuation and other non-word characters re-open the suggest widget.</summary>
        public bool        suggestOnTriggerCharacters;

        /// <summary>
        /// Suggestions drawn from the words already in the document: <c>"off"</c>, <c>"currentDocument"</c>,
        /// <c>"matchingDocuments"</c> or <c>"allDocuments"</c>. Turn it off for a language whose completions
        /// come from a backend, or the two lists interleave.
        /// </summary>
        public string      wordBasedSuggestions;

        public MinimapOptions                 minimap;
        public StickyScrollOptions            stickyScroll;
        public GuidesOptions                  guides;
        public UnicodeHighlightOptions        unicodeHighlight;
        public ScrollbarOptions               scrollbar;
        public SuggestOptions                 suggest;
        public InlineSuggestOptions           inlineSuggest;
        public HoverOptions                   hover;
        public BracketPairColorizationOptions bracketPairColorization;
        public PaddingOptions                 padding;

        /// <summary>Renders suggest/hover popups into <see cref="overflowWidgetsDomNode"/> instead of clipping them.</summary>
        public bool        fixedOverflowWidgets;

        /// <summary>Where the overflow widgets are rendered - see <c>MonacoEditor.GetOverflowWidgetsHost</c>.</summary>
        public HTMLElement overflowWidgetsDomNode;
    }

    /// <summary>Monaco's <c>IDiffEditorConstructionOptions</c>.</summary>
    [ObjectLiteral]
    public class DiffEditorOptions
    {
        public string      theme;
        public bool        readOnly;
        public bool        originalEditable;
        public bool        automaticLayout;

        public bool        renderSideBySide;
        public bool        ignoreTrimWhitespace;
        public bool        renderIndicators;
        public bool        renderOverviewRuler;
        public bool        renderMarginRevertIcon;
        public bool        enableSplitViewResizing;
        public string      diffWordWrap;
        public double      maxComputationTime;

        public string      fontFamily;
        public double      fontSize;
        public string      wordWrap;
        public bool        scrollBeyondLastLine;

        public MinimapOptions                 minimap;
        public ScrollbarOptions               scrollbar;
        public BracketPairColorizationOptions bracketPairColorization;
        public HideUnchangedRegionsOptions    hideUnchangedRegions;
        public DiffExperimentalOptions        experimental;

        public bool        fixedOverflowWidgets;
        public HTMLElement overflowWidgetsDomNode;
    }

    /// <summary>
    /// Monaco's <c>hideUnchangedRegions</c> settings - collapsing long identical runs to a few lines of
    /// context with a band to expand them.
    /// </summary>
    [ObjectLiteral]
    public class HideUnchangedRegionsOptions
    {
        public bool enabled;
        public int  contextLineCount;
        public int  minimumLineCount;
        public int  revealLineCount;
    }

    /// <summary>The diff editor's experimental settings. <c>showMoves</c> draws moved blocks as moves.</summary>
    [ObjectLiteral]
    public class DiffExperimentalOptions
    {
        public bool showMoves;
        public bool showEmptyDecorations;
    }

    /// <summary>Monaco's <c>ITextModelUpdateOptions</c>.</summary>
    [ObjectLiteral]
    public class TextModelOptions
    {
        public int  tabSize;
        public bool insertSpaces;
        public bool trimAutoWhitespace;
    }

    [ObjectLiteral]
    public class MinimapOptions
    {
        public bool   enabled;
        public string side;
        public bool   renderCharacters;
    }

    [ObjectLiteral]
    public class ScrollbarOptions
    {
        /// <summary>Off, the wheel keeps scrolling the page once the editor has nothing left to scroll.</summary>
        public bool   alwaysConsumeMouseWheel;
        public string vertical;
        public string horizontal;
        public bool   useShadows;
    }

    [ObjectLiteral]
    public class SuggestOptions
    {
        public bool   preview;
        public bool   showWords;
        public bool   filterGraceful;
        public string insertMode;
    }

    [ObjectLiteral]
    public class InlineSuggestOptions
    {
        public bool   enabled;
        public string showToolbar;
    }

    [ObjectLiteral]
    public class HoverOptions
    {
        public bool   enabled;
        public bool   sticky;
        public double delay;
        public double hidingDelay;
    }

    [ObjectLiteral]
    public class BracketPairColorizationOptions
    {
        public bool enabled;
    }

    /// <summary>Monaco's <c>IEditorStickyScrollOptions</c> - the enclosing scope pinned to the top.</summary>
    [ObjectLiteral]
    public class StickyScrollOptions
    {
        public bool   enabled;
        public int    maxLineCount;

        /// <summary><c>"outlineModel"</c>, <c>"foldingProviderModel"</c> or <c>"indentationModel"</c>.</summary>
        public string defaultModel;
    }

    /// <summary>Monaco's <c>IGuidesOptions</c> - the indentation and bracket guide lines.</summary>
    [ObjectLiteral]
    public class GuidesOptions
    {
        public bool indentation;
        public bool highlightActiveIndentation;
        public bool bracketPairs;
        public bool bracketPairsHorizontal;
    }

    /// <summary>Monaco's <c>IUnicodeHighlightOptions</c> - flagging confusable and invisible characters.</summary>
    [ObjectLiteral]
    public class UnicodeHighlightOptions
    {
        public bool ambiguousCharacters;
        public bool invisibleCharacters;
        public bool nonBasicASCII;
    }

    [ObjectLiteral]
    public class PaddingOptions
    {
        public double top;
        public double bottom;
    }

    /// <summary>
    /// An entry in a theme's <c>rules</c> array - one Monarch token type and how to colour it.
    /// </summary>
    [ObjectLiteral]
    public class ThemeRule
    {
        public string token;

        /// <summary>Hex <b>without</b> the leading <c>#</c>, as Monaco requires.</summary>
        public string foreground;

        public string background;

        /// <summary>Any of <c>"italic"</c>, <c>"bold"</c>, <c>"underline"</c>, or a space-separated combination.</summary>
        public string fontStyle;
    }

    /// <summary>Monaco's <c>IStandaloneThemeData</c>, as passed to <c>monaco.editor.defineTheme</c>.</summary>
    [ObjectLiteral]
    public class StandaloneThemeData
    {
        /// <summary>
        /// The built-in theme to build on: <c>"vs"</c>, <c>"vs-dark"</c> or <c>"hc-black"</c>.
        /// Named <c>baseTheme</c> because <c>base</c> is a C# keyword; <c>[Name]</c> puts the
        /// JavaScript name back.
        /// </summary>
        [Name("base")]
        public string baseTheme;

        public bool        inherit;

        /// <summary>
        /// Whether a semantic-tokens provider's output is coloured by the rules below. Monaco's editor
        /// option defaults to <c>"configuredByTheme"</c>, so a theme that leaves this unset means a
        /// registered provider is never asked at all.
        /// </summary>
        public bool        semanticHighlighting;

        public ThemeRule[] rules;

        /// <summary>
        /// Workbench colour overrides. Left as a plain object because its keys are dotted
        /// (<c>editor.background</c>), which is neither a legal C# identifier nor something
        /// <c>[Name]</c> can express - a renamed field would emit as a nested property access.
        /// Fill it with <see cref="ThemeColors"/>.
        /// </summary>
        public object colors;
    }

    /// <summary>
    /// The <c>colors</c> map of a theme. Its keys are dotted, so they are set by key rather than
    /// declared as fields - the one place in the package where a JavaScript name is still a string.
    /// </summary>
    [ObjectLiteral]
    public class ThemeColors
    {
    }

    /// <summary>
    /// Setting a theme colour by key. An extension rather than a method on
    /// <see cref="ThemeColors"/> itself, because an <c>[ObjectLiteral]</c> is a bare
    /// <c>{ }</c> at runtime and carries no methods of its own.
    /// </summary>
    public static class ThemeColorsExtensions
    {
        /// <summary>Sets one colour, e.g. <c>Set("editor.background", "#1e1e1e")</c>.</summary>
        public static ThemeColors Set(this ThemeColors colors, string key, string color)
        {
            Script.Set(colors, key, color);

            return colors;
        }
    }

    /// <summary>Monaco's <c>IActionDescriptor</c>, as passed to <c>editor.addAction</c>.</summary>
    [ObjectLiteral]
    public class EditorAction
    {
        public string id;
        public string label;

        /// <summary>Keybindings, each a <c>KeyMod</c> bitmask OR-ed with a <c>KeyCode</c>.</summary>
        public int[]  keybindings;

        public string precondition;
        public string keybindingContext;
        public string contextMenuGroupId;
        public double contextMenuOrder;

        public Action<IStandaloneCodeEditor> run;
    }

    /// <summary>Monaco's <c>ILanguageExtensionPoint</c>, as passed to <c>monaco.languages.register</c>.</summary>
    [ObjectLiteral]
    public class LanguageRegistration
    {
        public string   id;
        public string[] aliases;
        public string[] extensions;
        public string[] filenames;
        public string[] mimetypes;
        public string   firstLine;
    }

    /// <summary>
    /// What <c>monaco.languages.registerTokensProviderFactory</c> takes: an object with a single
    /// <c>create</c> that Monaco calls the first time a document uses the language. It may hand back
    /// the Monarch grammar directly or a promise of one, which is what makes a grammar that lives in
    /// its own script file loadable on demand.
    /// </summary>
    [ObjectLiteral]
    public class TokensProviderFactory
    {
        public Func<object> create;
    }

    /// <summary>The pair of models a diff editor compares, as passed to <c>diffEditor.setModel</c>.</summary>
    [ObjectLiteral]
    public class DiffEditorModel
    {
        public ITextModel original;
        public ITextModel modified;
    }
}
