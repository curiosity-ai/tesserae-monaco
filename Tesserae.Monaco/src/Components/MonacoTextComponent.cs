using System;
using System.Collections.Generic;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Everything a single-model Monaco editor can do, as a fluent API on the component.
    ///
    /// The operations themselves live once in <see cref="EditorSurface"/>; this class adds the two
    /// things a component needs on top of them. First, buffering: a component is configured in
    /// <c>Main</c>, long before it is mounted and long before Monaco has loaded, so a call made then
    /// is recorded and replayed against the editor once it exists - and replayed again if the
    /// component is removed from the DOM and re-added. Second, the fluent return type: the generic
    /// self-type is what lets <c>SetLanguage(...)</c> on a <see cref="CodeEditor"/> return a
    /// <see cref="CodeEditor"/> rather than this base, so a chain can mix shared and specific calls.
    ///
    /// Two kinds of call are distinguished on purpose. <see cref="Configure"/> is for standing
    /// configuration - an event subscription, an action, an option - which is recorded and survives a
    /// remount. <see cref="Live"/> is for transient acts - focusing, revealing, scrolling - which are
    /// dropped if the editor does not exist yet, because replaying them later would be wrong.
    ///
    /// The typed option setters go through <see cref="Option"/>, which records a mutator rather than a
    /// name/value pair: the same mutator run against the construction options and against a fresh
    /// <see cref="EditorOptions"/> gives both the initial value and the <c>updateOptions</c> patch, with
    /// no JavaScript names in strings anywhere.
    /// </summary>
    public abstract class MonacoTextComponent<T> : MonacoComponent where T : MonacoTextComponent<T>
    {
        /// <summary>The derived component, so shared members can return its type.</summary>
        protected abstract T Self { get; }

        private readonly List<Action<EditorSurface>> _configured    = new List<Action<EditorSurface>>();
        private readonly List<Action<EditorOptions>> _optionSetters = new List<Action<EditorOptions>>();

        private EditorSurface        _surface;
        private DecorationCollection _decorations;
        private ReadOnlyArray<TextDecoration> _pendingDecorations;
        private Action<T>            _onRendered;
        private EditorViewState      _savedViewState;
        private EditorHistory        _history;

        // Buffered until the editor exists.
        private string _text     = "";
        private string _language = "";
        private bool   _wordWrap;
        private bool   _readOnly;
        private int    _tabSize      = 4;
        private bool   _insertSpaces = true;

        /// <summary>
        /// The live editor's operation surface, or null before mount. Everything on it is also on the
        /// component, buffered; reach for this inside <see cref="OnRendered"/> when you want the raw
        /// surface, or to pass one function the same API a diff editor's sides expose.
        /// </summary>
        public EditorSurface Surface => _surface;

        /// <summary>
        /// The underlying Monaco <c>IStandaloneCodeEditor</c>, or null before mount.
        ///
        /// A direct cast, not <c>as</c>: <c>as</c> and <c>is</c> against an <c>[External]</c> interface
        /// compile to a runtime type test, and nothing is emitted for such a type - so the test reads
        /// <c>constructor</c> off undefined metadata and throws. A cast to one emits nothing at all.
        /// </summary>
        public IStandaloneCodeEditor Editor => (IStandaloneCodeEditor)Instance;

        /// <summary>The typed option setters recorded so far, for a derived component's construction options.</summary>
        protected IEnumerable<Action<EditorOptions>> OptionSetters => _optionSetters;

        #region Buffering

        /// <summary>
        /// Records a standing configuration step and applies it if the editor already exists. Recorded
        /// steps are replayed on a remount, so a component moved in the DOM keeps its events, actions
        /// and options.
        /// </summary>
        protected T Configure(Action<EditorSurface> op)
        {
            if (op is null) return Self;

            _configured.Add(op);

            if (_surface is object) op(_surface);

            return Self;
        }

        /// <summary>
        /// Applies a transient step, or drops it when there is no editor yet. Focusing or scrolling an
        /// editor that does not exist has no meaning, and replaying it after mount would surprise.
        /// </summary>
        protected T Live(Action<EditorSurface> op)
        {
            if (op is object && _surface is object) op(_surface);

            return Self;
        }

        /// <summary>
        /// Binds the surface to the freshly created editor and replays everything configured so far.
        /// Derived components call this first in <c>AfterCreate</c>.
        /// </summary>
        protected void BindSurface()
        {
            if (Instance is null) return;

            var editor = (IStandaloneCodeEditor)Instance;

            _surface = new EditorSurface(editor, Disposables);

            ApplyIndentation();

            foreach (var op in _configured)
            {
                op(_surface);
            }

            RestoreSavedViewState();
            ApplyPendingDecorations();

            // After the replay, so a restored revision lands on an editor that already has its
            // options, its events and its own remembered view state rather than racing them.
            _history?.Attach(_surface);
        }

        /// <summary>
        /// Captures what should survive a remount and drops the surface. Derived components call this
        /// from <c>BeforeDispose</c>.
        /// </summary>
        protected void UnbindSurface()
        {
            // Before the surface is dropped: the recorder still has to read the text it is flushing.
            _history?.Detach();

            if (_surface is object)
            {
                // Read the content and the user's place in it back into the fields, so a component that
                // is re-added to the DOM comes back as it was rather than as it was first configured.
                _text           = _surface.Text;
                _savedViewState = _surface.SaveViewState();
            }

            _surface     = null;
            _decorations = null;
        }

        private void RestoreSavedViewState()
        {
            if (_savedViewState is object && _savedViewState.HasValue)
            {
                _surface.RestoreViewState(_savedViewState);
            }
        }

        #endregion

        #region Content

        /// <summary>
        /// The editor's content. Reads from the live model once mounted.
        ///
        /// Assigning calls <c>setValue</c>, which resets the undo stack and the caret. Use
        /// <see cref="ApplyEdits"/> when the user should be able to undo the change, or when the caret
        /// should stay put.
        /// </summary>
        public string Text
        {
            get => _surface is null ? _text : _surface.Text;
            set
            {
                _text = value ?? "";

                if (_surface is object) _surface.Text = _text;
            }
        }

        /// <summary>Sets the content, skipping the write when it is unchanged.</summary>
        public T SetText(string text)
        {
            if (Text != text) Text = text;

            return Self;
        }

        /// <summary>The buffered initial text, for a derived component's construction options.</summary>
        protected string InitialText => _text;

        /// <summary>The buffered language id, for a derived component's construction options.</summary>
        protected string InitialLanguage => _language;

        /// <summary>The buffered read-only flag, for a derived component's construction options.</summary>
        protected bool InitialReadOnly => _readOnly;

        /// <summary>The buffered word-wrap flag, for a derived component's construction options.</summary>
        protected bool InitialWordWrap => _wordWrap;

        /// <summary>Sets the language by Monaco language id (<c>"csharp"</c>, <c>"json"</c>, …).</summary>
        public T SetLanguage(string language)
        {
            _language = language ?? "";

            if (_surface is object)
            {
                var model = _surface.Model;

                if (model is object) model.SetLanguage(_language);
            }

            return Self;
        }

        /// <summary>Registers <paramref name="language"/> if needed, then selects it.</summary>
        public T SetLanguage(LanguageDefinition language)
        {
            MonacoEditor.RegisterLanguage(language);

            return SetLanguage(language?.Id);
        }

        /// <summary>
        /// Picks the language from a file extension, if Monaco knows one for it.
        ///
        /// Deferred when Monaco has not loaded yet - the registry it resolves against does not exist
        /// until then, and a component is usually configured well before it is mounted, so answering
        /// "no such extension" there would silently leave every such editor in plain text.
        /// </summary>
        public T SetLanguageByExtension(string extension)
        {
            if (MonacoEditor.IsLoaded)
            {
                Resolve();
            }
            else
            {
                MonacoEditor.WhenLoaded(Resolve);
            }

            return Self;

            void Resolve()
            {
                if (MonacoEditor.TryGetLanguageIdForExtension(extension, out var languageId)) SetLanguage(languageId);
            }
        }

        /// <summary>The document being shown, or null before mount.</summary>
        public CodeModel Model => _surface is null ? null : _surface.Model;

        /// <summary>
        /// Shows a different document. The model that was showing is not disposed - it stays valid, so
        /// a host can switch back to it. Pair with <see cref="SaveViewState"/> per document.
        /// </summary>
        public T SetModel(CodeModel model)
        {
            return Configure(s => s.SetModel(model));
        }

        /// <summary>
        /// Applies edits without resetting the undo stack, as one undoable step. This is the
        /// non-destructive alternative to assigning <see cref="Text"/>.
        /// </summary>
        public T ApplyEdits(ReadOnlyArray<TextEdit> edits, string source = "tss-monaco")
        {
            return Live(s => s.ApplyEdits(edits, source));
        }

        /// <summary>Closes the current undo step, so later edits undo separately.</summary>
        public T PushUndoStop() => Live(s => s.PushUndoStop());

        /// <summary>Undoes the last step.</summary>
        public T Undo() => Live(s => s.Undo());

        /// <summary>Redoes the last undone step.</summary>
        public T Redo() => Live(s => s.Redo());

        /// <summary>How many lines the document has, or 0 before mount.</summary>
        public int LineCount
        {
            get
            {
                var model = Model;

                return model is null ? 0 : model.LineCount;
            }
        }

        /// <summary>The model's version id, which increases on every edit - use it to drop stale async work.</summary>
        public int VersionId
        {
            get
            {
                var model = Model;

                return model is null ? 0 : model.VersionId;
            }
        }

        /// <summary>One line's text, without its line ending. Lines are one-based.</summary>
        public string GetLineContent(int lineNumber)
        {
            var model = Model;

            return model is null ? null : model.GetLineContent(lineNumber);
        }

        /// <summary>The text inside a range.</summary>
        public string GetValueInRange(TextRange range)
        {
            var model = Model;

            return model is null ? null : model.GetValueInRange(range);
        }

        /// <summary>The character offset of a position - how most compiler APIs address code.</summary>
        public int GetOffsetAt(Position position)
        {
            var model = Model;

            return model is null ? 0 : model.GetOffsetAt(position);
        }

        /// <summary>The position of a character offset.</summary>
        public Position GetPositionAt(int offset)
        {
            var model = Model;

            return model is null ? null : model.GetPositionAt(offset);
        }

        /// <summary>The word at a position, or null when there isn't one.</summary>
        public string GetWordAt(Position position)
        {
            var model = Model;

            return model is null ? null : model.GetWordAt(position);
        }

        /// <summary>Every match for <paramref name="searchString"/>, as ranges.</summary>
        public TextRange[] FindMatches(
            string searchString,
            bool   isRegex   = false,
            bool   matchCase = false,
            bool   wholeWord = false,
            int    limit     = 1000)
        {
            var model = Model;

            return model is null
                ? new TextRange[0]
                : model.FindMatches(searchString, isRegex, matchCase, wholeWord, limit);
        }

        /// <summary>
        /// Sets the indentation this editor inserts and reports. These are model options rather than
        /// editor options, so they are applied to the model rather than through <c>updateOptions</c>.
        /// </summary>
        public T Indentation(int tabSize, bool insertSpaces = true)
        {
            _tabSize      = tabSize < 1 ? 1 : tabSize;
            _insertSpaces = insertSpaces;

            ApplyIndentation();

            return Self;
        }

        private void ApplyIndentation()
        {
            if (_surface is null) return;

            var model = _surface.Model;

            if (model is object) model.SetIndentation(_tabSize, _insertSpaces);
        }

        /// <summary>Normalises the document's line endings.</summary>
        public T EndOfLine(EndOfLineSequence eol)
        {
            return Configure(s =>
            {
                var model = s.Model;

                if (model is object) model.SetEndOfLine(eol);
            });
        }

        #endregion

        #region Markers

        /// <summary>
        /// Replaces this editor's squiggles for one owner, leaving other owners' alone - including the
        /// markers Monaco's own workers produce.
        /// </summary>
        public T SetMarkers(ReadOnlyArray<CodeMarker> markers, string owner = MonacoEditor.DEFAULT_MARKER_OWNER)
        {
            return Live(s =>
            {
                var model = s.Model;

                if (model is object) model.SetMarkers(markers, owner);
            });
        }

        /// <summary>Replaces this editor's squiggles from zero-based diagnostics.</summary>
        public T SetDiagnostics(ReadOnlyArray<CodeDiagnostic> diagnostics, string owner = MonacoEditor.DEFAULT_MARKER_OWNER)
        {
            if (diagnostics is null) return ClearMarkers(owner);

            var markers = new CodeMarker[diagnostics.Length];

            for (var i = 0; i < diagnostics.Length; i++)
            {
                markers[i] = diagnostics[i].ToMarker();
            }

            return SetMarkers(markers, owner);
        }

        /// <summary>Clears one owner's squiggles.</summary>
        public T ClearMarkers(string owner = MonacoEditor.DEFAULT_MARKER_OWNER)
        {
            return SetMarkers(new CodeMarker[0], owner);
        }

        /// <summary>
        /// Every squiggle on this editor's document, from every owner - the host's own and the ones
        /// Monaco's bundled JSON, TypeScript, CSS and HTML workers produce. This is what an error count
        /// or a "go to next problem" needs, and it is only readable once mounted.
        /// </summary>
        public CodeMarker[] GetMarkers()
        {
            var model = Model;

            return model is null ? new CodeMarker[0] : model.GetMarkers();
        }

        /// <summary>
        /// Runs <paramref name="handler"/> whenever the markers on any document change - including
        /// asynchronously, when a worker finishes. Pass the count through
        /// <see cref="GetMarkers"/> from inside it.
        /// </summary>
        public T OnMarkersChanged(Action handler)
        {
            if (handler is null) return Self;

            return Configure(_ => MonacoEditor.OnMarkersChanged(handler, Disposables));
        }

        #endregion

        #region View state

        /// <summary>
        /// Captures caret, selections, scroll offset and folding - what to keep when swapping the
        /// model, so the user does not lose their place.
        /// </summary>
        public EditorViewState SaveViewState()
        {
            return _surface is null ? null : _surface.SaveViewState();
        }

        /// <summary>Restores state from <see cref="SaveViewState"/>.</summary>
        public T RestoreViewState(EditorViewState state)
        {
            if (state is object) _savedViewState = state;

            return Live(s => s.RestoreViewState(state));
        }

        #endregion

        #region History

        /// <summary>
        /// Keeps this editor's history somewhere it survives a reload and a closed browser, and puts it
        /// back the next time the same document is opened.
        ///
        /// The default store is the browser's IndexedDB; <see cref="EditorHistoryOptions.Store"/> is
        /// where a server-backed one goes instead, or alongside. Both halves of the entry - the text
        /// and the caret/scroll/folding - come from what Monaco actually hands out serialisably; its
        /// undo <i>stack</i> is not among them, which is why a restored revision is applied as an edit
        /// rather than swapped in. See <see cref="EditorHistoryEntry"/>.
        ///
        /// <code>
        /// MonacoEditor.Editor()
        ///    .SetLanguage("csharp")
        ///    .PersistHistory(new EditorHistoryOptions
        ///    {
        ///        Scope      = $"user:{userId}",
        ///        DocumentId = "src/Program.cs"
        ///    });
        /// </code>
        ///
        /// Calling this a second time replaces the recorder, which drops whatever the previous one had
        /// pending - so configure it once, at construction, as with the rest of the component.
        /// </summary>
        public T PersistHistory(EditorHistoryOptions options)
        {
            if (options is null) return Self;

            _history?.Detach();
            _history = new EditorHistory(options);

            if (_surface is object) _history.Attach(_surface);

            return Self;
        }

        /// <summary>
        /// Shorthand for <see cref="PersistHistory(EditorHistoryOptions)"/> with only the two settings
        /// that have no sensible default.
        /// </summary>
        /// <param name="scope">The partition - a user id, a workspace id, or a composite of them.</param>
        /// <param name="documentId">The document within it, stable across sessions.</param>
        public T PersistHistory(string scope, string documentId)
        {
            return PersistHistory(new EditorHistoryOptions { Scope = scope, DocumentId = documentId });
        }

        /// <summary>
        /// The recorder attached by <see cref="PersistHistory(EditorHistoryOptions)"/>, or null. Use it
        /// to take a revision by hand (<c>SaveNowAsync</c>), list what is stored, or put an older one
        /// back. Valid before mount, like the rest of the component.
        /// </summary>
        public EditorHistory History => _history;

        #endregion

        #region Selection and position

        /// <summary>The one-based caret position, or null before mount.</summary>
        public Position GetPosition() => _surface is null ? null : _surface.GetPosition();

        /// <summary>Moves the caret.</summary>
        public T SetPosition(Position position) => Live(s => s.SetPosition(position));

        /// <summary>The primary selection, direction included, or null before mount.</summary>
        public TextSelection GetSelection() => _surface is null ? null : _surface.GetSelection();

        /// <summary>Every selection, primary first - more than one when multi-cursor is in play.</summary>
        public TextSelection[] GetSelections() => _surface is null ? new TextSelection[0] : _surface.GetSelections();

        /// <summary>Selects a range.</summary>
        public T SetSelection(TextRange range) => Live(s => s.SetSelection(range));

        /// <summary>Selects with an explicit direction.</summary>
        public T SetSelection(TextSelection selection) => Live(s => s.SetSelection(selection));

        /// <summary>Replaces every selection - how multi-cursor is set programmatically.</summary>
        public T SetSelections(ReadOnlyArray<TextSelection> selections) => Live(s => s.SetSelections(selections));

        /// <summary>The selected text, or an empty string.</summary>
        public string GetSelectedText() => _surface is null ? "" : _surface.GetSelectedText();

        /// <summary>Selects the whole document.</summary>
        public T SelectAll() => Live(s => s.SelectAll());

        #endregion

        #region Revealing and scrolling

        /// <summary>
        /// Scrolls a line to the middle of the viewport.
        ///
        /// Note this is <b>not</b> Monaco's <c>revealLine</c>, which scrolls as little as possible -
        /// that is <see cref="EnsureLineVisible"/>. The centring behaviour is what this method has
        /// always done, and changing it silently would move every caller's viewport.
        /// </summary>
        public T RevealLine(int lineNumber) => Live(s => s.RevealLineInCenter(lineNumber));

        /// <summary>Scrolls a line into view doing as little as possible - Monaco's own <c>revealLine</c>.</summary>
        public T EnsureLineVisible(int lineNumber) => Live(s => s.RevealLine(lineNumber));

        /// <summary>Scrolls a line to the middle of the viewport.</summary>
        public T RevealLineInCenter(int lineNumber) => Live(s => s.RevealLineInCenter(lineNumber));

        /// <summary>Centres a line only if it is off-screen - the right choice for "go to next match".</summary>
        public T RevealLineInCenterIfOutsideViewport(int lineNumber) => Live(s => s.RevealLineInCenterIfOutsideViewport(lineNumber));

        /// <summary>Scrolls a line near the top, leaving context below it.</summary>
        public T RevealLineNearTop(int lineNumber) => Live(s => s.RevealLineNearTop(lineNumber));

        /// <summary>Scrolls a position into view.</summary>
        public T RevealPosition(Position position) => Live(s => s.RevealPosition(position));

        /// <summary>Scrolls a position to the middle of the viewport.</summary>
        public T RevealPositionInCenter(Position position) => Live(s => s.RevealPositionInCenter(position));

        /// <summary>Scrolls a range into view.</summary>
        public T RevealRange(TextRange range) => Live(s => s.RevealRange(range));

        /// <summary>Scrolls a range to the middle of the viewport.</summary>
        public T RevealRangeInCenter(TextRange range) => Live(s => s.RevealRangeInCenter(range));

        /// <summary>Centres a range only if it is off-screen.</summary>
        public T RevealRangeInCenterIfOutsideViewport(TextRange range) => Live(s => s.RevealRangeInCenterIfOutsideViewport(range));

        /// <summary>Scrolls a range to the top of the viewport.</summary>
        public T RevealRangeAtTop(TextRange range) => Live(s => s.RevealRangeAtTop(range));

        /// <summary>The vertical scroll offset in pixels, or 0 before mount.</summary>
        public double GetScrollTop() => _surface is null ? 0 : _surface.GetScrollTop();

        /// <summary>Scrolls vertically to an absolute offset.</summary>
        public T SetScrollTop(double scrollTop) => Live(s => s.SetScrollTop(scrollTop));

        /// <summary>The horizontal scroll offset in pixels.</summary>
        public double GetScrollLeft() => _surface is null ? 0 : _surface.GetScrollLeft();

        /// <summary>Scrolls horizontally to an absolute offset.</summary>
        public T SetScrollLeft(double scrollLeft) => Live(s => s.SetScrollLeft(scrollLeft));

        /// <summary>The full scrollable height in pixels.</summary>
        public double GetScrollHeight() => _surface is null ? 0 : _surface.GetScrollHeight();

        /// <summary>The height the content needs in pixels - what to size a container to.</summary>
        public double GetContentHeight() => _surface is null ? 0 : _surface.GetContentHeight();

        /// <summary>The width the content needs in pixels.</summary>
        public double GetContentWidth() => _surface is null ? 0 : _surface.GetContentWidth();

        #endregion

        #region Focus

        /// <summary>Gives the editor keyboard focus.</summary>
        public T Focus() => Live(s => s.Focus());

        /// <summary>Whether the text area has focus.</summary>
        public bool HasTextFocus() => _surface is object && _surface.HasTextFocus();

        #endregion

        #region Decorations

        /// <summary>
        /// Replaces the component's decorations. Safe to call before mount and cheap to call often:
        /// one collection is kept and updated, so Monaco re-renders only the difference and the ranges
        /// stay tracked across edits.
        ///
        /// Reach for <see cref="CreateDecorations"/> when you need several independent sets - search
        /// hits and error highlights, say - that are cleared separately.
        /// </summary>
        public T Decorate(ReadOnlyArray<TextDecoration> decorations)
        {
            _pendingDecorations = decorations;

            ApplyPendingDecorations();

            return Self;
        }

        /// <summary>Clears the decorations set by <see cref="Decorate"/>.</summary>
        public T ClearDecorations()
        {
            _pendingDecorations = null;

            if (_decorations is object) _decorations.Clear();

            return Self;
        }

        /// <summary>Where <see cref="Decorate"/>'s decorations are now, after any edits since.</summary>
        public TextRange[] GetDecorationRanges()
        {
            return _decorations is null ? new TextRange[0] : _decorations.GetRanges();
        }

        private void ApplyPendingDecorations()
        {
            if (_surface is null || _pendingDecorations is null) return;

            if (_decorations is null) _decorations = _surface.CreateDecorations();

            _decorations.Set(_pendingDecorations);
        }

        /// <summary>
        /// A decoration collection of your own, independent of <see cref="Decorate"/>. Only available
        /// once mounted - call it from <see cref="OnRendered"/>.
        /// </summary>
        public DecorationCollection CreateDecorations(ReadOnlyArray<TextDecoration> initial = null)
        {
            return _surface is null ? null : _surface.CreateDecorations(initial);
        }

        #endregion

        #region Widgets and view zones

        /// <summary>Places a widget inside the text, anchored to a document position.</summary>
        public T AddContentWidget(ContentWidget widget) => Configure(s => s.AddContentWidget(widget));

        /// <summary>Re-places a content widget after its position changed.</summary>
        public T LayoutContentWidget(ContentWidget widget) => Live(s => s.LayoutContentWidget(widget));

        /// <summary>Removes a content widget.</summary>
        public T RemoveContentWidget(ContentWidget widget) => Live(s => s.RemoveContentWidget(widget));

        /// <summary>Pins a widget to a corner of the editor, outside the scrolling text.</summary>
        public T AddOverlayWidget(OverlayWidget widget) => Configure(s => s.AddOverlayWidget(widget));

        /// <summary>Removes an overlay widget.</summary>
        public T RemoveOverlayWidget(OverlayWidget widget) => Live(s => s.RemoveOverlayWidget(widget));

        /// <summary>Opens a band of space between two lines and renders an element into it.</summary>
        public T AddViewZone(ViewZone zone) => Configure(s => s.AddViewZone(zone));

        /// <summary>Closes a view zone.</summary>
        public T RemoveViewZone(ViewZone zone) => Live(s => s.RemoveViewZone(zone));

        #endregion

        #region Actions, commands and context keys

        /// <summary>Adds a labelled command, with optional keybindings and a context-menu entry.</summary>
        public T AddAction(EditorActionDescriptor action) => Configure(s => s.AddAction(action));

        /// <summary>Adds a labelled command from its parts.</summary>
        public T AddAction(string id, string label, Action<EditorSurface> run, int[] keybindings = null, string contextMenuGroupId = null)
        {
            return AddAction(new EditorActionDescriptor(id, label, run)
            {
                Keybindings        = keybindings,
                ContextMenuGroupId = contextMenuGroupId
            });
        }

        /// <summary>
        /// Binds a keybinding to a handler with no menu entry. Build the binding from
        /// <see cref="KeyMod"/> and <see cref="KeyCode"/>.
        /// </summary>
        public T AddCommand(int keybinding, Action handler) => Configure(s => s.AddCommand(keybinding, handler));

        /// <summary>A named boolean this editor's actions can be gated on. Only available once mounted.</summary>
        public IContextKey CreateContextKey(string key, bool defaultValue = false)
        {
            return _surface?.CreateContextKey(key, defaultValue);
        }

        /// <summary>
        /// Runs one of Monaco's built-in commands by id. This is how a host reaches Find, Format
        /// Document, Comment Line, Go to Definition and the rest without depending on a keybinding that
        /// differs per platform.
        ///
        /// Reaches more than <see cref="RunAction"/> does - including commands bound only by keybinding
        /// rule, which is what the navigation ones are - at the cost of not reporting whether the id
        /// matched anything.
        /// </summary>
        public T Trigger(string actionId, object payload = null) => Live(s => s.Trigger(actionId, payload));

        /// <summary>
        /// Runs a built-in editor action, reporting whether the id matched anything. Note the navigation
        /// commands are not editor actions - see <see cref="EditorSurface.RunAction"/>.
        /// </summary>
        public bool RunAction(string actionId) => _surface is object && _surface.RunAction(actionId);

        /// <summary>Go to the definition of the symbol at the caret, through <c>OnDefinition</c>.</summary>
        public T GoToDefinition() => Trigger("editor.action.revealDefinition");

        /// <summary>Open the references peek for the symbol at the caret, through <c>OnReferences</c>.</summary>
        public T ShowReferences() => Trigger("editor.action.referenceSearch.trigger");

        /// <summary>Start renaming the symbol at the caret, through <c>OnRename</c>.</summary>
        public T StartRename() => Trigger("editor.action.rename");

        /// <summary>Open the outline picker, populated by <c>OnDocumentSymbols</c>.</summary>
        public T ShowOutline() => Trigger("editor.action.quickOutline");

        /// <summary>Open the quick-fix menu, populated by <c>OnCodeActions</c>.</summary>
        public T ShowQuickFixes() => Trigger("editor.action.quickFix");

        /// <summary>Open the parameter hints, populated by <c>OnSignatureHelp</c>.</summary>
        public T ShowParameterHints() => Trigger("editor.action.triggerParameterHints");

        /// <summary>Whether Monaco has this action and it is currently enabled.</summary>
        public bool IsActionSupported(string actionId) => _surface is object && _surface.IsActionSupported(actionId);

        /// <summary>
        /// Formats the document through whatever formatting provider is registered - the same path as
        /// the Format Document keybinding, without depending on which one the platform uses.
        /// </summary>
        public bool Format() => RunAction("editor.action.formatDocument");

        /// <summary>Formats just the selection.</summary>
        public bool FormatSelection() => RunAction("editor.action.formatSelection");

        /// <summary>Opens the find widget.</summary>
        public bool ShowFind() => RunAction("actions.find");

        /// <summary>Opens the find-and-replace widget.</summary>
        public bool ShowReplace() => RunAction("editor.action.startFindReplaceAction");

        /// <summary>Toggles line comments on the selection.</summary>
        public bool ToggleLineComment() => RunAction("editor.action.commentLine");

        /// <summary>Opens the quick-suggest widget, as Ctrl+Space would.</summary>
        public bool ShowSuggestions() => RunAction("editor.action.triggerSuggest");

        /// <summary>
        /// Closes Monaco's transient over-the-caret message - "No definition found for 'x'" and its
        /// siblings. See <see cref="EditorSurface.CloseMessage"/> for when that is the right thing to
        /// do, and why it has to happen a turn after the provider answered.
        /// </summary>
        public T CloseMessage() => Live(s => s.CloseMessage());

        /// <summary>
        /// Opens the suggest widget's documentation pane and leaves it open. Worth it when the
        /// completions carry real documentation; it makes the widget considerably taller otherwise.
        /// </summary>
        public T ShowSuggestDetails(bool visible = true) => Configure(s => s.ShowSuggestDetails(visible));

        #endregion

        #region Events

        /// <summary>The text area gained focus.</summary>
        public T OnFocused(Action handler) => Configure(s => s.OnFocus(handler));

        /// <summary>The text area lost focus - the hook for saving on blur.</summary>
        public T OnBlurred(Action handler) => Configure(s => s.OnBlur(handler));

        /// <summary>The editor or one of its widgets gained focus.</summary>
        public T OnWidgetFocused(Action handler) => Configure(s => s.OnWidgetFocus(handler));

        /// <summary>The editor and all of its widgets lost focus.</summary>
        public T OnWidgetBlurred(Action handler) => Configure(s => s.OnWidgetBlur(handler));

        /// <summary>A key went down. Carries Monaco's <see cref="KeyCode"/>, not the browser's.</summary>
        public T OnKeyDown(Action<IKeyboardEvent> handler) => Configure(s => s.OnKeyDown(handler));

        /// <summary>A key came up.</summary>
        public T OnKeyUp(Action<IKeyboardEvent> handler) => Configure(s => s.OnKeyUp(handler));

        /// <summary>A mouse button went down.</summary>
        public T OnMouseDown(Action<IEditorMouseEvent> handler) => Configure(s => s.OnMouseDown(handler));

        /// <summary>A mouse button came up.</summary>
        public T OnMouseUp(Action<IEditorMouseEvent> handler) => Configure(s => s.OnMouseUp(handler));

        /// <summary>The pointer moved over the editor.</summary>
        public T OnMouseMove(Action<IEditorMouseEvent> handler) => Configure(s => s.OnMouseMove(handler));

        /// <summary>The pointer left the editor.</summary>
        public T OnMouseLeave(Action<IEditorMouseEvent> handler) => Configure(s => s.OnMouseLeave(handler));

        /// <summary>The context menu was requested; the target says what was right-clicked.</summary>
        public T OnContextMenu(Action<IEditorMouseEvent> handler) => Configure(s => s.OnContextMenu(handler));

        /// <summary>Text was pasted, with the range it landed in.</summary>
        public T OnPaste(Action<IPasteEvent> handler) => Configure(s => s.OnPaste(handler));

        /// <summary>The editor scrolled.</summary>
        public T OnScrollChanged(Action<IScrollEvent> handler) => Configure(s => s.OnScrollChanged(handler));

        /// <summary>The caret moved.</summary>
        public T OnCursorPositionChanged(Action<ICursorPositionChangedEvent> handler) => Configure(s => s.OnCursorPositionChanged(handler));

        /// <summary>The selection changed.</summary>
        public T OnSelectionChanged(Action<ICursorSelectionChangedEvent> handler) => Configure(s => s.OnCursorSelectionChanged(handler));

        /// <summary>
        /// The content changed, with what changed and the new version id - the detail the bare
        /// <c>OnChanged(Action)</c> overload throws away.
        /// </summary>
        public T OnContentChanged(Action<IContentChangedEvent> handler) => Configure(s => s.OnContentChanged(handler));

        /// <summary>The editor was pointed at a different model.</summary>
        public T OnModelChanged(Action handler) => Configure(s => s.OnModelChanged(handler));

        /// <summary>The model's language changed.</summary>
        public T OnLanguageChanged(Action<ILanguageChangedEvent> handler) => Configure(s => s.OnLanguageChanged(handler));

        /// <summary>An option changed, from <c>updateOptions</c> or a built-in action.</summary>
        public T OnConfigurationChanged(Action handler) => Configure(s => s.OnConfigurationChanged(handler));

        /// <summary>The editor re-laid out.</summary>
        public T OnLayoutChanged(Action handler) => Configure(s => s.OnLayoutChanged(handler));

        /// <summary>The content's own size changed - the right signal for sizing a container to fit.</summary>
        public T OnContentSizeChanged(Action<IContentSizeChangedEvent> handler) => Configure(s => s.OnContentSizeChanged(handler));

        /// <summary>The user tried to type into a read-only editor.</summary>
        public T OnAttemptReadOnlyEdit(Action handler) => Configure(s => s.OnAttemptReadOnlyEdit(handler));

        /// <summary>The underlying editor was disposed.</summary>
        public T OnEditorDisposed(Action handler) => Configure(s => s.OnDisposed(handler));

        /// <summary>Runs once the underlying editor exists. Handlers accumulate.</summary>
        public T OnRendered(Action<T> onRendered)
        {
            _onRendered += onRendered;

            return Self;
        }

        /// <summary>Invokes the <see cref="OnRendered"/> handlers. Derived components call this last in <c>AfterCreate</c>.</summary>
        protected void RaiseRendered()
        {
            _onRendered?.Invoke(Self);
        }

        #endregion

        #region Options

        /// <summary>
        /// Records a change to the editor's options and applies it if the editor already exists.
        ///
        /// The same mutator serves both paths: run against the construction options it sets the initial
        /// value, and run against an empty <see cref="EditorOptions"/> it produces exactly the patch
        /// <c>updateOptions</c> wants, since an <c>[ObjectLiteral]</c> emits only the fields assigned.
        /// </summary>
        protected T Option(Action<EditorOptions> set)
        {
            if (set is null) return Self;

            _optionSetters.Add(set);

            if (_surface is object)
            {
                var patch = new EditorOptions();

                set(patch);

                _surface.UpdateOptions(patch);
            }

            return Self;
        }

        /// <summary>
        /// Sets an option <see cref="EditorOptions"/> does not declare - the escape hatch for the rest of
        /// Monaco's several hundred, and the one place a JavaScript name is still a string.
        /// </summary>
        public T SetRawOption(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) return Self;

            return Option(options => Script.Set(options, name, value));
        }

        /// <summary>Makes the editor read-only.</summary>
        public T ReadOnly(bool readOnly = true)
        {
            _readOnly = readOnly;

            return Option(o => o.readOnly = readOnly);
        }

        /// <summary>Whether the editor is read-only.</summary>
        public bool IsReadOnly => _readOnly;

        /// <summary>Soft-wraps long lines instead of scrolling horizontally.</summary>
        public T WordWrap(bool wordWrap = true)
        {
            _wordWrap = wordWrap;

            return Option(o => o.wordWrap = wordWrap ? "on" : "off");
        }

        /// <summary>
        /// Whether lines are currently wrapped. Tracks the editor's own state, so it stays correct
        /// after the user toggles wrapping from the context menu.
        /// </summary>
        public bool IsWordWrapped => _wordWrap;

        internal void TrackWordWrap(bool wordWrap)
        {
            _wordWrap = wordWrap;
        }

        /// <summary>Shows the minimap. Off by default, since it costs width a small editor doesn't have.</summary>
        public T Minimap(bool enabled = true) => Option(o => o.minimap = new MinimapOptions { enabled = enabled });

        /// <summary>Shows line numbers.</summary>
        public T LineNumbers(bool enabled) => Option(o => o.lineNumbers = enabled ? "on" : "off");

        /// <summary>Line-number mode: <c>"on"</c>, <c>"off"</c>, <c>"relative"</c> or <c>"interval"</c>.</summary>
        public T LineNumbers(string mode) => Option(o => o.lineNumbers = mode);

        /// <summary>Shows the glyph margin - needed for a decoration's <c>glyphMarginClassName</c> to be visible.</summary>
        public T GlyphMargin(bool enabled = true) => Option(o => o.glyphMargin = enabled);

        /// <summary>Enables code folding.</summary>
        public T Folding(bool enabled = true) => Option(o => o.folding = enabled);

        /// <summary>Keeps the enclosing scope pinned to the top of the viewport.</summary>
        public T StickyScroll(bool enabled = true) => Option(o => o.stickyScroll = new StickyScrollOptions { enabled = enabled });

        /// <summary>Draws indentation guides.</summary>
        public T IndentGuides(bool enabled = true) => Option(o => o.guides = new GuidesOptions { indentation = enabled });

        /// <summary>Draws vertical rulers at the given columns.</summary>
        public T Rulers(int[] columns) => Option(o => o.rulers = Script.ToArray(columns ?? new int[0]));

        /// <summary>Renders whitespace: <c>"none"</c>, <c>"boundary"</c>, <c>"selection"</c>, <c>"trailing"</c> or <c>"all"</c>.</summary>
        public T RenderWhitespace(string mode) => Option(o => o.renderWhitespace = mode);

        /// <summary>Renders control characters as visible glyphs.</summary>
        public T RenderControlCharacters(bool enabled = true) => Option(o => o.renderControlCharacters = enabled);

        /// <summary>How the current line is highlighted: <c>"none"</c>, <c>"gutter"</c>, <c>"line"</c> or <c>"all"</c>.</summary>
        public T RenderLineHighlight(string mode) => Option(o => o.renderLineHighlight = mode);

        /// <summary>Highlights other occurrences of the symbol at the caret: <c>"off"</c>, <c>"singleFile"</c> or <c>"multiFile"</c>.</summary>
        public T OccurrencesHighlight(string mode) => Option(o => o.occurrencesHighlight = mode);

        /// <summary>Font size in pixels.</summary>
        public T FontSize(double px) => Option(o => o.fontSize = px);

        /// <summary>Font stack. Defaults to the package's monospace stack.</summary>
        public T FontFamily(string fontFamily) => Option(o => o.fontFamily = fontFamily);

        /// <summary>Line height in pixels, or 0 to derive it from the font size.</summary>
        public T LineHeight(double px) => Option(o => o.lineHeight = px);

        /// <summary>Extra letter spacing in pixels.</summary>
        public T LetterSpacing(double px) => Option(o => o.letterSpacing = px);

        /// <summary>Enables font ligatures.</summary>
        public T FontLigatures(bool enabled = true) => Option(o => o.fontLigatures = enabled);

        /// <summary>Cursor shape: <c>"line"</c>, <c>"block"</c>, <c>"underline"</c> and the thin variants.</summary>
        public T CursorStyle(string style) => Option(o => o.cursorStyle = style);

        /// <summary>Cursor animation: <c>"blink"</c>, <c>"smooth"</c>, <c>"phase"</c>, <c>"expand"</c> or <c>"solid"</c>.</summary>
        public T CursorBlinking(string style) => Option(o => o.cursorBlinking = style);

        /// <summary>Padding above and below the content, in pixels.</summary>
        public T Padding(double top, double bottom) => Option(o => o.padding = new PaddingOptions { top = top, bottom = bottom });

        /// <summary>Text shown when the document is empty.</summary>
        public T Placeholder(string text) => Option(o => o.placeholder = text);

        /// <summary>
        /// The message shown when the user types into a read-only editor. Markdown, so it can explain
        /// rather than just refuse.
        /// </summary>
        public T ReadOnlyMessage(string markdown) => Option(o => o.readOnlyMessage = new MarkdownString { value = markdown });

        /// <summary>
        /// Marks the DOM itself read-only, not just the editor's model. Stops the browser's own
        /// spellcheck and autofill from touching the text.
        /// </summary>
        public T DomReadOnly(bool enabled = true) => Option(o => o.domReadOnly = enabled);

        /// <summary>Makes URLs in the text clickable.</summary>
        public T Links(bool enabled = true) => Option(o => o.links = enabled);

        /// <summary>Lets Ctrl+wheel zoom the font size.</summary>
        public T MouseWheelZoom(bool enabled = true) => Option(o => o.mouseWheelZoom = enabled);

        /// <summary>Animates scrolling instead of jumping.</summary>
        public T SmoothScrolling(bool enabled = true) => Option(o => o.smoothScrolling = enabled);

        /// <summary>Highlights confusable and invisible Unicode characters.</summary>
        public T UnicodeHighlight(bool ambiguousCharacters = true, bool invisibleCharacters = true)
        {
            return Option(o => o.unicodeHighlight = new UnicodeHighlightOptions
            {
                ambiguousCharacters = ambiguousCharacters,
                invisibleCharacters = invisibleCharacters
            });
        }

        /// <summary>Allows scrolling past the last line.</summary>
        public T ScrollBeyondLastLine(bool enabled = true) => Option(o => o.scrollBeyondLastLine = enabled);

        /// <summary>
        /// Enables semantic highlighting - the colours a semantic-tokens provider supplies.
        ///
        /// Monaco's own default is <c>"configuredByTheme"</c>, and a standalone theme that does not
        /// declare it leaves this off, so a registered provider is asked for nothing and nothing is
        /// coloured. <c>OnSemanticTokens</c> turns it on for you; this is here for turning it off again,
        /// or on for a provider registered by other means.
        /// </summary>
        public T SemanticHighlighting(bool enabled = true) => SetRawOption("semanticHighlighting.enabled", enabled);

        /// <summary>Colours matching bracket pairs.</summary>
        public T BracketPairColorization(bool enabled = true) => Option(o => o.bracketPairColorization = new BracketPairColorizationOptions { enabled = enabled });

        /// <summary>Shows the editor's own context menu.</summary>
        public T ContextMenu(bool enabled = true) => Option(o => o.contextmenu = enabled);

        /// <summary>Opens the suggest widget as the user types.</summary>
        public T QuickSuggestions(bool enabled = true) => Option(o => o.quickSuggestions = enabled);

        /// <summary>How long to wait before opening the suggest widget, in milliseconds.</summary>
        public T QuickSuggestionsDelay(double ms) => Option(o => o.quickSuggestionsDelay = ms);

        /// <summary>Whether Enter accepts a suggestion: <c>"on"</c>, <c>"smart"</c> or <c>"off"</c>.</summary>
        public T AcceptSuggestionOnEnter(string mode) => Option(o => o.acceptSuggestionOnEnter = mode);

        /// <summary>Whether Tab completes: <c>"on"</c>, <c>"off"</c> or <c>"onlySnippets"</c>.</summary>
        public T TabCompletion(string mode) => Option(o => o.tabCompletion = mode);

        /// <summary>Screen-reader label for the editor.</summary>
        public T AriaLabel(string label) => Option(o => o.ariaLabel = label);

        /// <summary>Screen-reader support: <c>"auto"</c>, <c>"on"</c> or <c>"off"</c>.</summary>
        public T AccessibilitySupport(string mode) => Option(o => o.accessibilitySupport = mode);

        /// <summary>Overrides the theme for this editor.</summary>
        public T Theme(string theme) => Option(o => o.theme = theme);

        /// <summary>
        /// Lets Monaco watch its own container size instead of the component's <c>ResizeObserver</c>.
        /// Off by default: the component already drives <c>layout()</c>.
        /// </summary>
        public T AutomaticLayout(bool enabled = true) => Option(o => o.automaticLayout = enabled);

        #endregion
    }
}
