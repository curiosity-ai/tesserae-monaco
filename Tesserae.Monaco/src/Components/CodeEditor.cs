using System;
using System.Collections.Generic;
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
    /// instance: <see cref="ProviderHost"/> gates each one on the model it was handed and disposes every
    /// registration when the component is torn down. That is what lets two editors share a language
    /// while answering differently.
    ///
    /// Everything shared with <see cref="CodeViewer"/> - text, selections, decorations, widgets, events,
    /// actions, options - comes from <see cref="MonacoTextComponent{T}"/>.
    /// </summary>
    [Transpose.Name("tssm.CodeEditor")]
    public sealed class CodeEditor : MonacoTextComponent<CodeEditor>
    {
        private const int    HOVER_REQUEST_DELAY_MS = 250;
        private const int    VALIDATION_DEBOUNCE_MS = 1_000;
        private const string FORMATTER_DISPLAY_NAME = "Tesserae.Monaco";

        private readonly bool _autoHeight;

        // Provider registrations, deferred until the editor exists and a ProviderHost can gate them.
        private readonly List<Action<ProviderHost>> _providers = new List<Action<ProviderHost>>();

        private Action                _onChanged;
        private Action                _onBeforeCreate;
        private Action<EditorOptions> _configureOptions;

        private Func<ITextModel, Position, IPromise>                     _onCompletion;
        private Func<ITextModel, Position, IPromise>                     _onHover;
        private Func<CompletionItem, ICancellationToken, object>         _onResolveCompletion;
        private Func<string, Task<string>>                               _onFormat;
        private Func<string, Task<ReadOnlyArray<CodeDiagnostic>>>        _validator;
        private bool                                                     _validateImmediately;

        private double _validationTimeoutId;

        protected override CodeEditor Self => this;

        internal CodeEditor(bool autoHeight)
        {
            _autoHeight = autoHeight;
        }

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

        /// <summary>
        /// Adjusts the Monaco construction options before creation, after the typed setters have run - so
        /// a caller here always wins. <see cref="EditorOptions"/> covers the common options; for one it
        /// does not declare, reach for <c>SetRawOption</c>, which names it in one place instead.
        /// </summary>
        public CodeEditor Options(Action<EditorOptions> configureOptions)
        {
            _configureOptions = configureOptions;

            return this;
        }

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

            return OnCompletionRaw((model, position) => MonacoEditor.AsPromise(ProviderHost.BuildCompletionListAsync(onCompletion, model, position)));
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
            _onResolveCompletion = onResolveCompletion is null
                ? (Func<CompletionItem, ICancellationToken, object>)null
                : (item, token) => onResolveCompletion(item, token);

            return this;
        }

        /// <summary>
        /// The same, for documentation that has to be fetched - which is the usual case, since
        /// resolving on demand only pays off when the work is expensive enough to be worth deferring.
        /// Monaco takes the promise and fills the details flyout in when it settles.
        ///
        /// Return the item to leave it as it was; a task that faults leaves the flyout empty rather
        /// than surfacing as an unhandled provider error, so handle the failure in the delegate if the
        /// caller should hear about it.
        /// </summary>
        public CodeEditor OnResolveCompletion(Func<CompletionItem, ICancellationToken, Task<CompletionItem>> onResolveCompletion)
        {
            _onResolveCompletion = onResolveCompletion is null
                ? (Func<CompletionItem, ICancellationToken, object>)null
                : (item, token) => MonacoEditor.AsPromise(onResolveCompletion(item, token));

            return this;
        }

        /// <summary>
        /// Ghost-text suggestions ahead of the caret, accepted with Tab - what an AI completion looks
        /// like. Distinct from <see cref="OnCompletion"/>, which is a list the user picks from.
        ///
        /// The editor already turns Monaco's inline-suggest UI on, so a provider here is all that is
        /// needed for it to appear.
        /// </summary>
        public CodeEditor OnInlineCompletion(Func<CodeContext, Task<InlineCompletion[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterInlineCompletions(handler));

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

        /// <summary>
        /// Reformats as the user types one of <paramref name="triggerCharacters"/> - closing a brace
        /// re-indenting its own line being the usual case.
        /// </summary>
        public CodeEditor OnTypeFormat(Func<CodeContext, string, Task<TextEdit[]>> handler, string[] triggerCharacters = null)
        {
            if (handler is object) _providers.Add(host => host.RegisterOnTypeFormatting(handler, triggerCharacters));

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

        #region Signature help

        /// <summary>
        /// Parameter hints, shown while the caret is inside an argument list. Monaco highlights the
        /// active parameter by matching <see cref="ParameterInformation.label"/> as a substring of the
        /// signature's own label, so the two have to agree exactly.
        /// </summary>
        public CodeEditor OnSignatureHelp(
            Func<CodeContext, Task<SignatureHelp>> handler,
            string[]                               triggerCharacters   = null,
            string[]                               retriggerCharacters = null)
        {
            if (handler is object) _providers.Add(host => host.RegisterSignatureHelp(handler, triggerCharacters, retriggerCharacters));

            return this;
        }

        #endregion
        #region Code actions

        /// <summary>
        /// Quick fixes and refactorings, offered through the lightbulb and Ctrl+. - the other half of
        /// <see cref="MonacoTextComponent{T}.SetDiagnostics"/>, which can report a problem but not
        /// offer to fix it.
        ///
        /// Filter on the context's markers to offer a fix only for a problem you actually reported.
        /// </summary>
        public CodeEditor OnCodeActions(Func<CodeActionContext, Task<CodeAction[]>> handler, string[] kinds = null)
        {
            if (handler is object) _providers.Add(host => host.RegisterCodeActions(handler, kinds));

            return this;
        }

        #endregion
        #region Navigation

        /// <summary>Go to definition (F12), and the Ctrl+click peek.</summary>
        public CodeEditor OnDefinition(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterDefinition(handler));

            return this;
        }

        /// <summary>Go to declaration, where a language separates it from the definition.</summary>
        public CodeEditor OnDeclaration(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterDeclaration(handler));

            return this;
        }

        /// <summary>Go to type definition.</summary>
        public CodeEditor OnTypeDefinition(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterTypeDefinition(handler));

            return this;
        }

        /// <summary>Go to implementation.</summary>
        public CodeEditor OnImplementation(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterImplementation(handler));

            return this;
        }

        /// <summary>Find all references (Shift+F12), listed in the peek view.</summary>
        public CodeEditor OnReferences(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterReferences(handler));

            return this;
        }

        /// <summary>
        /// Highlights the other occurrences of the symbol under the caret. Needs
        /// <c>OccurrencesHighlight("singleFile")</c>, which is the editor's default.
        /// </summary>
        public CodeEditor OnDocumentHighlights(Func<CodeContext, Task<DocumentHighlight[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterDocumentHighlights(handler));

            return this;
        }

        #endregion
        #region Symbols and rename

        /// <summary>
        /// The outline: what Ctrl+Shift+O lists and what the sticky-scroll header is built from. A
        /// symbol's <see cref="DocumentSymbol.selectionRange"/> is where the caret lands, and defaults
        /// to its full range when unset.
        /// </summary>
        public CodeEditor OnDocumentSymbols(Func<string, Task<DocumentSymbol[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterDocumentSymbols(handler));

            return this;
        }

        /// <summary>
        /// Rename symbol (F2). The handler receives the caret context and the new name, and returns
        /// every edit the rename implies. Returning nothing tells Monaco the symbol cannot be renamed.
        /// </summary>
        public CodeEditor OnRename(Func<CodeContext, string, Task<TextEdit[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterRename(handler));

            return this;
        }

        #endregion
        #region Inlay hints, code lenses, folding, links, colours, semantic tokens

        /// <summary>
        /// Read-only annotations painted between the code - an inferred type, a parameter name. The
        /// handler is given the whole text and the visible range, so it can answer for just what is
        /// on screen.
        /// </summary>
        public CodeEditor OnInlayHints(Func<string, TextRange, Task<InlayHint[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterInlayHints(handler));

            return this;
        }

        /// <summary>
        /// Clickable annotations above a line - "3 references", "run test". <paramref name="onClick"/>
        /// receives the item the user clicked.
        /// </summary>
        public CodeEditor OnCodeLenses(Func<string, Task<CodeLensItem[]>> handler, Action<CodeLensItem> onClick = null)
        {
            if (handler is object) _providers.Add(host => host.RegisterCodeLenses(handler, onClick));

            return this;
        }

        /// <summary>
        /// Custom foldable regions, replacing Monaco's indentation-based guess. Needs
        /// <c>Folding(true)</c>, which is Monaco's default.
        /// </summary>
        public CodeEditor OnFoldingRanges(Func<string, Task<FoldingRange[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterFoldingRanges(handler));

            return this;
        }

        /// <summary>
        /// Smart-expand ranges (Shift+Alt+Right): the ranges around the caret from smallest to largest,
        /// which Monaco steps outwards through.
        /// </summary>
        public CodeEditor OnSelectionRanges(Func<CodeContext, Task<TextRange[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterSelectionRanges(handler));

            return this;
        }

        /// <summary>Clickable links in the text. Needs <c>Links(true)</c>, which is Monaco's default.</summary>
        public CodeEditor OnDocumentLinks(Func<string, Task<DocumentLink[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterDocumentLinks(handler));

            return this;
        }

        /// <summary>
        /// Colour swatches with an inline picker. <paramref name="format"/> decides what the picker
        /// writes back; left null it produces a CSS hex literal.
        /// </summary>
        public CodeEditor OnColors(Func<string, Task<ColorInformation[]>> handler, Func<ColorValue, string> format = null)
        {
            if (handler is object) _providers.Add(host => host.RegisterColors(handler, format));

            return this;
        }

        /// <summary>
        /// Server-driven highlighting layered over the Monarch tokenizer. The legend's type names are
        /// what a theme's rules match, so they should line up with the token names used in
        /// <see cref="LanguageDefinition.TokenColors"/> - a type with no matching theme rule is coloured
        /// like ordinary text. Build the packed data with <see cref="SemanticTokenBuilder"/>.
        ///
        /// Semantic highlighting is switched on for this editor as a side effect; Monaco otherwise leaves
        /// it to the theme and never asks the provider.
        /// </summary>
        public CodeEditor OnSemanticTokens(SemanticTokensLegend legend, Func<string, Task<SemanticTokens>> handler)
        {
            if (handler is null || legend is null) return this;

            _providers.Add(host => host.RegisterSemanticTokens(legend, handler));

            // Monaco leaves semantic highlighting to the theme, and a standalone theme that says nothing
            // leaves it off - so a provider registered without this is simply never asked. Registering
            // one is unambiguous about wanting it.
            SemanticHighlighting();

            return this;
        }

        /// <summary>
        /// Ranges that should be edited together, such as an HTML tag and its closing tag. Fewer than
        /// two ranges is treated as "nothing to link".
        /// </summary>
        public CodeEditor OnLinkedEditing(Func<CodeContext, Task<TextRange[]>> handler)
        {
            if (handler is object) _providers.Add(host => host.RegisterLinkedEditing(handler));

            return this;
        }

        #endregion

        #region Diagnostics

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

            // Discard a stale result: the editor may have been torn down, or the text moved on.
            if (Surface is null || Text != code) return;

            if (diagnostics is object && diagnostics.Length > 0)
            {
                SetDiagnostics(diagnostics);
            }
        }

        #endregion

        protected override IEditor Create(HTMLElement container)
        {
            _onBeforeCreate?.Invoke();

            var options = BuildBaseOptions(InitialLanguage, InitialText, InitialReadOnly, InitialWordWrap, _autoHeight);

            options.snippetSuggestions = "bottom";
            options.suggest            = new SuggestOptions { preview = true };
            options.suggestFontSize    = 10;
            options.inlineSuggest      = new InlineSuggestOptions { enabled = true, showToolbar = "always" };
            options.hover              = new HoverOptions { enabled = true, sticky = true, delay = HOVER_REQUEST_DELAY_MS, hidingDelay = 300 };

            FinishOptions(options, OptionSetters, _configureOptions);

            var editor = MonacoApi.editor.create(container, options);

            editor.onDidChangeModelContent(() => _onChanged?.Invoke());

            return editor;
        }

        protected override void AfterCreate()
        {
            // Binds the shared surface and replays everything configured before mount - options, events,
            // actions, widgets, decorations and the saved view state.
            BindSurface();

            var editor   = Editor;
            var language = string.IsNullOrWhiteSpace(InitialLanguage) ? "plaintext" : InitialLanguage;
            var host     = new ProviderHost(editor, language, Disposables);

            RegisterCompletionProvider(host, editor);
            RegisterHoverProvider(host, editor);
            RegisterFormattingProviders(host, editor);

            foreach (var register in _providers)
            {
                register(host);
            }

            AddWordWrapAction(editor);

            if (_autoHeight) EnableAutoHeight();

            RaiseRendered();

            if (_validator is object && _validateImmediately)
            {
                Validate();
            }
        }

        private void RegisterCompletionProvider(ProviderHost host, IStandaloneCodeEditor editor)
        {
            host.RegisterCompletion(_onCompletion, _onResolveCompletion);
        }

        private void RegisterHoverProvider(ProviderHost host, IStandaloneCodeEditor editor)
        {
            if (_onHover is null) return;

            host.Keep(MonacoApi.languages.registerHoverProvider(host.Language, new HoverProvider
            {
                provideHover = (model, position, token) => MonacoEditor.AsPromise(ProvideHoverAsync(editor, model, position, token))
            }));
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

        private void RegisterFormattingProviders(ProviderHost host, IStandaloneCodeEditor editor)
        {
            if (_onFormat is null) return;

            host.Keep(MonacoApi.languages.registerDocumentFormattingEditProvider(host.Language, new DocumentFormattingEditProvider
            {
                displayName = FORMATTER_DISPLAY_NAME,

                provideDocumentFormattingEdits = (model, formattingOptions, token) =>
                    host.OwnsModel(model) ? MonacoEditor.AsPromise(FormatWholeModelAsync(model)) : null
            }));

            host.Keep(MonacoApi.languages.registerDocumentRangeFormattingEditProvider(host.Language, new DocumentRangeFormattingEditProvider
            {
                displayName = FORMATTER_DISPLAY_NAME,

                provideDocumentRangeFormattingEdits = (model, range, formattingOptions, token) =>
                    host.OwnsModel(model) ? MonacoEditor.AsPromise(FormatRangeAsync(model, range)) : null
            }));
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
                run                = _ => WordWrap(!IsWordWrapped)
            });
        }

        protected override void BeforeDispose()
        {
            clearTimeout(_validationTimeoutId);

            // Captures the text and the user's place in it, then drops the surface. Every provider
            // registration and event subscription is released by the base class's DisposableBag:
            // Monaco's provider registry is global, so disposing the editor does not remove them and
            // each mount would otherwise leak one bound to a dead model.
            UnbindSurface();
        }
    }
}
