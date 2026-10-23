using System;
using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The preferences a person can set for their own editors - the ones that are about how the editor looks
    /// and how much it helps, not about what it edits. They are what the <b>Editor Settings</b> entry of a
    /// <see cref="CodeEditor"/>'s context menu edits, and <see cref="EditorSettingsStore"/> keeps them in
    /// <c>localStorage</c>, so they follow the person to every editor on the origin and to the next visit.
    ///
    /// A fresh instance holds the package's own defaults. A host that wants different ones for its people (say,
    /// inlay hints off) passes an adjusted instance to <see cref="CodeEditor.SettingsDefaults"/>; those apply
    /// until the person saves a choice of their own, and are what "Reset to defaults" returns to.
    /// </summary>
    public sealed class EditorSettings
    {
        #region Appearance

        /// <summary>Font size in pixels.</summary>
        public int    FontSize = 12;

        /// <summary><c>"on"</c>, <c>"off"</c> or <c>"relative"</c>.</summary>
        public string LineNumbers = "on";

        public bool   Minimap;

        /// <summary>Soft-wraps long lines instead of scrolling horizontally.</summary>
        public bool   WordWrap;

        /// <summary><c>"none"</c>, <c>"boundary"</c>, <c>"selection"</c> or <c>"all"</c>.</summary>
        public string RenderWhitespace = "selection";

        public bool   IndentGuides            = true;
        public bool   BracketPairColorization = true;

        /// <summary>The enclosing scope pinned to the top while scrolling.</summary>
        public bool   StickyScroll = true;

        public bool   Folding       = true;
        public bool   FontLigatures = true;

        #endregion

        #region Assistance

        /// <summary><c>"on"</c>, <c>"off"</c> or <c>"offUnlessPressed"</c> (shown while Ctrl+Alt is held).</summary>
        public string InlayHints = "on";

        /// <summary>Documentation popups when the pointer rests on a word.</summary>
        public bool   Hover = true;

        /// <summary>The signature popup shown while typing the arguments of a call.</summary>
        public bool   ParameterHints = true;

        /// <summary>Open the suggestion list while typing, without asking for it.</summary>
        public bool   QuickSuggestions = true;

        /// <summary>Grey "ghost text" completions offered inline.</summary>
        public bool   InlineSuggestions = true;

        /// <summary>The "N references" line above a declaration.</summary>
        public bool   CodeLens = true;

        #endregion

        #region Editing

        public int    TabSize = 4;

        /// <summary>Indent with spaces rather than tab characters.</summary>
        public bool   InsertSpaces = true;

        public bool   FormatOnPaste;
        public bool   SmoothScrolling;

        /// <summary>Ctrl + mouse wheel changes the font size.</summary>
        public bool   MouseWheelZoom;

        #endregion

        public EditorSettings Clone()
        {
            return new EditorSettings
            {
                FontSize                = FontSize,
                LineNumbers             = LineNumbers,
                Minimap                 = Minimap,
                WordWrap                = WordWrap,
                RenderWhitespace        = RenderWhitespace,
                IndentGuides            = IndentGuides,
                BracketPairColorization = BracketPairColorization,
                StickyScroll            = StickyScroll,
                Folding                 = Folding,
                FontLigatures           = FontLigatures,
                InlayHints              = InlayHints,
                Hover                   = Hover,
                ParameterHints          = ParameterHints,
                QuickSuggestions        = QuickSuggestions,
                InlineSuggestions       = InlineSuggestions,
                CodeLens                = CodeLens,
                TabSize                 = TabSize,
                InsertSpaces            = InsertSpaces,
                FormatOnPaste           = FormatOnPaste,
                SmoothScrolling         = SmoothScrolling,
                MouseWheelZoom          = MouseWheelZoom
            };
        }

        /// <summary>
        /// Writes these settings into a Monaco options object - the construction options, or an empty patch for
        /// <c>updateOptions</c>. A nested option is changed in place when the object already has one, so what the
        /// editor set around the field (the hover delay, the minimap side) survives.
        /// </summary>
        internal void WriteTo(EditorOptions o)
        {
            o.fontSize                = FontSize;
            o.lineNumbers             = LineNumbers;
            o.wordWrap                = WordWrap ? "on" : "off";
            o.renderWhitespace        = RenderWhitespace;
            o.folding                 = Folding;
            o.fontLigatures           = FontLigatures;
            o.quickSuggestions        = QuickSuggestions;
            o.tabSize                 = TabSize;
            o.insertSpaces            = InsertSpaces;
            o.detectIndentation       = false;
            o.formatOnPaste           = FormatOnPaste;
            o.smoothScrolling         = SmoothScrolling;
            o.mouseWheelZoom          = MouseWheelZoom;
            o.codeLens                = CodeLens;

            o.minimap                 = o.minimap ?? new MinimapOptions();
            o.minimap.enabled         = Minimap;

            o.stickyScroll            = o.stickyScroll ?? new StickyScrollOptions();
            o.stickyScroll.enabled    = StickyScroll;

            o.guides                  = o.guides ?? new GuidesOptions();
            o.guides.indentation      = IndentGuides;
            o.guides.highlightActiveIndentation = IndentGuides;

            o.bracketPairColorization = o.bracketPairColorization ?? new BracketPairColorizationOptions();
            o.bracketPairColorization.enabled = BracketPairColorization;

            o.inlayHints              = o.inlayHints ?? new InlayHintsOptions();
            o.inlayHints.enabled      = InlayHints;

            o.hover                   = o.hover ?? new HoverOptions();
            o.hover.enabled           = Hover;

            o.parameterHints          = o.parameterHints ?? new ParameterHintsOptions();
            o.parameterHints.enabled  = ParameterHints;

            o.inlineSuggest           = o.inlineSuggest ?? new InlineSuggestOptions();
            o.inlineSuggest.enabled   = InlineSuggestions;
        }

        // The stored form is an [ObjectLiteral] record with one nullable field per setting, so a value an older
        // version never wrote is simply null and keeps the baseline it is read over.
        internal object ToPlainObject()
        {
            return new EditorSettingsRecord
            {
                fontSize                = FontSize,
                lineNumbers             = LineNumbers,
                minimap                 = Minimap,
                wordWrap                = WordWrap,
                renderWhitespace        = RenderWhitespace,
                indentGuides            = IndentGuides,
                bracketPairColorization = BracketPairColorization,
                stickyScroll            = StickyScroll,
                folding                 = Folding,
                fontLigatures           = FontLigatures,
                inlayHints              = InlayHints,
                hover                   = Hover,
                parameterHints          = ParameterHints,
                quickSuggestions        = QuickSuggestions,
                inlineSuggestions       = InlineSuggestions,
                codeLens                = CodeLens,
                tabSize                 = TabSize,
                insertSpaces            = InsertSpaces,
                formatOnPaste           = FormatOnPaste,
                smoothScrolling         = SmoothScrolling,
                mouseWheelZoom          = MouseWheelZoom
            };
        }

        /// <summary>The stored object read over <paramref name="baseline"/>: keys it lacks keep the baseline's value.</summary>
        internal static EditorSettings FromPlainObject(object plain, EditorSettings baseline)
        {
            var s = baseline.Clone();

            if (plain is null) return s;

            // A cast to an [ObjectLiteral] hands a plain object straight back - see EditorView.FromPlainObject.
            var r = (EditorSettingsRecord)plain;

            s.FontSize                = r.fontSize                ?? s.FontSize;
            s.LineNumbers             = r.lineNumbers             ?? s.LineNumbers;
            s.Minimap                 = r.minimap                 ?? s.Minimap;
            s.WordWrap                = r.wordWrap                ?? s.WordWrap;
            s.RenderWhitespace        = r.renderWhitespace        ?? s.RenderWhitespace;
            s.IndentGuides            = r.indentGuides            ?? s.IndentGuides;
            s.BracketPairColorization = r.bracketPairColorization ?? s.BracketPairColorization;
            s.StickyScroll            = r.stickyScroll            ?? s.StickyScroll;
            s.Folding                 = r.folding                 ?? s.Folding;
            s.FontLigatures           = r.fontLigatures           ?? s.FontLigatures;
            s.InlayHints              = r.inlayHints              ?? s.InlayHints;
            s.Hover                   = r.hover                   ?? s.Hover;
            s.ParameterHints          = r.parameterHints          ?? s.ParameterHints;
            s.QuickSuggestions        = r.quickSuggestions        ?? s.QuickSuggestions;
            s.InlineSuggestions       = r.inlineSuggestions       ?? s.InlineSuggestions;
            s.CodeLens                = r.codeLens                ?? s.CodeLens;
            s.TabSize                 = r.tabSize                 ?? s.TabSize;
            s.InsertSpaces            = r.insertSpaces            ?? s.InsertSpaces;
            s.FormatOnPaste           = r.formatOnPaste           ?? s.FormatOnPaste;
            s.SmoothScrolling         = r.smoothScrolling         ?? s.SmoothScrolling;
            s.MouseWheelZoom          = r.mouseWheelZoom          ?? s.MouseWheelZoom;

            return s;
        }
    }
}

namespace Tesserae.Monaco
{
    /// <summary>The shape <see cref="EditorSettings"/> is stored in. Every field is nullable so a missing key reads as "not set".</summary>
    [ObjectLiteral]
    internal class EditorSettingsRecord
    {
        public int?    fontSize;
        public string  lineNumbers;
        public bool?   minimap;
        public bool?   wordWrap;
        public string  renderWhitespace;
        public bool?   indentGuides;
        public bool?   bracketPairColorization;
        public bool?   stickyScroll;
        public bool?   folding;
        public bool?   fontLigatures;
        public string  inlayHints;
        public bool?   hover;
        public bool?   parameterHints;
        public bool?   quickSuggestions;
        public bool?   inlineSuggestions;
        public bool?   codeLens;
        public int?    tabSize;
        public bool?   insertSpaces;
        public bool?   formatOnPaste;
        public bool?   smoothScrolling;
        public bool?   mouseWheelZoom;
    }
}
