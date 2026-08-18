using System;
using System.Threading.Tasks;
using Tesserae;
using Transpose;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>Anything Monaco hands back that has to be released - a provider registration, an event listener.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IJsDisposable
    {
        void dispose();
    }

    /// <summary>What the code editor and the diff editor have in common.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditor
    {
        /// <summary>Re-measures the editor against its container.</summary>
        void layout();

        /// <summary>Tears the editor down. Does <b>not</b> dispose models handed to it.</summary>
        void dispose();
    }

    /// <summary>Monaco's <c>IStandaloneCodeEditor</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IStandaloneCodeEditor : IEditor
    {
        string      getValue();
        void        setValue(string newValue);
        ITextModel  getModel();
        Position    getPosition();
        void        setPosition(Position position);
        void        revealLineInCenter(int lineNumber);
        void        focus();
        void        updateOptions(EditorOptions options);
        HTMLElement getDomNode();

        IJsDisposable onDidChangeModelContent(Action listener);
        IJsDisposable onDidChangeModelDecorations(Action listener);
        IJsDisposable addAction(EditorAction action);

        /// <summary>
        /// Looks up one of Monaco's built-in actions by id, e.g. <c>editor.action.formatDocument</c>.
        /// Returns <c>null</c> when the editor has no such action.
        /// </summary>
        IEditorAction getAction(string id);

        /// <summary>The vertical offset of a line, in pixels.</summary>
        double getTopForLineNumber(int lineNumber);

        void setModel(ITextModel model);

        /// <summary>Applies edits as one undoable step, moving the caret as a user edit would.</summary>
        void executeEdits(string source, ReadOnlyArray<TextEdit> edits);

        /// <summary>Closes the current undo step, so later edits undo separately.</summary>
        void pushUndoStop();

        /// <summary>
        /// Runs a command by id. Reaches more than <c>getAction</c> does - Monaco registers the
        /// navigation commands as keybinding rules rather than editor actions - at the cost of not
        /// reporting whether the id matched.
        /// </summary>
        void trigger(string source, string handlerId, object payload);

        /// <summary>The editor action with this id, or null. Only editor actions, not every command.</summary>
        IEditorAction getAction(string id);

        /// <summary>Caret, selections, scroll offset and folding state. Opaque - hand it back unchanged.</summary>
        IViewState saveViewState();

        void restoreViewState(IViewState state);

        TextSelection   getSelection();
        TextSelection[] getSelections();

        /// <summary>
        /// Monaco accepts either an <c>IRange</c> or an <c>ISelection</c> here, and one declaration
        /// cannot claim both - a second one with the same <c>[Name]</c> is emitted as
        /// <c>setSelection$1</c> and stops matching Monaco. The typed overloads live on the C# side.
        /// </summary>
        void setSelection(object rangeOrSelection);

        void setSelections(ReadOnlyArray<TextSelection> selections);

        void revealLine(int lineNumber);
        void revealLineInCenterIfOutsideViewport(int lineNumber);
        void revealLineNearTop(int lineNumber);
        void revealPosition(Position position);
        void revealPositionInCenter(Position position);
        void revealRange(TextRange range);
        void revealRangeInCenter(TextRange range);
        void revealRangeInCenterIfOutsideViewport(TextRange range);
        void revealRangeAtTop(TextRange range);

        double getScrollTop();
        void   setScrollTop(double scrollTop);
        double getScrollLeft();
        void   setScrollLeft(double scrollLeft);
        double getScrollHeight();

        /// <summary>The height the content needs, in pixels - what to size a container to.</summary>
        double getContentHeight();

        double getContentWidth();

        bool hasTextFocus();

        /// <summary>
        /// A decoration set Monaco keeps tracked across edits. Pass null for an empty one: Monaco
        /// only applies the argument when it is truthy.
        /// </summary>
        IEditorDecorationsCollection createDecorationsCollection(ReadOnlyArray<TextDecoration> decorations);

        void addContentWidget(ContentWidgetDescriptor widget);
        void layoutContentWidget(ContentWidgetDescriptor widget);
        void removeContentWidget(ContentWidgetDescriptor widget);
        void addOverlayWidget(OverlayWidgetDescriptor widget);
        void removeOverlayWidget(OverlayWidgetDescriptor widget);

        /// <summary>Opens or closes bands of space between lines, through the accessor it hands the callback.</summary>
        void changeViewZones(Action<IViewZoneAccessor> callback);

        /// <summary>Binds a keybinding to a handler with no menu entry. Returns the command id, or null.</summary>
        string addCommand(int keybinding, Action handler, string context);

        /// <summary>A named boolean this editor's actions can be gated on.</summary>
        IContextKey createContextKey(string key, bool defaultValue);

        IJsDisposable onDidFocusEditorText(Action listener);
        IJsDisposable onDidBlurEditorText(Action listener);
        IJsDisposable onDidFocusEditorWidget(Action listener);
        IJsDisposable onDidBlurEditorWidget(Action listener);
        IJsDisposable onKeyDown(Action<IKeyboardEvent> listener);
        IJsDisposable onKeyUp(Action<IKeyboardEvent> listener);
        IJsDisposable onMouseDown(Action<IEditorMouseEvent> listener);
        IJsDisposable onMouseUp(Action<IEditorMouseEvent> listener);
        IJsDisposable onMouseMove(Action<IEditorMouseEvent> listener);
        IJsDisposable onMouseLeave(Action<IEditorMouseEvent> listener);
        IJsDisposable onContextMenu(Action<IEditorMouseEvent> listener);
        IJsDisposable onDidPaste(Action<IPasteEvent> listener);
        IJsDisposable onDidScrollChange(Action<IScrollEvent> listener);
        IJsDisposable onDidChangeCursorPosition(Action<ICursorPositionChangedEvent> listener);
        IJsDisposable onDidChangeCursorSelection(Action<ICursorSelectionChangedEvent> listener);
        IJsDisposable onDidChangeModel(Action listener);
        IJsDisposable onDidChangeModelLanguage(Action<ILanguageChangedEvent> listener);
        IJsDisposable onDidChangeConfiguration(Action listener);
        IJsDisposable onDidLayoutChange(Action listener);
        IJsDisposable onDidContentSizeChange(Action<IContentSizeChangedEvent> listener);
        IJsDisposable onDidAttemptReadOnlyEdit(Action listener);
        IJsDisposable onDidDispose(Action listener);

        /// <summary>
        /// The typed content event, alongside the argument-less <see cref="onDidChangeModelContent"/>
        /// the package already used. Declared under its own C# name because two declarations cannot
        /// share a <c>[Name]</c>.
        /// </summary>
        [Name("onDidChangeModelContent")]
        IJsDisposable onDidChangeModelContentDetailed(Action<IContentChangedEvent> listener);

        /// <summary>
        /// Reads an option by its <see cref="IEditorOptionIds"/> id. Monaco's <c>getOption</c> is
        /// heterogeneous - each id has its own value type - and one C# declaration can only claim
        /// one of them, so this covers the numeric ids. Two declarations cannot share a
        /// <c>[Name]</c>: overloads are emitted with a <c>$1</c> suffix and stop matching Monaco.
        /// </summary>
        [Name("getOption")]
        double getNumberOption(int optionId);
    }

    /// <summary>One of the editor's actions, as handed back by <c>getAction</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditorAction
    {
        string id    { get; }
        string label { get; }

        /// <summary>Runs the action. The promise resolves once it has finished.</summary>
        IPromise run();
    }

    /// <summary>Monaco's <c>IStandaloneDiffEditor</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IStandaloneDiffEditor : IEditor
    {
        void updateOptions(DiffEditorOptions options);
        void setModel(DiffEditorModel model);

        /// <summary><paramref name="target"/> is <c>"next"</c> or <c>"previous"</c>.</summary>
        void goToDiff(string target);

        IStandaloneCodeEditor getOriginalEditor();
        IStandaloneCodeEditor getModifiedEditor();

        /// <summary>
        /// Fires when the diff has been recomputed - which happens on a worker, well after the text
        /// changed. The only correct moment to read <see cref="getLineChanges"/>.
        /// </summary>
        IJsDisposable onDidUpdateDiff(Action listener);

        /// <summary>
        /// The changed blocks, as line ranges on both sides. Empty until the worker has run. An end line
        /// of 0 on a side means the block does not exist there: that is Monaco's encoding for a pure
        /// insertion or deletion.
        /// </summary>
        LineChange[] getLineChanges();

        /// <summary>The full diff result, whose <c>identical</c> flag honours <c>ignoreTrimWhitespace</c>.</summary>
        IDiffComputationResult getDiffComputationResult();
    }

    /// <summary>What the diff worker came back with, matching Monaco's <c>IDiffComputationResult</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IDiffComputationResult
    {
        bool identical { get; }

        /// <summary>True when the diff gave up early - see <c>MaxComputationTime</c>.</summary>
        bool quitEarly { get; }
    }

    /// <summary>Monaco's <c>ITextModel</c> - the document behind an editor.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ITextModel
    {
        string           getValue();
        void             setValue(string newValue);
        string           getValueInRange(TextRange range);
        TextRange        getFullModelRange();
        IWordAtPosition  getWordAtPosition(Position position);
        int              getLineCount();
        void             updateOptions(TextModelOptions options);
        void             dispose();

        /// <summary>The model's URI. What the TypeScript service resolves imports by, and what a JSON schema is matched against.</summary>
        IUri             uri { get; }

        string           getLanguageId();

        /// <summary>Increases on every change - the cheapest way to discard a stale async result.</summary>
        int              getVersionId();

        string           getLineContent(int lineNumber);
        int              getOffsetAt(Position position);
        Position         getPositionAt(int offset);

        /// <summary>
        /// Monaco's signature is
        /// <c>(searchString, searchOnlyEditableRange, isRegex, matchCase, wordSeparators, captureMatches, limitResultCount)</c>.
        /// <paramref name="wordSeparators"/> is null for a substring match, or the separator set for a
        /// whole-word one.
        /// </summary>
        IFindMatch[]     findMatches(string searchString, bool searchOnlyEditableRange, bool isRegex, bool matchCase, string wordSeparators, bool captureMatches, int limitResultCount);

        /// <summary>Applies edits without resetting the undo stack. The computer may return null.</summary>
        void             pushEditOperations(ReadOnlyArray<TextSelection> beforeCursorState, ReadOnlyArray<TextEdit> editOperations, Func<object, object> cursorStateComputer);

        void             pushStackElement();
        void             undo();
        void             redo();

        /// <summary>Normalises line endings. 0 is LF, 1 is CRLF.</summary>
        void             setEOL(int eol);

        IJsDisposable    onDidChangeContent(Action<IContentChangedEvent> listener);
    }

    /// <summary>Monaco's <c>IWordAtPosition</c>, or null when the caret isn't on a word.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IWordAtPosition
    {
        string word { get; }
        int    startColumn { get; }
        int    endColumn { get; }
    }

    /// <summary>
    /// Monaco's <c>CancellationToken</c>. Every language provider is handed one and is expected to
    /// stop as soon as it fires - a hover is cancelled the moment the pointer moves on.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface ICancellationToken
    {
        bool          isCancellationRequested { get; }
        IJsDisposable onCancellationRequested(Action listener);
    }

    /// <summary>Monaco's <c>FormattingOptions</c>, as passed to a formatting provider.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IFormattingOptions
    {
        int  tabSize { get; }
        bool insertSpaces { get; }
    }

    /// <summary><c>monaco.editor</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditorApi
    {
        IStandaloneCodeEditor create(HTMLElement container, EditorOptions options);
        IStandaloneDiffEditor createDiffEditor(HTMLElement container, DiffEditorOptions options);

        /// <summary>
        /// Creates a document. <paramref name="uri"/> may be null, which Monaco treats as "invent one" -
        /// declared as three parameters rather than two overloads, since overloads cannot share a
        /// <c>[Name]</c>.
        /// </summary>
        ITextModel            createModel(string value, string language, IUri uri);

        /// <summary>The model registered for a URI, or null. Creating a second model for a taken URI throws.</summary>
        ITextModel            getModel(IUri uri);

        ITextModel[]          getModels();

        /// <summary>Every editor on the page in creation order, including a diff editor's two inner ones.</summary>
        IStandaloneCodeEditor[] getEditors();

        /// <summary>Markers matching a filter - pass an empty filter for every marker on the page.</summary>
        CodeMarker[]          getModelMarkers(MarkerFilter filter);

        /// <summary>
        /// Fires when markers change on any model - including asynchronously, when one of Monaco's own
        /// workers finishes. The only way to see a worker's diagnostics: they arrive well after the edit.
        /// </summary>
        IJsDisposable         onDidChangeMarkers(Action listener);

        /// <summary>Syntax-highlights a string to HTML, with no editor instance involved.</summary>
        IPromise              colorize(string text, string languageId, ColorizeOptions options);

        /// <summary>Highlights the code already inside an element, in place.</summary>
        void                  colorizeElement(HTMLElement element, ColorizeElementOptions options);

        /// <summary>Registers a command a code lens (or any other command reference) can name.</summary>
        IJsDisposable         registerCommand(string id, Action<object, object> handler);

        /// <summary>A Monaco-managed worker running the host's own module.</summary>
        object                createWebWorker(WebWorkerOptions options);

        void setModelLanguage(ITextModel model, string mimeTypeOrLanguageId);

        /// <summary>
        /// Replaces the markers a given owner contributed. <see cref="ReadOnlyArray{T}"/> is the
        /// underlying array at runtime, so it crosses into Monaco with no copy.
        /// </summary>
        void setModelMarkers(ITextModel model, string owner, ReadOnlyArray<CodeMarker> markers);

        void defineTheme(string themeName, StandaloneThemeData themeData);
        void setTheme(string themeName);

        /// <summary>Re-measures character widths, e.g. after a web font has landed.</summary>
        void remeasureFonts();

        /// <summary>The option-id table <see cref="IStandaloneCodeEditor.getNumberOption"/> indexes into.</summary>
        IEditorOptionIds EditorOption { get; }
    }

    /// <summary>
    /// The ids in Monaco's <c>EditorOption</c> table. Only the ones this package reads are declared;
    /// the full table has well over a hundred entries.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditorOptionIds
    {
        int lineHeight { get; }
        int fontSize { get; }
        int readOnly { get; }
        int wordWrap { get; }
        int lineNumbers { get; }
        int glyphMargin { get; }
        int minimap { get; }
    }

    /// <summary><c>monaco.languages</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ILanguagesApi
    {
        void register(LanguageRegistration language);
        void setMonarchTokensProvider(string languageId, object monarchLanguage);
        void setLanguageConfiguration(string languageId, object configuration);

        IJsDisposable registerCompletionItemProvider(string languageId, CompletionItemProvider provider);
        IJsDisposable registerHoverProvider(string languageId, HoverProvider provider);
        IJsDisposable registerDocumentFormattingEditProvider(string languageId, DocumentFormattingEditProvider provider);
        IJsDisposable registerDocumentRangeFormattingEditProvider(string languageId, DocumentRangeFormattingEditProvider provider);

        IJsDisposable registerSignatureHelpProvider(string languageId, SignatureHelpProvider provider);
        IJsDisposable registerCodeActionProvider(string languageId, CodeActionProvider provider);
        IJsDisposable registerDefinitionProvider(string languageId, DefinitionProvider provider);
        IJsDisposable registerDeclarationProvider(string languageId, DeclarationProvider provider);
        IJsDisposable registerTypeDefinitionProvider(string languageId, TypeDefinitionProvider provider);
        IJsDisposable registerImplementationProvider(string languageId, ImplementationProvider provider);
        IJsDisposable registerReferenceProvider(string languageId, ReferenceProvider provider);
        IJsDisposable registerDocumentHighlightProvider(string languageId, DocumentHighlightProvider provider);
        IJsDisposable registerDocumentSymbolProvider(string languageId, DocumentSymbolProvider provider);
        IJsDisposable registerRenameProvider(string languageId, RenameProvider provider);
        IJsDisposable registerInlayHintsProvider(string languageId, InlayHintsProvider provider);
        IJsDisposable registerCodeLensProvider(string languageId, CodeLensProvider provider);
        IJsDisposable registerFoldingRangeProvider(string languageId, FoldingRangeProvider provider);
        IJsDisposable registerSelectionRangeProvider(string languageId, SelectionRangeProvider provider);
        IJsDisposable registerLinkProvider(string languageId, LinkProvider provider);
        IJsDisposable registerColorProvider(string languageId, ColorProvider provider);
        IJsDisposable registerDocumentSemanticTokensProvider(string languageId, DocumentSemanticTokensProvider provider);
        IJsDisposable registerOnTypeFormattingEditProvider(string languageId, OnTypeFormattingEditProvider provider);
        IJsDisposable registerLinkedEditingRangeProvider(string languageId, LinkedEditingRangeProvider provider);
        IJsDisposable registerInlineCompletionsProvider(string languageId, InlineCompletionsProvider provider);

        /// <summary>The bundled JSON language service. See <c>MonacoEditor.ConfigureJson</c>.</summary>
        IJsonApi       json { get; }

        /// <summary>The bundled TypeScript and JavaScript language service.</summary>
        ITypeScriptApi typescript { get; }

        /// <summary>The bundled CSS, SCSS and LESS language service.</summary>
        ICssApi        css { get; }

        /// <summary>The bundled HTML, Handlebars and Razor language service.</summary>
        IHtmlApi       html { get; }

        LanguageInfo[] getLanguages();
    }

    /// <summary>The modifier bits of a keybinding, OR-ed with a <see cref="IKeyCode"/>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IKeyMod
    {
        int CtrlCmd { get; }
        int Shift { get; }
        int Alt { get; }
        int WinCtrl { get; }
    }

    /// <summary>
    /// The key part of a keybinding, OR-ed with <see cref="IKeyMod"/> bits. A useful subset of
    /// Monaco's <c>KeyCode</c>; declaring more costs nothing at runtime, since none of this is emitted.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IKeyCode
    {
        int Backspace { get; }
        int Tab { get; }
        int Enter { get; }
        int Escape { get; }
        int Space { get; }
        int PageUp { get; }
        int PageDown { get; }
        int End { get; }
        int Home { get; }
        int LeftArrow { get; }
        int UpArrow { get; }
        int RightArrow { get; }
        int DownArrow { get; }
        int Delete { get; }

        int Digit0 { get; }
        int Digit1 { get; }
        int Digit2 { get; }
        int Digit3 { get; }
        int Digit4 { get; }
        int Digit5 { get; }
        int Digit6 { get; }
        int Digit7 { get; }
        int Digit8 { get; }
        int Digit9 { get; }

        int KeyA { get; }
        int KeyB { get; }
        int KeyC { get; }
        int KeyD { get; }
        int KeyE { get; }
        int KeyF { get; }
        int KeyG { get; }
        int KeyH { get; }
        int KeyI { get; }
        int KeyJ { get; }
        int KeyK { get; }
        int KeyL { get; }
        int KeyM { get; }
        int KeyN { get; }
        int KeyO { get; }
        int KeyP { get; }
        int KeyQ { get; }
        int KeyR { get; }
        int KeyS { get; }
        int KeyT { get; }
        int KeyU { get; }
        int KeyV { get; }
        int KeyW { get; }
        int KeyX { get; }
        int KeyY { get; }
        int KeyZ { get; }

        int F1 { get; }
        int F2 { get; }
        int F3 { get; }
        int F4 { get; }
        int F5 { get; }
        int F6 { get; }
        int F7 { get; }
        int F8 { get; }
        int F9 { get; }
        int F10 { get; }
        int F11 { get; }
        int F12 { get; }
    }
}
