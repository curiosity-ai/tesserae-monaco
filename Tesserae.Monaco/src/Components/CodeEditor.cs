using System;
using System.Linq;
using System.Threading.Tasks;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A full-featured Monaco code editor: completion, hover documentation, document/range
    /// formatting, error squiggles and as-you-type validation.
    ///
    /// The package deliberately ships <b>no</b> language intelligence of its own - every provider is
    /// a delegate you supply, so the same component works against a server-side compiler, a
    /// client-side analyser, or a static word list. Create one with
    /// <see cref="MonacoEditor.Editor(bool)"/>.
    ///
    /// Monaco's provider registry is global per language, but the callbacks below are scoped to this
    /// instance: each one checks that the model it was handed belongs to this editor before doing
    /// any work, and every registration is disposed when the component leaves the DOM. That is what
    /// lets two editors share a language while answering completions differently.
    /// </summary>
    [Transpose.Name("tssm.CodeEditor")]
    public sealed class CodeEditor : MonacoComponent
    {
        private const int    HOVER_REQUEST_DELAY_MS = 250;
        private const int    VALIDATION_DEBOUNCE_MS = 1_000;
        private const string MARKER_OWNER           = "tss-monaco";
        private const string FORMATTER_DISPLAY_NAME = "Tesserae.Monaco";

        private readonly bool _autoHeight;

        private string _text     = "";
        private string _language = "";
        private bool   _readOnly;
        private bool   _wordWrap;

        private Action                _onChanged;
        private Action                _onBeforeCreate;
        private Action<CodeEditor>    _onRendered;
        private Action<EditorOptions> _configureOptions;

        private Func<ITextModel, Position, IPromise>                     _onCompletion;
        private Func<ITextModel, Position, IPromise>                     _onHover;
        private Func<CompletionItem, ICancellationToken, CompletionItem> _onResolveCompletion;
        private Func<string, Task<string>>                               _onFormat;
        private Func<string, Task<ReadOnlyArray<CodeDiagnostic>>>        _validator;
        private bool                                                     _validateImmediately;

        // The live provider registrations, disposed with the component.
        private IJsDisposable _completionProvider;
        private IJsDisposable _hoverProvider;
        private IJsDisposable _formattingProvider;
        private IJsDisposable _rangeFormattingProvider;

        private double _validationTimeoutId;

        internal CodeEditor(bool autoHeight)
        {
            _autoHeight = autoHeight;
        }

        /// <summary>The editor's content. Reads straight from the live model once mounted.</summary>
        public string Text
        {
            get => Editor is null ? _text : Editor.getValue();
            set
            {
                _text = value ?? "";

                Editor?.setValue(_text);
            }
        }

        /// <summary>
        /// Sets the content, skipping the write when it is unchanged - which matters because
        /// <c>setValue</c> resets the undo stack and the caret.
        /// </summary>
        public CodeEditor SetText(string text)
        {
            if (Text != text) Text = text;

            return this;
        }

        /// <summary>The one-based caret position, or null before mount.</summary>
        public Position GetPosition() => Editor?.getPosition();

        /// <summary>Moves the caret.</summary>
        public CodeEditor SetPosition(Position position)
        {
            if (position != null) Editor?.setPosition(position);

            return this;
        }

        /// <summary>Scrolls <paramref name="lineNumber"/> into the middle of the viewport.</summary>
        public CodeEditor RevealLine(int lineNumber)
        {
            Editor?.revealLineInCenter(lineNumber);

            return this;
        }

        /// <summary>Gives the editor keyboard focus.</summary>
        public CodeEditor Focus()
        {
            Editor?.focus();

            return this;
        }

        /// <summary>Sets the language by Monaco language id (<c>"csharp"</c>, <c>"json"</c>, …).</summary>
        public CodeEditor SetLanguage(string language)
        {
            _language = language ?? "";

            var model = Editor?.getModel();

            if (model != null)
            {
                MonacoApi.editor.setModelLanguage(model, _language);
            }

            return this;
        }

        /// <summary>Registers <paramref name="language"/> if needed, then selects it.</summary>
        public CodeEditor SetLanguage(LanguageDefinition language)
        {
            MonacoEditor.RegisterLanguage(language);

            return SetLanguage(language?.Id);
        }

        /// <summary>Picks the language from a file extension, if Monaco knows one for it.</summary>
        public CodeEditor SetLanguageByExtension(string extension)
        {
            if (MonacoEditor.TryGetLanguageIdForExtension(extension, out var languageId))
            {
                SetLanguage(languageId);
            }

            return this;
        }

        /// <summary>Makes the editor read-only.</summary>
        public CodeEditor ReadOnly(bool readOnly = true)
        {
            _readOnly = readOnly;

            Editor?.updateOptions(new EditorOptions { readOnly = readOnly });

            return this;
        }

        /// <summary>Soft-wraps long lines instead of scrolling horizontally.</summary>
        public CodeEditor WordWrap(bool wordWrap = true)
        {
            _wordWrap = wordWrap;

            Editor?.updateOptions(new EditorOptions { wordWrap = wordWrap ? "on" : "off" });

            return this;
        }

        /// <summary>
        /// Whether lines are currently wrapped. Tracks the editor's own state, so it stays correct
        /// after the user toggles wrapping from the context menu.
        /// </summary>
        public bool IsWordWrapped => _wordWrap;

        /// <summary>
        /// Adds a callback for content changes. Callbacks accumulate rather than replace, so
        /// internal wiring (as-you-type validation) and caller wiring coexist.
        /// </summary>
        public CodeEditor OnChanged(Action onChanged)
        {
            if (onChanged is null) return this;

            if (_onChanged is null)
            {
                _onChanged = onChanged;
            }
            else
            {
                var previous = _onChanged;

                _onChanged = () =>
                {
                    onChanged();
                    previous();
                };
            }

            return this;
        }

        /// <summary>
        /// Runs just before the Monaco instance is created - the hook for registering a language or
        /// otherwise touching global Monaco state that the editor is about to depend on.
        /// </summary>
        public CodeEditor OnBeforeCreate(Action onBeforeCreate)
        {
            _onBeforeCreate += onBeforeCreate;

            return this;
        }

        /// <summary>Runs once the underlying editor exists.</summary>
        public CodeEditor OnRendered(Action<CodeEditor> onRendered)
        {
            _onRendered = onRendered;

            return this;
        }

        /// <summary>
        /// Adjusts the Monaco construction options before creation - the escape hatch for options
        /// this wrapper doesn't surface. <see cref="EditorOptions"/> covers the common ones; it is a
        /// plain JavaScript object at runtime, so <c>((dynamic)options).someOption = value</c>
        /// reaches the rest.
        /// </summary>
        public CodeEditor Options(Action<EditorOptions> configureOptions)
        {
            _configureOptions = configureOptions;

            return this;
        }

        /// <summary>The underlying Monaco editor, or null before mount.</summary>
        public IStandaloneCodeEditor Editor => (IStandaloneCodeEditor)Instance;

        #region Completion

        /// <summary>
        /// Supplies completions from a <see cref="CodeContext"/>. Set
        /// <see cref="CompletionItem.kind"/> for a sensible icon, and
        /// <see cref="CompletionItem.insertText"/> only when it differs from the label.
        ///
        /// <see cref="CompletionItem.insertText"/> and <see cref="CompletionItem.range"/> are filled
        /// in for you when left unset - the label and the word under the caret respectively. Monaco
        /// treats both as required and throws from deep inside the suggest widget when they are
        /// missing, which is a poor trade for what is almost always the obvious default.
        /// </summary>
        public CodeEditor OnCompletion(Func<CodeContext, Task<CompletionItem[]>> onCompletion)
        {
            if (onCompletion is null) return this;

            return OnCompletionRaw((model, position) => MonacoEditor.AsPromise(BuildCompletionListAsync(onCompletion, model, position)));
        }

        private static async Task<object> BuildCompletionListAsync(Func<CodeContext, Task<CompletionItem[]>> onCompletion, ITextModel model, Position position)
        {
            var context = new CodeContext(model, position);
            var items   = await onCompletion(context);

            if (items is null) return new CompletionList { suggestions = new CompletionItem[0] };

            // Replace the word being typed; with the caret off a word, insert at the caret.
            var range = context.WordRange ?? new TextRange
            {
                startLineNumber = context.Position.lineNumber,
                endLineNumber   = context.Position.lineNumber,
                startColumn     = context.Position.column,
                endColumn       = context.Position.column
            };

            foreach (var item in items)
            {
                if (item is null) continue;

                if (string.IsNullOrEmpty(item.insertText)) item.insertText = item.label;

                if (item.range == null) item.range = range;
            }

            return new CompletionList { suggestions = items };
        }

        /// <summary>
        /// The unwrapped completion provider: hand back the <c>Promise</c> of a Monaco
        /// <c>CompletionList</c> yourself. Use <see cref="OnCompletion"/> unless you need to shape the
        /// response beyond a list of items.
        /// </summary>
        public CodeEditor OnCompletionRaw(Func<ITextModel, Position, IPromise> onCompletion)
        {
            _onCompletion = onCompletion;

            return this;
        }

        /// <summary>
        /// Fills in the expensive parts of a completion item (documentation, say) only once the user
        /// highlights it in the suggest list. Monaco passes the item and a cancellation token, and
        /// nothing else - it does not repeat the model and position from the original request.
        /// </summary>
        public CodeEditor OnResolveCompletion(Func<CompletionItem, ICancellationToken, CompletionItem> onResolveCompletion)
        {
            _onResolveCompletion = onResolveCompletion;

            return this;
        }

        #endregion

        #region Hover

        /// <summary>
        /// Supplies hover documentation for the symbol under the cursor. Return markdown, or null for
        /// no hover. Prefix the string with <see cref="MonacoEditor.HTML_MARKER"/> to render it as
        /// HTML instead - escaping anything untrusted with <see cref="MonacoEditor.EscapeHtml"/>
        /// first.
        /// </summary>
        public CodeEditor OnHover(Func<CodeContext, Task<string>> onHover)
        {
            if (onHover is null) return this;

            return OnHoverRaw((model, position) => MonacoEditor.AsPromise(BuildHoverAsync(onHover, model, position)));
        }

        private static async Task<object> BuildHoverAsync(Func<CodeContext, Task<string>> onHover, ITextModel model, Position position)
        {
            var context = new CodeContext(model, position);

            if (context.Word is null) return null;

            var documentation = await onHover(context);

            if (string.IsNullOrWhiteSpace(documentation)) return null;

            return new Hover
            {
                range    = context.WordRange,
                contents = new[]
                {
                    new MarkdownString { value = documentation, supportHtml = true, isTrusted = true }
                }
            };
        }

        /// <summary>
        /// The unwrapped hover provider: hand back the <c>Promise</c> of a Monaco <c>Hover</c>
        /// yourself, for control over the highlighted range or multiple content sections.
        /// </summary>
        public CodeEditor OnHoverRaw(Func<ITextModel, Position, IPromise> onHover)
        {
            _onHover = onHover;

            return this;
        }

        #endregion

        #region Formatting

        /// <summary>
        /// Enables "Format Document" (Shift+Alt+F) and "Format Selection" (Ctrl+K Ctrl+F) by
        /// supplying a formatter: given the source text, return the formatted text (or null to leave
        /// it alone). The same delegate serves both, called with the selection for a range format.
        ///
        /// A formatter that throws is treated as "no edits" rather than surfacing as a rejected
        /// promise, which Monaco would log as an unhandled provider error.
        /// </summary>
        public CodeEditor OnFormat(Func<string, Task<string>> onFormat)
        {
            _onFormat = onFormat;

            return this;
        }

        private async Task<object> FormatWholeModelAsync(ITextModel model)
        {
            try
            {
                var code      = model.getValue();
                var formatted = await _onFormat(code);

                if (formatted is null || formatted == code) return null;

                return PlainEdits(new TextEdit { range = model.getFullModelRange(), text = formatted });
            }
            catch
            {
                return null;
            }
        }

        private async Task<object> FormatRangeAsync(ITextModel model, TextRange range)
        {
            try
            {
                var code      = model.getValueInRange(range);
                var formatted = await _onFormat(code);

                if (formatted is null || formatted == code) return null;

                return PlainEdits(new TextEdit { range = range, text = formatted });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Wraps a formatter's edits in a <b>plain</b> JavaScript array.
        ///
        /// Monaco hands the edits to its editor worker to be minimised, so they have to survive
        /// <c>structuredClone</c> - and a C# array does not. Every typed array carries a
        /// <c>$type</c> pointing at the Transpose class that describes its element type, which is a
        /// function, so posting one fails the whole worker message with a <c>DataCloneError</c>.
        /// </summary>
        private static TextEdit[] PlainEdits(TextEdit edit) => Script.ToArray(new[] { edit });

        #endregion

        #region Diagnostics

        /// <summary>
        /// Replaces the editor's squiggles. Coordinates are Monaco's own, i.e. one-based; pass
        /// an empty array to clear.
        /// </summary>
        public CodeEditor SetMarkers(ReadOnlyArray<CodeMarker> markers)
        {
            if (Editor is null) return this;

            MonacoApi.editor.setModelMarkers(Editor.getModel(), MARKER_OWNER, markers);

            return this;
        }

        /// <summary>
        /// Replaces the editor's squiggles from zero-based <see cref="CodeDiagnostic"/>s, as a
        /// compiler usually reports them.
        /// </summary>
        public CodeEditor SetDiagnostics(ReadOnlyArray<CodeDiagnostic> diagnostics)
        {
            var markers = new CodeMarker[diagnostics.Length];

            for (var i = 0; i < diagnostics.Length; i++)
            {
                markers[i] = diagnostics[i].ToMarker();
            }

            return SetMarkers(markers);
        }

        /// <summary>Clears every squiggle.</summary>
        public CodeEditor ClearMarkers() => SetMarkers(new CodeMarker[0]);

        /// <summary>
        /// Validates the content as the user types and shows the results as squiggles.
        ///
        /// Markers are cleared on each keystroke and the validator only runs after a second of quiet,
        /// so a server-backed validator isn't called per character. The result is discarded if the
        /// text moved on while it was in flight, so a slow response can't squiggle the wrong code.
        /// </summary>
        /// <param name="validator">Given the current text, the diagnostics to show.</param>
        /// <param name="validateImmediately">Also validate once as soon as the editor is created.</param>
        public CodeEditor ValidateAsYouType(Func<string, Task<ReadOnlyArray<CodeDiagnostic>>> validator, bool validateImmediately = true)
        {
            if (validator is null) return this;

            _validator           = validator;
            _validateImmediately = validateImmediately;

            OnChanged(Validate);

            return this;
        }

        /// <summary>Runs the <see cref="ValidateAsYouType"/> validator now, on the same debounce.</summary>
        public void Validate()
        {
            if (_validator is null) return;

            ClearMarkers();

            clearTimeout(_validationTimeoutId);

            var code = Text;

            if (string.IsNullOrWhiteSpace(code)) return;

            _validationTimeoutId = setTimeout(_ => ValidateAsync(code).FireAndForget(), VALIDATION_DEBOUNCE_MS);
        }

        private async Task ValidateAsync(string code)
        {
            var diagnostics = await _validator(code);

            // Discard a stale result: the editor may have been disposed, or the text moved on.
            if (Editor is null || Text != code) return;

            if (diagnostics is object && diagnostics.Length > 0)
            {
                SetDiagnostics(diagnostics);
            }
        }

        #endregion

        protected override IEditor Create(HTMLElement container)
        {
            _onBeforeCreate?.Invoke();

            var options = BuildBaseOptions(_language, _text, _readOnly, _wordWrap, _autoHeight);

            options.snippetSuggestions = "bottom";
            options.suggest            = new SuggestOptions { preview = true };
            options.suggestFontSize    = 10;
            options.inlineSuggest      = new InlineSuggestOptions { enabled = true, showToolbar = "always" };
            options.hover              = new HoverOptions { enabled = true, sticky = true, delay = HOVER_REQUEST_DELAY_MS, hidingDelay = 300 };

            _configureOptions?.Invoke(options);

            ApplyOverflowWidgetsHost(options);

            var editor = MonacoApi.editor.create(container, options);

            editor.onDidChangeModelContent(() => _onChanged?.Invoke());
            editor.getModel().updateOptions(new TextModelOptions { tabSize = 4, insertSpaces = true });

            return editor;
        }

        protected override void AfterCreate()
        {
            var editor   = Editor;
            var language = string.IsNullOrWhiteSpace(_language) ? "plaintext" : _language;

            RegisterCompletionProvider(editor, language);
            RegisterHoverProvider(editor, language);
            RegisterFormattingProviders(editor, language);
            AddWordWrapAction(editor);

            if (_autoHeight) EnableAutoHeight();

            _onRendered?.Invoke(this);

            if (_validator is object && _validateImmediately)
            {
                Validate();
            }
        }

        private void RegisterCompletionProvider(IStandaloneCodeEditor editor, string language)
        {
            if (_onCompletion is null && _onResolveCompletion is null) return;

            _completionProvider = MonacoApi.languages.registerCompletionItemProvider(language, new CompletionItemProvider
            {
                // Monaco asks every provider registered for the language; answering only for our own
                // model is what keeps two editors on the same language independent.
                provideCompletionItems = (model, position) =>
                    editor.getModel() != model ? null : _onCompletion?.Invoke(model, position),

                resolveCompletionItem = (item, token) => _onResolveCompletion?.Invoke(item, token) ?? item
            });
        }

        private void RegisterHoverProvider(IStandaloneCodeEditor editor, string language)
        {
            if (_onHover is null) return;

            _hoverProvider = MonacoApi.languages.registerHoverProvider(language, new HoverProvider
            {
                provideHover = (model, position, token) => MonacoEditor.AsPromise(ProvideHoverAsync(editor, model, position, token))
            });
        }

        /// <summary>
        /// Monaco cancels a hover request as soon as the pointer moves on. Settling on cancellation -
        /// rather than resolving whenever the provider eventually finishes - is what stops a stale
        /// tooltip from flashing up over the wrong symbol, and stops a rejected provider promise from
        /// being logged as an unhandled provider error.
        /// </summary>
        private async Task<object> ProvideHoverAsync(IStandaloneCodeEditor editor, ITextModel model, Position position, ICancellationToken token)
        {
            if (editor.getModel() != model || IsCancelled(token)) return null;

            // Whichever comes first wins: the provider's answer, or the cancellation.
            var settled      = new TaskCompletionSource<object>();
            var registration = token?.onCancellationRequested(() => settled.TrySetResult(null));

            ResolveHover(settled, editor, model, position, token);

            try
            {
                return await settled.Task;
            }
            finally
            {
                registration?.dispose();
            }
        }

        private void ResolveHover(
            TaskCompletionSource<object> settled,
            IStandaloneCodeEditor        editor,
            ITextModel                   model,
            Position                     position,
            ICancellationToken           token)
        {
            IPromise pending;

            try
            {
                pending = _onHover?.Invoke(model, position);
            }
            catch (Exception exception)
            {
                Reject(settled, token, exception);
                return;
            }

            if (pending is null)
            {
                settled.TrySetResult(null);
                return;
            }

            // Then, not await. Awaiting an IPromise is typed as handing back the resolved values as
            // an array, but the runtime adapter passes a native promise straight through - so the
            // awaited value is the single resolved value, and reading .Length off it silently
            // yields nothing at all.
            pending.Then(
                new Action<object>(hover => settled.TrySetResult(IsCancelled(token) || editor.getModel() != model ? null : hover)),
                new Action<object>(error => Reject(settled, token, error)),
                null);
        }

        /// <summary>
        /// Fails the pending hover - unless it was cancelled, in which case there is nothing to
        /// report: Monaco logs a rejected provider promise as an unhandled provider error, and a
        /// hover the user has already moved away from is not an error.
        /// </summary>
        private static void Reject(TaskCompletionSource<object> settled, ICancellationToken token, object error)
        {
            if (IsCancelled(token))
            {
                settled.TrySetResult(null);
                return;
            }

            settled.TrySetException(error as Exception ?? new Exception("The hover provider failed: " + error));
        }

        private static bool IsCancelled(ICancellationToken token) => token != null && token.isCancellationRequested;

        private void RegisterFormattingProviders(IStandaloneCodeEditor editor, string language)
        {
            if (_onFormat is null) return;

            _formattingProvider = MonacoApi.languages.registerDocumentFormattingEditProvider(language, new DocumentFormattingEditProvider
            {
                displayName = FORMATTER_DISPLAY_NAME,

                provideDocumentFormattingEdits = (model, formattingOptions, token) =>
                    editor.getModel() != model ? null : MonacoEditor.AsPromise(FormatWholeModelAsync(model))
            });

            _rangeFormattingProvider = MonacoApi.languages.registerDocumentRangeFormattingEditProvider(language, new DocumentRangeFormattingEditProvider
            {
                displayName = FORMATTER_DISPLAY_NAME,

                provideDocumentRangeFormattingEdits = (model, range, formattingOptions, token) =>
                    editor.getModel() != model ? null : MonacoEditor.AsPromise(FormatRangeAsync(model, range))
            });
        }

        // Word wrap is a per-editor view preference, so it belongs on the editor's own context menu
        // rather than in the host application's chrome. Toggling through WordWrap keeps _wordWrap in
        // step, so IsWordWrapped still answers correctly after the user flips it here.
        private void AddWordWrapAction(IStandaloneCodeEditor editor)
        {
            editor.addAction(new EditorAction
            {
                id                 = "tssm.toggleWordWrap",
                label              = "Toggle Word Wrap",
                contextMenuGroupId = "view",
                contextMenuOrder   = 1.5,
                keybindings        = new[] { MonacoApi.KeyMod.Alt | MonacoApi.KeyCode.KeyZ },
                run                = _ => WordWrap(!_wordWrap)
            });
        }

        protected override void BeforeDispose()
        {
            clearTimeout(_validationTimeoutId);

            // Language providers are registered globally, so disposing the editor does not remove
            // them - they have to be released explicitly or each mount leaks another provider that
            // answers for a model that no longer exists.
            DisposeProvider(ref _completionProvider);
            DisposeProvider(ref _hoverProvider);
            DisposeProvider(ref _formattingProvider);
            DisposeProvider(ref _rangeFormattingProvider);
        }

        private static void DisposeProvider(ref IJsDisposable provider)
        {
            if (provider is null) return;

            provider.dispose();
            provider = null;
        }
    }
}
