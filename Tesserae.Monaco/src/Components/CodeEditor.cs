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
        private const int HOVER_REQUEST_DELAY_MS = 250;
        private const int VALIDATION_DEBOUNCE_MS = 1_000;

        private readonly bool _autoHeight;

        private string _text     = "";
        private string _language = "";
        private bool   _readOnly;
        private bool   _wordWrap;

        private Action                  _onChanged;
        private Action                  _onBeforeCreate;
        private Action<CodeEditor>      _onRendered;
        private Action<dynamic>          _configureOptions;

        private Func<dynamic, dynamic, IPromise>                                  _onCompletion;
        private Func<dynamic, dynamic, IPromise>                                  _onHover;
        private Func<dynamic, dynamic, CompletionItem, dynamic, CompletionItem>   _onResolveCompletion;
        private Func<string, Task<string>>                                        _onFormat;
        private Func<string, Task<ReadOnlyArray<CodeDiagnostic>>>                 _validator;
        private bool                                                              _validateImmediately;

        // The live provider registrations, disposed with the component.
        private dynamic _completionProvider;
        private dynamic _hoverProvider;
        private dynamic _formattingProvider;
        private dynamic _rangeFormattingProvider;

        private double _validationTimeoutId;

        internal CodeEditor(bool autoHeight)
        {
            _autoHeight = autoHeight;
        }

        /// <summary>The editor's content. Reads straight from the live model once mounted.</summary>
        public string Text
        {
            get => Instance is null ? _text : Script.Write<string>("{0}.getValue()", Instance);
            set
            {
                _text = value ?? "";

                if (Instance is object)
                {
                    Script.Write("{0}.setValue({1})", Instance, _text);
                }
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
        public Position GetPosition() => Instance is null ? null : Script.Write<Position>("{0}.getPosition()", Instance);

        /// <summary>Moves the caret.</summary>
        public CodeEditor SetPosition(Position position)
        {
            if (Instance is object && position is object)
            {
                Script.Write("{0}.setPosition({1})", Instance, position);
            }

            return this;
        }

        /// <summary>Scrolls <paramref name="lineNumber"/> into the middle of the viewport.</summary>
        public CodeEditor RevealLine(int lineNumber)
        {
            if (Instance is object)
            {
                Script.Write("{0}.revealLineInCenter({1})", Instance, lineNumber);
            }

            return this;
        }

        /// <summary>Gives the editor keyboard focus.</summary>
        public CodeEditor Focus()
        {
            if (Instance is object)
            {
                Script.Write("{0}.focus()", Instance);
            }

            return this;
        }

        /// <summary>Sets the language by Monaco language id (<c>"csharp"</c>, <c>"json"</c>, …).</summary>
        public CodeEditor SetLanguage(string language)
        {
            _language = language ?? "";

            if (Instance is object)
            {
                object model = Script.Write<object>("{0}.getModel()", Instance);

                if (model is object)
                {
                    Script.Write("monaco.editor.setModelLanguage({0}, {1})", model, _language);
                }
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

            if (Instance is object)
            {
                Script.Write("{0}.updateOptions({ readOnly: {1} })", Instance, readOnly);
            }

            return this;
        }

        /// <summary>Soft-wraps long lines instead of scrolling horizontally.</summary>
        public CodeEditor WordWrap(bool wordWrap = true)
        {
            _wordWrap = wordWrap;

            if (Instance is object)
            {
                Script.Write("{0}.updateOptions({ wordWrap: {1} })", Instance, wordWrap ? "on" : "off");
            }

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
        /// Mutates the raw Monaco <c>IStandaloneEditorConstructionOptions</c> before creation - the
        /// escape hatch for options this wrapper doesn't surface.
        /// </summary>
        public CodeEditor Options(Action<dynamic> configureOptions)
        {
            _configureOptions = configureOptions;

            return this;
        }

        /// <summary>The raw Monaco <c>IStandaloneCodeEditor</c>, or null before mount.</summary>
        public object Editor => Instance;

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

        private static async Task<object> BuildCompletionListAsync(Func<CodeContext, Task<CompletionItem[]>> onCompletion, dynamic model, dynamic position)
        {
            var context = new CodeContext(model, position);
            var items   = await onCompletion(context);

            if (items is null) return new { suggestions = new CompletionItem[0] };

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

                if (Script.Write<bool>("{0}.range == null", item)) item.range = range;
            }

            return new { suggestions = items };
        }

        /// <summary>
        /// The unwrapped completion provider: hand back the <c>Promise</c> of a Monaco
        /// <c>CompletionList</c> yourself. Use <see cref="OnCompletion"/> unless you need to shape the
        /// response beyond a list of items.
        /// </summary>
        public CodeEditor OnCompletionRaw(Func<dynamic, dynamic, IPromise> onCompletion)
        {
            _onCompletion = onCompletion;

            return this;
        }

        /// <summary>
        /// Fills in the expensive parts of a completion item (documentation, say) only once the user
        /// highlights it in the suggest list.
        /// </summary>
        public CodeEditor OnResolveCompletion(Func<dynamic, dynamic, CompletionItem, dynamic, CompletionItem> onResolveCompletion)
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

        private static async Task<object> BuildHoverAsync(Func<CodeContext, Task<string>> onHover, dynamic model, dynamic position)
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
        public CodeEditor OnHoverRaw(Func<dynamic, dynamic, IPromise> onHover)
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

        private async Task<object> FormatWholeModelAsync(dynamic model)
        {
            try
            {
                string code      = Script.Write<string>("{0}.getValue()", model);
                string formatted = await _onFormat(code);

                if (formatted is null || formatted == code) return null;

                dynamic fullRange = Script.Write<dynamic>("{0}.getFullModelRange()", model);

                return Script.Write<object>("[{ range: {0}, text: {1} }]", fullRange, formatted);
            }
            catch
            {
                return null;
            }
        }

        private async Task<object> FormatRangeAsync(dynamic model, dynamic range)
        {
            try
            {
                string code      = Script.Write<string>("{0}.getValueInRange({1})", model, range);
                string formatted = await _onFormat(code);

                if (formatted is null || formatted == code) return null;

                return Script.Write<object>("[{ range: {0}, text: {1} }]", range, formatted);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Diagnostics

        /// <summary>
        /// Replaces the editor's squiggles. Coordinates are Monaco's own, i.e. one-based; pass
        /// an empty array to clear.
        /// </summary>
        public CodeEditor SetMarkers(ReadOnlyArray<CodeMarker> markers)
        {
            if (Instance is null) return this;

            // ReadOnlyArray<T> is the underlying array at runtime, so this needs no conversion.
            Script.Write("monaco.editor.setModelMarkers({0}.getModel(), 'tss-monaco', {1})", Instance, markers);

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
            if (Instance is null || Text != code) return;

            if (diagnostics is object && diagnostics.Length > 0)
            {
                SetDiagnostics(diagnostics);
            }
        }

        #endregion

        protected override object Create(HTMLElement container)
        {
            _onBeforeCreate?.Invoke();

            dynamic options = BuildBaseOptions(_language, _text, _readOnly, _wordWrap, _autoHeight);

            options.snippetSuggestions = "bottom";
            options.suggest            = new { preview = true };
            options.suggestFontSize    = 10;
            options.inlineSuggest      = new { enabled = true, showToolbar = "always" };
            options.hover              = new { enabled = true, sticky = true, delay = HOVER_REQUEST_DELAY_MS, hidingDelay = 300 };

            _configureOptions?.Invoke(options);

            ApplyOverflowWidgetsHost(options);

            var editor = Script.Write<object>("monaco.editor.create({0}, {1})", container, options);

            Action onChanged = () => _onChanged?.Invoke();

            Script.Write("{0}.onDidChangeModelContent({1})", editor, onChanged);
            Script.Write("{0}.getModel().updateOptions({ tabSize: 4, insertSpaces: true })", editor);

            return editor;
        }

        protected override void AfterCreate()
        {
            var editor   = Instance;
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

        private void RegisterCompletionProvider(object editor, string language)
        {
            if (_onCompletion is null && _onResolveCompletion is null) return;

            // Monaco asks every provider registered for the language; answering only for our own
            // model is what keeps two editors on the same language independent.
            Func<dynamic, dynamic, IPromise> provideCompletionItems = (model, position) =>
            {
                if (Script.Write<bool>("{0}.getModel() != {1}", editor, model)) return null;

                return _onCompletion?.Invoke(model, position);
            };

            Func<dynamic, dynamic, CompletionItem, dynamic, CompletionItem> resolveCompletionItem =
                (model, position, item, token) => _onResolveCompletion?.Invoke(model, position, item, token) ?? item;

            _completionProvider = Script.Write<dynamic>(
                @"monaco.languages.registerCompletionItemProvider({0}, {
                    provideCompletionItems: (model, position) => {1}(model, position),
                    resolveCompletionItem: (model, position, item, token) => {2}(model, position, item, token)
                })",
                language,
                provideCompletionItems,
                resolveCompletionItem
            );
        }

        private void RegisterHoverProvider(object editor, string language)
        {
            if (_onHover is null) return;

            Func<dynamic, dynamic, IPromise> provideHover = (model, position) => _onHover?.Invoke(model, position);

            // Monaco cancels a hover request as soon as the pointer moves on. Honouring the token -
            // rather than resolving late - is what stops a stale tooltip from flashing up over the
            // wrong symbol, and stops a rejected provider promise from being logged as an error.
            _hoverProvider = Script.Write<dynamic>(
                @"monaco.languages.registerHoverProvider({0}, {
                    provideHover: (model, position, token) => {
                        return new Promise((resolve, reject) => {
                            if ({1}.getModel() != model) { resolve(null); return; }

                            var completed = false;
                            var cancelRegistration = null;

                            var cleanup = () => {
                                if (cancelRegistration && cancelRegistration.dispose) {
                                    cancelRegistration.dispose();
                                    cancelRegistration = null;
                                }
                            };

                            var finish = (value) => {
                                if (completed) { return; }
                                completed = true;
                                cleanup();
                                resolve(value);
                            };

                            if (token && token.onCancellationRequested) {
                                cancelRegistration = token.onCancellationRequested(() => finish(null));
                            }

                            if (completed || (token && token.isCancellationRequested) || {1}.getModel() != model) {
                                finish(null);
                                return;
                            }

                            try {
                                var hoverResult = {2}(model, position);

                                if (!hoverResult || typeof hoverResult.then !== 'function') {
                                    finish(hoverResult || null);
                                    return;
                                }

                                hoverResult.then(
                                    value => {
                                        if ((token && token.isCancellationRequested) || {1}.getModel() != model) {
                                            finish(null);
                                            return;
                                        }
                                        finish(value);
                                    },
                                    error => {
                                        cleanup();
                                        if (completed || (token && token.isCancellationRequested)) { resolve(null); return; }
                                        completed = true;
                                        reject(error);
                                    }
                                );
                            } catch(e) {
                                cleanup();
                                if (completed || (token && token.isCancellationRequested) || {1}.getModel() != model) { resolve(null); return; }
                                completed = true;
                                console.error(e);
                                reject(e);
                            }
                        });
                    }
                })",
                language,
                editor,
                provideHover
            );
        }

        private void RegisterFormattingProviders(object editor, string language)
        {
            if (_onFormat is null) return;

            Func<dynamic, dynamic, dynamic, IPromise> provideDocumentFormattingEdits = (model, formattingOptions, token) =>
            {
                if (Script.Write<bool>("{0}.getModel() != {1}", editor, model)) return null;

                return MonacoEditor.AsPromise(FormatWholeModelAsync(model));
            };

            Func<dynamic, dynamic, dynamic, dynamic, IPromise> provideDocumentRangeFormattingEdits = (model, range, formattingOptions, token) =>
            {
                if (Script.Write<bool>("{0}.getModel() != {1}", editor, model)) return null;

                return MonacoEditor.AsPromise(FormatRangeAsync(model, range));
            };

            _formattingProvider = Script.Write<dynamic>(
                @"monaco.languages.registerDocumentFormattingEditProvider({0}, {
                    displayName: 'Tesserae.Monaco',
                    provideDocumentFormattingEdits: async (model, options, token) => await {1}(model, options, token)
                })",
                language,
                provideDocumentFormattingEdits
            );

            _rangeFormattingProvider = Script.Write<dynamic>(
                @"monaco.languages.registerDocumentRangeFormattingEditProvider({0}, {
                    displayName: 'Tesserae.Monaco',
                    provideDocumentRangeFormattingEdits: async (model, range, options, token) => await {1}(model, range, options, token)
                })",
                language,
                provideDocumentRangeFormattingEdits
            );
        }

        // Word wrap is a per-editor view preference, so it belongs on the editor's own context menu
        // rather than in the host application's chrome. Kept in sync with _wordWrap so IsWordWrapped
        // still answers correctly after the user toggles it here.
        private void AddWordWrapAction(object editor)
        {
            Action<bool> setWordWrap = wrap => _wordWrap = wrap;

            Script.Write(
                @"{0}.addAction({
                    id: 'tssm.toggleWordWrap',
                    label: 'Toggle Word Wrap',
                    contextMenuGroupId: 'view',
                    contextMenuOrder: 1.5,
                    keybindings: [monaco.KeyMod.Alt | monaco.KeyCode.KeyZ],
                    run: function(ed) {
                        var current = ed.getOption(monaco.editor.EditorOption.wordWrap);
                        var next = (current === 'on') ? 'off' : 'on';
                        ed.updateOptions({ wordWrap: next });
                        {1}(next === 'on');
                    }
                })",
                editor,
                setWordWrap
            );
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

        private static void DisposeProvider(ref dynamic provider)
        {
            if (provider is null) return;

            provider.dispose();
            provider = null;
        }
    }
}
