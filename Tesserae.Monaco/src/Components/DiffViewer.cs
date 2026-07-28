using System;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A Monaco diff of two documents, side by side or inline, with the usual gutter markers and
    /// change navigation. Create one with <see cref="MonacoEditor.Diff()"/>.
    ///
    /// A diff editor owns two models rather than one. They are created here and disposed with the
    /// component: Monaco does not dispose the models it is handed, so leaving that out leaks a model
    /// per rendered diff - which is easy to miss, because nothing visibly breaks.
    /// </summary>
    [Transpose.Name("tssm.DiffViewer")]
    public sealed class DiffViewer : MonacoComponent
    {
        private string          _original = "";
        private string          _modified = "";
        private string          _language = "";
        private bool            _readOnly = true;
        private bool            _sideBySide = true;
        private bool            _ignoreTrimWhitespace = true;
        private bool            _renderIndicators = true;
        private Action<dynamic> _configureOptions;
        private Action<DiffViewer> _onRendered;

        // The two models backing the comparison, owned by this component.
        private object _originalModel;
        private object _modifiedModel;

        internal DiffViewer() { }

        /// <summary>The left-hand (baseline) document.</summary>
        public string Original
        {
            get => _originalModel is null ? _original : Script.Write<string>("{0}.getValue()", _originalModel);
            set
            {
                _original = value ?? "";

                if (_originalModel is object)
                {
                    Script.Write("{0}.setValue({1})", _originalModel, _original);
                }
            }
        }

        /// <summary>The right-hand (changed) document.</summary>
        public string Modified
        {
            get => _modifiedModel is null ? _modified : Script.Write<string>("{0}.getValue()", _modifiedModel);
            set
            {
                _modified = value ?? "";

                if (_modifiedModel is object)
                {
                    Script.Write("{0}.setValue({1})", _modifiedModel, _modified);
                }
            }
        }

        /// <summary>Sets the left-hand (baseline) document.</summary>
        public DiffViewer SetOriginal(string original)
        {
            Original = original;

            return this;
        }

        /// <summary>Sets the right-hand (changed) document.</summary>
        public DiffViewer SetModified(string modified)
        {
            Modified = modified;

            return this;
        }

        /// <summary>Sets both sides at once - the common case when showing a stored comparison.</summary>
        public DiffViewer SetContent(string original, string modified)
        {
            return SetOriginal(original).SetModified(modified);
        }

        /// <summary>
        /// Sets the language for both sides. Recreates the models when the diff is already mounted,
        /// because a model's language is fixed at creation.
        /// </summary>
        public DiffViewer SetLanguage(string language)
        {
            _language = language ?? "";

            if (Instance is object)
            {
                // Capture the live text first - switching language must not silently revert edits.
                _original = Original;
                _modified = Modified;

                SetModels();
            }

            return this;
        }

        /// <summary>Registers <paramref name="language"/> if needed, then selects it for both sides.</summary>
        public DiffViewer SetLanguage(LanguageDefinition language)
        {
            MonacoEditor.RegisterLanguage(language);

            return SetLanguage(language?.Id);
        }

        /// <summary>Picks the language from a file extension, if Monaco knows one for it.</summary>
        public DiffViewer SetLanguageByExtension(string extension)
        {
            if (MonacoEditor.TryGetLanguageIdForExtension(extension, out var languageId))
            {
                SetLanguage(languageId);
            }

            return this;
        }

        /// <summary>
        /// Two panes (the default) versus a single inline pane. Inline suits narrow containers, where
        /// two panes leave each side too cramped to read.
        /// </summary>
        public DiffViewer SideBySide(bool sideBySide = true)
        {
            _sideBySide = sideBySide;

            if (Instance is object)
            {
                Script.Write("{0}.updateOptions({ renderSideBySide: {1} })", Instance, sideBySide);
            }

            return this;
        }

        /// <summary>Shows the whole diff in one pane instead of two.</summary>
        public DiffViewer Inline() => SideBySide(false);

        /// <summary>
        /// Whether whitespace-only changes are treated as no change. On by default, which keeps
        /// re-indentation from swamping the real edits; turn it off when whitespace is the point.
        /// </summary>
        public DiffViewer IgnoreTrimWhitespace(bool ignore = true)
        {
            _ignoreTrimWhitespace = ignore;

            if (Instance is object)
            {
                Script.Write("{0}.updateOptions({ ignoreTrimWhitespace: {1} })", Instance, ignore);
            }

            return this;
        }

        /// <summary>Shows the +/- indicators in the gutter.</summary>
        public DiffViewer RenderIndicators(bool render = true)
        {
            _renderIndicators = render;

            if (Instance is object)
            {
                Script.Write("{0}.updateOptions({ renderIndicators: {1} })", Instance, render);
            }

            return this;
        }

        /// <summary>
        /// Allows editing the right-hand side. A diff viewer is read-only by default, since the usual
        /// job is reviewing a change rather than making one.
        /// </summary>
        public DiffViewer Editable(bool editable = true)
        {
            _readOnly = !editable;

            if (Instance is object)
            {
                Script.Write("{0}.updateOptions({ readOnly: {1} })", Instance, _readOnly);
            }

            return this;
        }

        /// <summary>
        /// Mutates the raw Monaco <c>IStandaloneDiffEditorConstructionOptions</c> before creation -
        /// the escape hatch for options this wrapper doesn't surface.
        /// </summary>
        public DiffViewer Options(Action<dynamic> configureOptions)
        {
            _configureOptions = configureOptions;

            return this;
        }

        /// <summary>Runs once the underlying diff editor exists.</summary>
        public DiffViewer OnRendered(Action<DiffViewer> onRendered)
        {
            _onRendered = onRendered;

            return this;
        }

        /// <summary>Moves to the next change.</summary>
        public DiffViewer GoToNextDifference()
        {
            if (Instance is object)
            {
                Script.Write("{0}.goToDiff('next')", Instance);
            }

            return this;
        }

        /// <summary>Moves to the previous change.</summary>
        public DiffViewer GoToPreviousDifference()
        {
            if (Instance is object)
            {
                Script.Write("{0}.goToDiff('previous')", Instance);
            }

            return this;
        }

        /// <summary>The raw Monaco <c>IStandaloneDiffEditor</c>, or null before mount.</summary>
        public object Editor => Instance;

        protected override object Create(HTMLElement container)
        {
            dynamic options = new
            {
                theme                   = MonacoEditor.ActiveTheme,
                readOnly                = _readOnly,
                originalEditable        = false,
                renderSideBySide        = _sideBySide,
                ignoreTrimWhitespace    = _ignoreTrimWhitespace,
                renderIndicators        = _renderIndicators,
                automaticLayout         = false, // the base class drives layout() from a ResizeObserver
                minimap                 = new { enabled = false },
                scrollBeyondLastLine    = false,
                fixedOverflowWidgets    = true,
                bracketPairColorization = new { enabled = true },
                fontSize                = 12
            };

            _configureOptions?.Invoke(options);

            ApplyOverflowWidgetsHost(options);

            return Script.Write<object>("monaco.editor.createDiffEditor({0}, {1})", container, options);
        }

        protected override void AfterCreate()
        {
            SetModels();

            _onRendered?.Invoke(this);
        }

        private void SetModels()
        {
            DisposeModels();

            var language = string.IsNullOrWhiteSpace(_language) ? "plaintext" : _language;

            _originalModel = Script.Write<object>("monaco.editor.createModel({0}, {1})", _original, language);
            _modifiedModel = Script.Write<object>("monaco.editor.createModel({0}, {1})", _modified, language);

            Script.Write("{0}.setModel({ original: {1}, modified: {2} })", Instance, _originalModel, _modifiedModel);

            Layout();
        }

        private void DisposeModels()
        {
            if (_originalModel is object)
            {
                Script.Write("{0}.dispose()", _originalModel);
                _originalModel = null;
            }

            if (_modifiedModel is object)
            {
                Script.Write("{0}.dispose()", _modifiedModel);
                _modifiedModel = null;
            }
        }

        protected override void BeforeDispose()
        {
            // Read the text back before the models go, so a re-mounted component still has content.
            _original = Original;
            _modified = Modified;

            DisposeModels();
        }
    }
}
