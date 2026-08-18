using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Registers Monaco language providers on behalf of one editor, and owns their disposal.
    ///
    /// Three things are the same for every provider, and are why this exists rather than each
    /// registration being written out by hand:
    ///
    /// <list type="bullet">
    /// <item><b>Monaco's provider registry is global per language.</b> Every editor showing
    /// <c>csharp</c> is asked for completions, so each callback has to answer only for its own model or
    /// two editors reply to each other's requests. <see cref="OwnsModel"/> is that check, applied once
    /// per provider.</item>
    /// <item><b>Registrations outlive the editor.</b> Disposing an editor does not remove them, so each
    /// handle goes into the component's <see cref="DisposableBag"/> and is released on teardown -
    /// otherwise every mount leaves behind a provider bound to a dead model.</item>
    /// <item><b>Monaco wants shapes, not values.</b> Several providers must return an object carrying a
    /// <c>dispose</c> alongside the data, or coordinates wrapped in a <c>Uri</c>. Those conversions
    /// happen here, so a host returns plain C#.</item>
    /// </list>
    /// </summary>
    public sealed class ProviderHost
    {
        private readonly IStandaloneCodeEditor _editor;
        private readonly string                _language;
        private readonly DisposableBag         _bag;

        internal ProviderHost(IStandaloneCodeEditor editor, string language, DisposableBag bag)
        {
            _editor   = editor;
            _language = string.IsNullOrWhiteSpace(language) ? "plaintext" : language;
            _bag      = bag;
        }

        /// <summary>The language id these providers are registered for.</summary>
        public string Language => _language;

        /// <summary>
        /// Whether a model Monaco handed a provider is the one this editor is showing. Every callback
        /// below starts with this; without it, two editors on one language answer each other's requests.
        /// </summary>
        public bool OwnsModel(ITextModel model) => _editor.getModel() == model;

        /// <summary>Keeps a registration so it is released when the component is torn down.</summary>
        public void Keep(IJsDisposable registration) => _bag.Add(registration);

        private static readonly Action NOTHING_TO_DISPOSE = () => { };

        #region Signature help

        /// <summary>Parameter hints, shown as the user types an argument list.</summary>
        public void RegisterSignatureHelp(Func<CodeContext, Task<SignatureHelp>> handler, string[] triggerCharacters, string[] retriggerCharacters)
        {
            Keep(MonacoApi.languages.registerSignatureHelpProvider(_language, new SignatureHelpProvider
            {
                signatureHelpTriggerCharacters   = triggerCharacters   ?? new[] { "(", "," },
                signatureHelpRetriggerCharacters = retriggerCharacters ?? new[] { ")" },

                provideSignatureHelp = (model, position) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildSignatureHelpAsync(handler, model, position)) : null
            }));
        }

        private static async Task<object> BuildSignatureHelpAsync(Func<CodeContext, Task<SignatureHelp>> handler, ITextModel model, Position position)
        {
            var help = await handler(new CodeContext(model, position));

            if (help is null || help.signatures is null || help.signatures.Length == 0) return null;

            // Monaco takes ownership of the result and disposes it, so the help arrives wrapped.
            return new SignatureHelpResult { value = help, dispose = NOTHING_TO_DISPOSE };
        }

        #endregion

        #region Code actions

        /// <summary>Quick fixes and refactorings, offered through the lightbulb.</summary>
        public void RegisterCodeActions(Func<CodeActionContext, Task<CodeAction[]>> handler, string[] kinds)
        {
            Keep(MonacoApi.languages.registerCodeActionProvider(_language, new CodeActionProvider
            {
                providedCodeActionKinds = kinds ?? new[] { "quickfix" },

                provideCodeActions = (model, range, context) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildCodeActionsAsync(handler, model, range, context)) : null
            }));
        }

        private static async Task<object> BuildCodeActionsAsync(
            Func<CodeActionContext, Task<CodeAction[]>> handler,
            ITextModel                                  model,
            TextRange                                   range,
            ICodeActionContext                          context)
        {
            var markers = context?.markers ?? new CodeMarker[0];
            var actions = await handler(new CodeActionContext(model.getValue(), range, markers));

            var converted = new List<MonacoCodeAction>();

            foreach (var action in actions ?? new CodeAction[0])
            {
                if (action is null || string.IsNullOrWhiteSpace(action.title)) continue;

                var converting = new MonacoCodeAction
                {
                    title       = action.title,
                    kind        = string.IsNullOrWhiteSpace(action.kind) ? "quickfix" : action.kind,
                    isPreferred = action.isPreferred,
                    diagnostics = action.diagnostics ?? new CodeMarker[0]
                };

                if (action.edits is object && action.edits.Length > 0)
                {
                    converting.edit = new WorkspaceEdit { edits = ToWorkspaceEdits(model, action.edits) };
                }

                converted.Add(converting);
            }

            // An empty list still has to be a disposable list, or Monaco logs a provider error.
            return new CodeActionList { actions = Script.ToArray(converted.ToArray()), dispose = NOTHING_TO_DISPOSE };
        }

        private static WorkspaceTextEdit[] ToWorkspaceEdits(ITextModel model, TextEdit[] edits)
        {
            var converted = new WorkspaceTextEdit[edits.Length];

            for (var i = 0; i < edits.Length; i++)
            {
                converted[i] = new WorkspaceTextEdit { resource = model.uri, textEdit = edits[i] };
            }

            return Script.ToArray(converted);
        }

        #endregion

        #region Navigation

        /// <summary>Go to definition (F12).</summary>
        public void RegisterDefinition(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            Keep(MonacoApi.languages.registerDefinitionProvider(_language, new DefinitionProvider
            {
                provideDefinition = (model, position) => Locations(handler, model, position)
            }));
        }

        /// <summary>Go to declaration.</summary>
        public void RegisterDeclaration(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            Keep(MonacoApi.languages.registerDeclarationProvider(_language, new DeclarationProvider
            {
                provideDeclaration = (model, position) => Locations(handler, model, position)
            }));
        }

        /// <summary>Go to type definition.</summary>
        public void RegisterTypeDefinition(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            Keep(MonacoApi.languages.registerTypeDefinitionProvider(_language, new TypeDefinitionProvider
            {
                provideTypeDefinition = (model, position) => Locations(handler, model, position)
            }));
        }

        /// <summary>Go to implementation.</summary>
        public void RegisterImplementation(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            Keep(MonacoApi.languages.registerImplementationProvider(_language, new ImplementationProvider
            {
                provideImplementation = (model, position) => Locations(handler, model, position)
            }));
        }

        /// <summary>Find all references (Shift+F12), shown in the peek view.</summary>
        public void RegisterReferences(Func<CodeContext, Task<CodeLocation[]>> handler)
        {
            Keep(MonacoApi.languages.registerReferenceProvider(_language, new ReferenceProvider
            {
                provideReferences = (model, position) => Locations(handler, model, position)
            }));
        }

        // Every navigation provider has the same shape: a position in, locations out.
        private object Locations(Func<CodeContext, Task<CodeLocation[]>> handler, ITextModel model, Position position)
        {
            return OwnsModel(model) ? MonacoEditor.AsPromise(BuildLocationsAsync(handler, model, position)) : null;
        }

        private static async Task<object> BuildLocationsAsync(Func<CodeContext, Task<CodeLocation[]>> handler, ITextModel model, Position position)
        {
            var locations = await handler(new CodeContext(model, position));

            if (locations is null || locations.Length == 0) return null;

            var converted = new List<MonacoLocation>();

            foreach (var location in locations)
            {
                if (location is null || location.range is null) continue;

                // A location's uri is optional on our side: unset means "this document", which is what a
                // single-file host almost always wants and saves it building a Uri at all.
                converted.Add(new MonacoLocation
                {
                    uri   = string.IsNullOrWhiteSpace(location.uri) ? model.uri : MonacoUri.parse(location.uri),
                    range = location.range
                });
            }

            return Script.ToArray(converted.ToArray());
        }

        /// <summary>Highlights the other occurrences of the symbol under the caret.</summary>
        public void RegisterDocumentHighlights(Func<CodeContext, Task<DocumentHighlight[]>> handler)
        {
            Keep(MonacoApi.languages.registerDocumentHighlightProvider(_language, new DocumentHighlightProvider
            {
                provideDocumentHighlights = (model, position) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(NonEmptyAsync(handler(new CodeContext(model, position)))) : null
            }));
        }

        // Monaco treats an empty array and null alike, but null is the cheaper answer and keeps a
        // provider that has nothing to say out of the merge.
        private static async Task<object> NonEmptyAsync<T>(Task<T[]> pending)
        {
            var values = await pending;

            return values is null || values.Length == 0 ? null : Script.ToArray(values);
        }

        #endregion

        #region Symbols and rename

        /// <summary>The outline (Ctrl+Shift+O) and the sticky-scroll header.</summary>
        public void RegisterDocumentSymbols(Func<string, Task<DocumentSymbol[]>> handler)
        {
            Keep(MonacoApi.languages.registerDocumentSymbolProvider(_language, new DocumentSymbolProvider
            {
                displayName = "Tesserae.Monaco",

                provideDocumentSymbols = model =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildSymbolsAsync(handler, model)) : null
            }));
        }

        private static async Task<object> BuildSymbolsAsync(Func<string, Task<DocumentSymbol[]>> handler, ITextModel model)
        {
            var symbols = await handler(model.getValue());

            if (symbols is null || symbols.Length == 0) return null;

            Fix(symbols);

            return Script.ToArray(symbols);

            // Monaco requires `tags` to exist and `selectionRange` to sit inside `range`, and silently
            // drops a symbol that breaks either rule - so both are filled in rather than trusted.
            void Fix(DocumentSymbol[] level)
            {
                foreach (var symbol in level)
                {
                    if (symbol is null) continue;

                    if (symbol.tags is null)           symbol.tags           = new object[0];
                    if (symbol.selectionRange is null) symbol.selectionRange = symbol.range;

                    if (symbol.children is object && symbol.children.Length > 0) Fix(symbol.children);
                }
            }
        }

        /// <summary>Rename symbol (F2). The handler is given the new name and returns the edits.</summary>
        public void RegisterRename(Func<CodeContext, string, Task<TextEdit[]>> handler)
        {
            Keep(MonacoApi.languages.registerRenameProvider(_language, new RenameProvider
            {
                provideRenameEdits = (model, position, newName) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildRenameAsync(handler, model, position, newName)) : null
            }));
        }

        private static async Task<object> BuildRenameAsync(
            Func<CodeContext, string, Task<TextEdit[]>> handler,
            ITextModel                                  model,
            Position                                    position,
            string                                      newName)
        {
            var edits = await handler(new CodeContext(model, position), newName);

            // A rejection message is how Monaco reports "this can't be renamed" to the user.
            if (edits is null || edits.Length == 0)
            {
                return new WorkspaceEdit { edits = new WorkspaceTextEdit[0], rejectReason = "Nothing to rename here." };
            }

            return new WorkspaceEdit { edits = ToWorkspaceEdits(model, edits) };
        }

        #endregion

        #region Inlay hints, code lenses, folding, links, colours

        /// <summary>Read-only annotations painted between the code.</summary>
        public void RegisterInlayHints(Func<string, TextRange, Task<InlayHint[]>> handler)
        {
            Keep(MonacoApi.languages.registerInlayHintsProvider(_language, new InlayHintsProvider
            {
                provideInlayHints = (model, range) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildInlayHintsAsync(handler, model, range)) : null
            }));
        }

        private static async Task<object> BuildInlayHintsAsync(Func<string, TextRange, Task<InlayHint[]>> handler, ITextModel model, TextRange range)
        {
            var hints = await handler(model.getValue(), range);

            return new InlayHintList { hints = Script.ToArray(hints ?? new InlayHint[0]), dispose = NOTHING_TO_DISPOSE };
        }

        /// <summary>
        /// Clickable annotations above a line. <paramref name="onClick"/> is called with the item the user
        /// clicked; Monaco routes it through a command, which is registered here.
        /// </summary>
        public void RegisterCodeLenses(Func<string, Task<CodeLensItem[]>> handler, Action<CodeLensItem> onClick)
        {
            // The lenses last handed out, so a click resolves back to the item the host gave us rather
            // than to an index into an array Monaco owns.
            var provided = new CodeLensItem[0];

            var commandId = "tssm.codelens." + (++_lensSequence);

            Keep(MonacoApi.editor.registerCommand(commandId, (accessor, index) =>
            {
                var at = index is int i ? i : -1;

                if (onClick is object && at >= 0 && at < provided.Length) onClick(provided[at]);
            }));

            Keep(MonacoApi.languages.registerCodeLensProvider(_language, new CodeLensProvider
            {
                provideCodeLenses = model =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildCodeLensesAsync(handler, model, commandId, items => provided = items)) : null,

                resolveCodeLens = (model, lens) => lens
            }));
        }

        private static int _lensSequence;

        private static async Task<object> BuildCodeLensesAsync(
            Func<string, Task<CodeLensItem[]>> handler,
            ITextModel                         model,
            string                             commandId,
            Action<CodeLensItem[]>             remember)
        {
            var items = await handler(model.getValue()) ?? new CodeLensItem[0];

            remember(items);

            var lenses = new List<MonacoCodeLens>();

            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];

                if (item is null || item.range is null) continue;

                lenses.Add(new MonacoCodeLens
                {
                    range   = item.range,
                    command = new Command
                    {
                        id        = commandId,
                        title     = item.title ?? "",
                        tooltip   = item.tooltip,
                        arguments = Script.ToArray(new object[] { i })
                    }
                });
            }

            return new CodeLensList { lenses = Script.ToArray(lenses.ToArray()), dispose = NOTHING_TO_DISPOSE };
        }

        /// <summary>Custom foldable regions, replacing Monaco's indentation-based guess.</summary>
        public void RegisterFoldingRanges(Func<string, Task<FoldingRange[]>> handler)
        {
            Keep(MonacoApi.languages.registerFoldingRangeProvider(_language, new FoldingRangeProvider
            {
                provideFoldingRanges = model =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(NonEmptyAsync(handler(model.getValue()))) : null
            }));
        }

        /// <summary>
        /// Smart-expand ranges (Shift+Alt+Right). The handler returns the ranges around the caret from
        /// smallest to largest, and Monaco steps outwards through them.
        /// </summary>
        public void RegisterSelectionRanges(Func<CodeContext, Task<TextRange[]>> handler)
        {
            Keep(MonacoApi.languages.registerSelectionRangeProvider(_language, new SelectionRangeProvider
            {
                provideSelectionRanges = (model, positions) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildSelectionRangesAsync(handler, model, positions)) : null
            }));
        }

        // Monaco asks for one list per caret, so multi-cursor is answered by mapping over the positions
        // rather than by the host having to think about it.
        private static async Task<object> BuildSelectionRangesAsync(Func<CodeContext, Task<TextRange[]>> handler, ITextModel model, Position[] positions)
        {
            var perPosition = new List<SelectionRangeResult[]>();

            foreach (var position in positions ?? new Position[0])
            {
                var ranges = await handler(new CodeContext(model, position));
                var steps  = new List<SelectionRangeResult>();

                foreach (var range in ranges ?? new TextRange[0])
                {
                    if (range is object) steps.Add(new SelectionRangeResult { range = range });
                }

                perPosition.Add(Script.ToArray(steps.ToArray()));
            }

            return Script.ToArray(perPosition.ToArray());
        }

        /// <summary>Clickable links in the text.</summary>
        public void RegisterDocumentLinks(Func<string, Task<DocumentLink[]>> handler)
        {
            Keep(MonacoApi.languages.registerLinkProvider(_language, new LinkProvider
            {
                provideLinks = model =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildLinksAsync(handler, model)) : null
            }));
        }

        private static async Task<object> BuildLinksAsync(Func<string, Task<DocumentLink[]>> handler, ITextModel model)
        {
            var links = await handler(model.getValue());

            return new LinksList { links = Script.ToArray(links ?? new DocumentLink[0]), dispose = NOTHING_TO_DISPOSE };
        }

        /// <summary>
        /// Colour swatches and the inline picker. The presentations - what the picker writes back when the
        /// user chooses a colour - default to a CSS hex literal.
        /// </summary>
        public void RegisterColors(Func<string, Task<ColorInformation[]>> handler, Func<ColorValue, string> format)
        {
            var present = format ?? DefaultColorFormat;

            Keep(MonacoApi.languages.registerColorProvider(_language, new ColorProvider
            {
                provideDocumentColors = model =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(NonEmptyAsync(handler(model.getValue()))) : null,

                provideColorPresentations = (model, colorInfo) =>
                    Script.ToArray(new[] { new ColorPresentation { label = present(colorInfo?.color) } })
            }));
        }

        private static string DefaultColorFormat(ColorValue color)
        {
            if (color is null) return "#000000";

            return "#" + Hex(color.red) + Hex(color.green) + Hex(color.blue);
        }

        private static string Hex(double component)
        {
            var value = (int)Math.Round(Math.Max(0, Math.Min(1, component)) * 255);
            var hex   = value.ToString("x");

            return hex.Length == 1 ? "0" + hex : hex;
        }

        #endregion

        #region Semantic tokens

        /// <summary>
        /// Server-driven highlighting on top of the Monarch tokenizer. The legend names the token types
        /// the packed data refers to, and those names are what a theme's rules match.
        /// </summary>
        public void RegisterSemanticTokens(SemanticTokensLegend legend, Func<string, Task<SemanticTokens>> handler)
        {
            Keep(MonacoApi.languages.registerDocumentSemanticTokensProvider(_language, new DocumentSemanticTokensProvider
            {
                getLegend = () => legend,

                provideDocumentSemanticTokens = model =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildSemanticTokensAsync(handler, model)) : null,

                releaseDocumentSemanticTokens = _ => { }
            }));
        }

        private static async Task<object> BuildSemanticTokensAsync(Func<string, Task<SemanticTokens>> handler, ITextModel model)
        {
            var tokens = await handler(model.getValue());

            if (tokens is null || tokens.Data is null || tokens.Data.Length == 0) return null;

            // Monaco reads the packed tokens as a Uint32Array; a plain array is silently ignored, and
            // building one also drops the $type marker a C# array carries.
            return new SemanticTokensResult { data = new Uint32Array(tokens.Data) };
        }

        #endregion

        #region On-type formatting, linked editing, inline completions

        /// <summary>
        /// Reformats as the user types one of <paramref name="triggerCharacters"/> - closing a brace
        /// re-indenting its line, for instance.
        /// </summary>
        public void RegisterOnTypeFormatting(Func<CodeContext, string, Task<TextEdit[]>> handler, string[] triggerCharacters)
        {
            Keep(MonacoApi.languages.registerOnTypeFormattingEditProvider(_language, new OnTypeFormattingEditProvider
            {
                autoFormatTriggerCharacters = triggerCharacters ?? new[] { "}", ";", "\n" },

                provideOnTypeFormattingEdits = (model, position, ch) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(NonEmptyAsync(handler(new CodeContext(model, position), ch))) : null
            }));
        }

        /// <summary>
        /// Ranges that should be edited together - an HTML tag and its closing tag being the canonical
        /// case. Monaco mirrors the user's typing across all of them.
        /// </summary>
        public void RegisterLinkedEditing(Func<CodeContext, Task<TextRange[]>> handler)
        {
            Keep(MonacoApi.languages.registerLinkedEditingRangeProvider(_language, new LinkedEditingRangeProvider
            {
                provideLinkedEditingRanges = (model, position) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildLinkedEditingAsync(handler, model, position)) : null
            }));
        }

        private static async Task<object> BuildLinkedEditingAsync(Func<CodeContext, Task<TextRange[]>> handler, ITextModel model, Position position)
        {
            var ranges = await handler(new CodeContext(model, position));

            // Fewer than two ranges is nothing to link.
            return ranges is null || ranges.Length < 2 ? null : new LinkedEditingRanges { ranges = Script.ToArray(ranges) };
        }

        /// <summary>
        /// Ghost-text suggestions ahead of the caret - the shape an AI completion takes. Distinct from
        /// <c>OnCompletion</c>: these are not a list the user picks from but a single inline proposal
        /// accepted with Tab.
        /// </summary>
        public void RegisterInlineCompletions(Func<CodeContext, Task<InlineCompletion[]>> handler)
        {
            Keep(MonacoApi.languages.registerInlineCompletionsProvider(_language, new InlineCompletionsProvider
            {
                provideInlineCompletions = (model, position) =>
                    OwnsModel(model) ? MonacoEditor.AsPromise(BuildInlineCompletionsAsync(handler, model, position)) : null,

                // Monaco calls this without checking that it exists, so it has to be here even though
                // there is nothing of ours to release.
                disposeInlineCompletions = (list, reason) => { }
            }));
        }

        private static async Task<object> BuildInlineCompletionsAsync(Func<CodeContext, Task<InlineCompletion[]>> handler, ITextModel model, Position position)
        {
            var items = await handler(new CodeContext(model, position));

            if (items is null || items.Length == 0) return null;

            var accepted = new List<InlineCompletion>();

            foreach (var item in items)
            {
                if (item is null || string.IsNullOrEmpty(item.insertText)) continue;

                // No range means insert at the caret, which is what a plain continuation wants.
                if (item.range is null) item.range = Ranges.At(position);

                accepted.Add(item);
            }

            return new InlineCompletionList { items = Script.ToArray(accepted.ToArray()) };
        }

        #endregion
    }

    /// <summary>
    /// Packs semantic tokens into the delta-encoded array Monaco expects.
    ///
    /// The encoding is five integers per token - <c>deltaLine, deltaStart, length, typeIndex,
    /// modifierSet</c> - each relative to the token before it, which is fiddly enough to get wrong by
    /// hand. Add tokens in document order and the builder keeps the running position.
    /// </summary>
    public sealed class SemanticTokenBuilder
    {
        private readonly List<uint> _data = new List<uint>();

        private int _lastLine   = 1;
        private int _lastColumn = 1;

        /// <summary>
        /// Adds one token. Coordinates are Monaco's own - one-based line and column - and tokens must be
        /// added in document order; one that goes backwards is dropped rather than mis-rendered.
        /// </summary>
        /// <param name="typeIndex">An index into the legend's <c>tokenTypes</c>.</param>
        /// <param name="modifierSet">A bit set of indexes into the legend's <c>tokenModifiers</c>.</param>
        public SemanticTokenBuilder Add(int lineNumber, int column, int length, int typeIndex, int modifierSet = 0)
        {
            if (length <= 0) return this;

            var deltaLine   = lineNumber - _lastLine;
            var deltaColumn = deltaLine == 0 ? column - _lastColumn : column - 1;

            if (deltaLine < 0 || deltaColumn < 0) return this;

            _data.Add((uint)deltaLine);
            _data.Add((uint)deltaColumn);
            _data.Add((uint)length);
            _data.Add((uint)typeIndex);
            _data.Add((uint)modifierSet);

            _lastLine   = lineNumber;
            _lastColumn = column;

            return this;
        }

        /// <summary>The packed tokens.</summary>
        public SemanticTokens Build() => new SemanticTokens(_data.ToArray());
    }
}
