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
    /// want. For anything else, <c>((dynamic)options).someOption = value</c> still works, since this
    /// is a plain JavaScript object at runtime.
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

        public string      snippetSuggestions;
        public double      suggestFontSize;
        public string      acceptSuggestionOnEnter;
        public string      tabCompletion;

        /// <summary>
        /// Monaco also accepts a per-context object here; this declares the boolean form, which is
        /// the common one. Reach the object form through the <c>dynamic</c> escape hatch.
        /// </summary>
        public bool        quickSuggestions;

        public MinimapOptions                 minimap;
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

        public bool        fixedOverflowWidgets;
        public HTMLElement overflowWidgetsDomNode;
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

    /// <summary>The pair of models a diff editor compares, as passed to <c>diffEditor.setModel</c>.</summary>
    [ObjectLiteral]
    public class DiffEditorModel
    {
        public ITextModel original;
        public ITextModel modified;
    }
}
