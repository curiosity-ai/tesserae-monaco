using System;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A read-only Monaco surface for displaying code: syntax highlighting, selection and copying,
    /// but none of the editing affordances (no suggest widget, no error squiggles, no formatting).
    ///
    /// Use <see cref="CodeEditor"/> when the user is meant to type. Create one with
    /// <see cref="MonacoEditor.Viewer(bool)"/>.
    /// </summary>
    [Transpose.Name("tssm.CodeViewer")]
    public sealed class CodeViewer : MonacoComponent
    {
        private readonly bool            _autoHeight;
        private          string          _text     = "";
        private          string          _language = "";
        private          bool            _wordWrap;
        private          bool            _editable;
        private          Action<EditorOptions> _configureOptions;
        private          Action<CodeViewer>   _onRendered;

        internal CodeViewer(bool autoHeight)
        {
            _autoHeight = autoHeight;
        }

        /// <summary>
        /// The displayed text. Reads straight from the live model once the viewer is mounted, so it
        /// reflects anything the user changed if <see cref="Editable"/> was turned on.
        /// </summary>
        public string Text
        {
            get => Editor is null ? _text : Editor.getValue();
            set
            {
                _text = value ?? "";

                Editor?.setValue(_text);
            }
        }

        /// <summary>Sets the displayed text, skipping the write when it is unchanged.</summary>
        public CodeViewer SetText(string text)
        {
            if (Text != text) Text = text;

            return this;
        }

        /// <summary>
        /// Sets the syntax-highlighting language by Monaco language id (<c>"csharp"</c>,
        /// <c>"json"</c>, <c>"typescript"</c>, …). Left unset, no highlighting is applied - which is
        /// the right default for plain text.
        /// </summary>
        public CodeViewer SetLanguage(string language)
        {
            _language = language ?? "";

            if (Editor != null)
            {
                MonacoApi.editor.setModelLanguage(Editor.getModel(), _language);
            }

            return this;
        }

        /// <summary>Registers <paramref name="language"/> if needed, then selects it.</summary>
        public CodeViewer SetLanguage(LanguageDefinition language)
        {
            MonacoEditor.RegisterLanguage(language);

            return SetLanguage(language?.Id);
        }

        /// <summary>
        /// Picks the language from a file extension (with or without the leading dot), leaving the
        /// current language alone if Monaco has no match. Only takes effect once mounted, since it
        /// needs Monaco's language registry.
        /// </summary>
        public CodeViewer SetLanguageByExtension(string extension)
        {
            if (MonacoEditor.TryGetLanguageIdForExtension(extension, out var languageId))
            {
                SetLanguage(languageId);
            }

            return this;
        }

        /// <summary>Soft-wraps long lines instead of scrolling horizontally.</summary>
        public CodeViewer WordWrap(bool wordWrap = true)
        {
            _wordWrap = wordWrap;

            Editor?.updateOptions(new EditorOptions { wordWrap = wordWrap ? "on" : "off" });

            return this;
        }

        /// <summary>
        /// Allows typing. A viewer that is editable is still just a viewer - it has no completion,
        /// hover or diagnostics wiring; reach for <see cref="CodeEditor"/> for that.
        /// </summary>
        public CodeViewer Editable(bool editable = true)
        {
            _editable = editable;

            Editor?.updateOptions(new EditorOptions { readOnly = !editable });

            return this;
        }

        /// <summary>
        /// Adjusts the Monaco construction options before the viewer is created - the escape hatch
        /// for options this wrapper doesn't surface. <see cref="EditorOptions"/> covers the common
        /// ones; it is a plain JavaScript object at runtime, so
        /// <c>((dynamic)options).someOption = value</c> reaches the rest.
        /// </summary>
        public CodeViewer Options(Action<EditorOptions> configureOptions)
        {
            _configureOptions = configureOptions;

            return this;
        }

        /// <summary>Runs once the underlying editor exists.</summary>
        public CodeViewer OnRendered(Action<CodeViewer> onRendered)
        {
            _onRendered = onRendered;

            return this;
        }

        /// <summary>The underlying Monaco editor, or null before mount.</summary>
        public IStandaloneCodeEditor Editor => (IStandaloneCodeEditor)Instance;

        protected override IEditor Create(HTMLElement container)
        {
            var options = BuildBaseOptions(_language, _text, readOnly: !_editable, wordWrap: _wordWrap, autoHeight: _autoHeight);

            // A viewer is for reading: no suggest widget, and no gutter noise it can't act on.
            options.quickSuggestions = false;
            options.occurrencesHighlight = "off";
            options.renderLineHighlight  = "none";
            options.contextmenu          = _editable;

            _configureOptions?.Invoke(options);

            ApplyOverflowWidgetsHost(options);

            return MonacoApi.editor.create(container, options);
        }

        protected override void AfterCreate()
        {
            if (_autoHeight) EnableAutoHeight();

            _onRendered?.Invoke(this);
        }
    }
}
