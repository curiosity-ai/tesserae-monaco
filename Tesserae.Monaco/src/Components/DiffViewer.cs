using System;
using System.Collections.Generic;
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
        private string                 _original             = "";
        private string                 _modified             = "";
        private string                 _language             = "";
        private string                 _originalLanguage;
        private bool                   _readOnly             = true;
        private bool                   _originalEditable;
        private bool                   _sideBySide           = true;
        private bool                   _ignoreTrimWhitespace = true;
        private bool                   _renderIndicators     = true;
        private Action<DiffEditorOptions> _configureOptions;
        private Action<DiffViewer>     _onRendered;
        private Action                 _onDiffUpdated;

        // Recorded so a remount rebuilds with the same options, and so a setter called before mount is
        // not lost - the same mechanism the code editor's typed setters use.
        private readonly List<Action<DiffEditorOptions>> _optionSetters = new List<Action<DiffEditorOptions>>();

        // The two models backing the comparison, owned by this component.
        private CodeModel _originalModel;
        private CodeModel _modifiedModel;

        private EditorSurface _originalSide;
        private EditorSurface _modifiedSide;

        internal DiffViewer() { }

        /// <summary>The left-hand (baseline) document.</summary>
        public string Original
        {
            get => _originalModel is null ? _original : _originalModel.Text;
            set
            {
                _original = value ?? "";

                if (_originalModel is object) _originalModel.Text = _original;
            }
        }

        /// <summary>The right-hand (changed) document.</summary>
        public string Modified
        {
            get => _modifiedModel is null ? _modified : _modifiedModel.Text;
            set
            {
                _modified = value ?? "";

                if (_modifiedModel is object) _modifiedModel.Text = _modified;
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

            if (Instance != null)
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

            Editor?.updateOptions(new DiffEditorOptions { renderSideBySide = sideBySide });

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

            Editor?.updateOptions(new DiffEditorOptions { ignoreTrimWhitespace = ignore });

            return this;
        }

        /// <summary>Shows the +/- indicators in the gutter.</summary>
        public DiffViewer RenderIndicators(bool render = true)
        {
            _renderIndicators = render;

            Editor?.updateOptions(new DiffEditorOptions { renderIndicators = render });

            return this;
        }

        /// <summary>
        /// Allows editing the right-hand side. A diff viewer is read-only by default, since the usual
        /// job is reviewing a change rather than making one.
        /// </summary>
        public DiffViewer Editable(bool editable = true)
        {
            _readOnly = !editable;

            Editor?.updateOptions(new DiffEditorOptions { readOnly = _readOnly });

            return this;
        }

        /// <summary>
        /// Adjusts the Monaco construction options before creation - the escape hatch for options
        /// this wrapper doesn't surface. <see cref="DiffEditorOptions"/> covers the common ones; it
        /// is a plain JavaScript object at runtime, so <c>SetRawOption</c> reaches the rest
        /// reaches the rest.
        /// </summary>
        public DiffViewer Options(Action<DiffEditorOptions> configureOptions)
        {
            _configureOptions = configureOptions;

            return this;
        }

        /// <summary>Runs once the underlying diff editor exists. Handlers accumulate.</summary>
        public DiffViewer OnRendered(Action<DiffViewer> onRendered)
        {
            _onRendered += onRendered;

            return this;
        }

        /// <summary>Moves to the next change.</summary>
        public DiffViewer GoToNextDifference()
        {
            Editor?.goToDiff("next");

            return this;
        }

        /// <summary>Moves to the previous change.</summary>
        public DiffViewer GoToPreviousDifference()
        {
            Editor?.goToDiff("previous");

            return this;
        }

        /// <summary>The underlying Monaco diff editor, or null before mount.</summary>
        public IStandaloneDiffEditor Editor => (IStandaloneDiffEditor)Instance;


        /// <summary>
        /// Runs whenever the diff has been recomputed - which is asynchronous, on the diff worker, and
        /// well after the text changed. This is the signal to read <see cref="GetLineChanges"/> or
        /// <see cref="ChangeCount"/>: doing it straight after setting the content reads the previous diff,
        /// or none at all.
        /// </summary>
        public DiffViewer OnDiffUpdated(Action handler)
        {
            _onDiffUpdated += handler;

            return this;
        }

        /// <summary>
        /// The left side's editor, or null before mount - for decorating just the baseline, or subscribing
        /// to it. Available from <see cref="OnRendered"/> onwards.
        /// </summary>
        public EditorSurface OriginalSide => _originalSide;

        /// <summary>
        /// The right side's editor, or null before mount. This is where to subscribe for edits when
        /// <see cref="Editable"/> is on - the diff editor itself has no content event.
        /// </summary>
        public EditorSurface ModifiedSide => _modifiedSide;

        /// <summary>The left side's document, or null before mount.</summary>
        public CodeModel OriginalModel => _originalModel;

        /// <summary>The right side's document, or null before mount.</summary>
        public CodeModel ModifiedModel => _modifiedModel;

        /// <summary>
        /// Every changed block, as line ranges on both sides. Empty until the diff worker has run - see
        /// <see cref="OnDiffUpdated"/>.
        /// </summary>
        public LineChange[] GetLineChanges()
        {
            return Editor is null ? new LineChange[0] : Editor.getLineChanges() ?? new LineChange[0];
        }

        /// <summary>How many changed blocks the diff found, or 0 before it has been computed.</summary>
        public int ChangeCount => GetLineChanges().Length;

        /// <summary>
        /// Whether the two sides are identical as far as the diff is concerned - which honours
        /// <see cref="IgnoreTrimWhitespace"/>, so two documents differing only in indentation report
        /// identical by default.
        /// </summary>
        public bool IsIdentical
        {
            get
            {
                var result = Editor?.getDiffComputationResult();

                return result is object && result.identical;
            }
        }

        /// <summary>
        /// Gives the left side its own language, for comparing two documents that are not the same kind of
        /// thing. Left unset both sides use <see cref="SetLanguage(string)"/>'s.
        /// </summary>
        public DiffViewer SetOriginalLanguage(string language)
        {
            _originalLanguage = language;

            if (Editor is object)
            {
                _original = Original;
                _modified = Modified;

                SetModels();
            }

            return this;
        }

        /// <summary>
        /// Allows editing the <b>left</b> side too. Off by default and separate from
        /// <see cref="Editable"/>, because the usual job is reviewing a change against a fixed baseline -
        /// but a merge or a two-way comparison needs both sides live.
        /// </summary>
        public DiffViewer OriginalEditable(bool editable = true)
        {
            _originalEditable = editable;

            return Option(o => o.originalEditable = editable);
        }

        /// <summary>
        /// Collapses runs of unchanged lines to a few lines of context, with a band to expand them - what
        /// makes a diff of two large, mostly-identical documents readable.
        /// </summary>
        public DiffViewer HideUnchangedRegions(bool enabled = true, int contextLineCount = 3, int minimumLineCount = 3)
        {
            return Option(o => o.hideUnchangedRegions = new HideUnchangedRegionsOptions
            {
                enabled          = enabled,
                contextLineCount = contextLineCount,
                minimumLineCount = minimumLineCount,
                revealLineCount  = 20
            });
        }

        /// <summary>
        /// Detects blocks that moved rather than changed, and draws them as moves instead of a delete plus
        /// an unrelated insert. Still marked experimental upstream.
        /// </summary>
        public DiffViewer ShowMoves(bool enabled = true)
        {
            return Option(o => o.experimental = new DiffExperimentalOptions { showMoves = enabled });
        }

        /// <summary>Shows the arrow in the margin that reverts one block. Only useful when a side is editable.</summary>
        public DiffViewer RenderMarginRevertIcon(bool enabled = true)
        {
            return Option(o => o.renderMarginRevertIcon = enabled);
        }

        /// <summary>Soft-wraps long lines in both panes: <c>"off"</c>, <c>"on"</c> or <c>"inherit"</c>.</summary>
        public DiffViewer DiffWordWrap(string mode)
        {
            return Option(o => o.diffWordWrap = mode);
        }

        /// <summary>Draws the change marks in the overview ruler beside the scrollbar.</summary>
        public DiffViewer RenderOverviewRuler(bool enabled = true)
        {
            return Option(o => o.renderOverviewRuler = enabled);
        }

        /// <summary>
        /// How long the diff may take before Monaco gives up and shows the documents unchanged, in
        /// milliseconds. Raise it for large files; 0 means no limit.
        /// </summary>
        public DiffViewer MaxComputationTime(double milliseconds)
        {
            return Option(o => o.maxComputationTime = milliseconds);
        }

        /// <summary>Shows the minimap in the diff's panes. Off by default.</summary>
        public DiffViewer Minimap(bool enabled = true)
        {
            return Option(o => o.minimap = new MinimapOptions { enabled = enabled });
        }

        /// <summary>Font size in pixels for both panes.</summary>
        public DiffViewer FontSize(double px)
        {
            return Option(o => o.fontSize = px);
        }

        /// <summary>
        /// Records an option change and applies it if the diff editor already exists. The same mutator
        /// serves construction and <c>updateOptions</c>, since an <c>[ObjectLiteral]</c> emits only the
        /// fields assigned.
        /// </summary>
        protected DiffViewer Option(Action<DiffEditorOptions> set)
        {
            if (set is null) return this;

            _optionSetters.Add(set);

            if (Editor is object)
            {
                var patch = new DiffEditorOptions();

                set(patch);

                Editor.updateOptions(patch);
            }

            return this;
        }

        /// <summary>
        /// Sets one option <see cref="DiffEditorOptions"/> does not declare - the escape hatch for the rest
        /// of Monaco's diff options.
        /// </summary>
        public DiffViewer SetRawOption(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) return this;

            return Option(options => Script.Set(options, name, value));
        }

        protected override IEditor Create(HTMLElement container)
        {
            var options = new DiffEditorOptions
            {
                theme                   = MonacoEditor.ActiveTheme,
                readOnly                = _readOnly,
                originalEditable        = _originalEditable,
                renderSideBySide        = _sideBySide,
                ignoreTrimWhitespace    = _ignoreTrimWhitespace,
                renderIndicators        = _renderIndicators,
                automaticLayout         = false, // the base class drives layout() from a ResizeObserver
                minimap                 = new MinimapOptions { enabled = false },
                scrollBeyondLastLine    = false,
                fixedOverflowWidgets    = true,
                bracketPairColorization = new BracketPairColorizationOptions { enabled = true },
                fontSize                = 12
            };

            foreach (var set in _optionSetters)
            {
                set(options);
            }

            _configureOptions?.Invoke(options);

            ApplyOverflowWidgetsHost(options);

            return MonacoApi.editor.createDiffEditor(container, options);
        }

        protected override void AfterCreate()
        {
            // The two inner editors are surfaces like any other, so a caller can decorate one side or
            // subscribe to edits on it without a second implementation of any of that.
            _originalSide = new EditorSurface(Editor.getOriginalEditor(), Disposables);
            _modifiedSide = new EditorSurface(Editor.getModifiedEditor(), Disposables);

            SetModels();

            // One stable dispatcher rather than one registration per handler: a handler added after mount
            // then needs no registration of its own, so the handlers already attached cannot fire twice.
            Disposables.Add(Editor.onDidUpdateDiff(() => _onDiffUpdated?.Invoke()));

            _onRendered?.Invoke(this);
        }

        private void SetModels()
        {
            DisposeModels();

            var modified = string.IsNullOrWhiteSpace(_language) ? "plaintext" : _language;
            var original = string.IsNullOrWhiteSpace(_originalLanguage) ? modified : _originalLanguage;

            _originalModel = CodeModel.Create(_original, original);
            _modifiedModel = CodeModel.Create(_modified, modified);

            Editor.setModel(new DiffEditorModel { original = _originalModel.Native, modified = _modifiedModel.Native });

            Layout();
        }

        private void DisposeModels()
        {
            if (_originalModel != null)
            {
                _originalModel.Dispose();
                _originalModel = null;
            }

            if (_modifiedModel != null)
            {
                _modifiedModel.Dispose();
                _modifiedModel = null;
            }
        }

        protected override void BeforeDispose()
        {
            // Read the text back before the models go, so a re-mounted component still has content.
            _original = Original;
            _modified = Modified;

            _originalSide = null;
            _modifiedSide = null;

            DisposeModels();
        }
    }
}
