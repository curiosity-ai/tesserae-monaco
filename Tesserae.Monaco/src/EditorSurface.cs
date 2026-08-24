using System;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A live Monaco editor's full operation surface: model and text, view state, selections, revealing
    /// and scrolling, decorations, widgets and view zones, actions and commands, and the events.
    ///
    /// Every operation the components expose is implemented here exactly once, against the declared
    /// <see cref="IStandaloneCodeEditor"/>. <see cref="MonacoTextComponent{T}"/> is a fluent facade over
    /// it that also buffers calls made before the editor exists, and a diff editor's two sides are
    /// surfaces too - which is what lets one side be decorated or subscribed to without a second
    /// implementation of any of this.
    ///
    /// A surface is only valid while its editor is: it is created on mount and dropped on teardown. Hold
    /// it no longer than the component that handed it to you.
    /// </summary>
    public sealed class EditorSurface
    {
        private readonly IStandaloneCodeEditor _editor;
        private readonly DisposableBag         _disposables;

        internal EditorSurface(IStandaloneCodeEditor editor, DisposableBag disposables)
        {
            _editor      = editor;
            _disposables = disposables ?? new DisposableBag();
        }

        /// <summary>
        /// The underlying Monaco editor. Everything below is a thin layer over it - reach for this for an
        /// event subscription you want to release early, since its <c>on...</c> methods hand back the
        /// <see cref="IJsDisposable"/> that the fluent wrappers keep for the component's lifetime.
        /// </summary>
        public IStandaloneCodeEditor Native => _editor;

        #region Model and text

        /// <summary>The document this editor is showing, or null when it has none.</summary>
        public CodeModel Model => CodeModel.Wrap(_editor.getModel());

        /// <summary>
        /// Shows <paramref name="model"/>. The previous model is <b>not</b> disposed - Monaco leaves that
        /// to whoever created it, which is what makes switching between documents cheap.
        /// </summary>
        public EditorSurface SetModel(CodeModel model)
        {
            _editor.setModel(model?.Native);

            return this;
        }

        /// <summary>The content. Assigning resets the undo stack - see <see cref="ApplyEdits"/>.</summary>
        public string Text
        {
            get => _editor.getValue();
            set => _editor.setValue(value ?? "");
        }

        /// <summary>
        /// Applies edits through the editor, keeping the undo stack and moving the caret as a user edit
        /// would. <paramref name="source"/> shows up as the <c>source</c> on the resulting cursor event,
        /// which is how a host tells its own edits apart from the user's.
        /// </summary>
        public EditorSurface ApplyEdits(ReadOnlyArray<TextEdit> edits, string source = "tss-monaco")
        {
            if (edits is object && edits.Length > 0)
            {
                // Script.ToArray strips the $type marker Transpose stamps onto a C# array: Monaco posts
                // the edits to its editor worker to minimise them, and postMessage refuses a value
                // carrying a function.
                _editor.executeEdits(source, Script.ToArray(edits));
            }

            return this;
        }

        /// <summary>Closes the current undo step, so later edits undo separately.</summary>
        public EditorSurface PushUndoStop()
        {
            _editor.pushUndoStop();

            return this;
        }

        /// <summary>Undoes the last step.</summary>
        public EditorSurface Undo() => Trigger("undo");

        /// <summary>Redoes the last undone step.</summary>
        public EditorSurface Redo() => Trigger("redo");

        #endregion

        #region View state

        /// <summary>
        /// Captures the caret, selections, scroll position and folding state. Save it before showing a
        /// different model and restore it when coming back, or the user loses their place.
        /// </summary>
        public EditorViewState SaveViewState() => new EditorViewState(_editor.saveViewState());

        /// <summary>Restores state captured by <see cref="SaveViewState"/>. A null or empty state is ignored.</summary>
        public EditorSurface RestoreViewState(EditorViewState state)
        {
            if (state is object && state.HasValue) _editor.restoreViewState(state.Native);

            return this;
        }

        #endregion

        #region Selection and position

        /// <summary>The primary caret position.</summary>
        public Position GetPosition() => _editor.getPosition();

        /// <summary>Moves the primary caret.</summary>
        public EditorSurface SetPosition(Position position)
        {
            if (position is object) _editor.setPosition(position);

            return this;
        }

        /// <summary>The primary selection, direction included.</summary>
        public TextSelection GetSelection() => _editor.getSelection();

        /// <summary>Every selection, primary first - more than one when multi-cursor is in play.</summary>
        public TextSelection[] GetSelections() => _editor.getSelections() ?? new TextSelection[0];

        /// <summary>Selects a range, anchored at its start.</summary>
        public EditorSurface SetSelection(TextRange range)
        {
            if (range is object) _editor.setSelection(range);

            return this;
        }

        /// <summary>Selects with an explicit direction.</summary>
        public EditorSurface SetSelection(TextSelection selection)
        {
            if (selection is object) _editor.setSelection(selection);

            return this;
        }

        /// <summary>Replaces every selection - this is how multi-cursor is set programmatically.</summary>
        public EditorSurface SetSelections(ReadOnlyArray<TextSelection> selections)
        {
            if (selections is object && selections.Length > 0) _editor.setSelections(selections);

            return this;
        }

        /// <summary>The selected text, or an empty string when nothing is selected.</summary>
        public string GetSelectedText()
        {
            var range = Selections.ToRange(GetSelection());
            var model = _editor.getModel();

            if (range is null || model is null) return "";

            return model.getValueInRange(range) ?? "";
        }

        /// <summary>Selects the whole document.</summary>
        public EditorSurface SelectAll() => Trigger("editor.action.selectAll");

        #endregion

        #region Revealing and scrolling

        /// <summary>Scrolls a line into view, doing as little as possible.</summary>
        public EditorSurface RevealLine(int lineNumber)
        {
            _editor.revealLine(lineNumber);

            return this;
        }

        /// <summary>Scrolls a line to the middle of the viewport.</summary>
        public EditorSurface RevealLineInCenter(int lineNumber)
        {
            _editor.revealLineInCenter(lineNumber);

            return this;
        }

        /// <summary>
        /// Centres a line only if it is off-screen. The right choice for "go to next match": a line
        /// already visible should not jump.
        /// </summary>
        public EditorSurface RevealLineInCenterIfOutsideViewport(int lineNumber)
        {
            _editor.revealLineInCenterIfOutsideViewport(lineNumber);

            return this;
        }

        /// <summary>Scrolls a line near the top, leaving context below it.</summary>
        public EditorSurface RevealLineNearTop(int lineNumber)
        {
            _editor.revealLineNearTop(lineNumber);

            return this;
        }

        /// <summary>Scrolls a position into view, horizontally as well as vertically.</summary>
        public EditorSurface RevealPosition(Position position)
        {
            if (position is object) _editor.revealPosition(position);

            return this;
        }

        /// <summary>Scrolls a position to the middle of the viewport.</summary>
        public EditorSurface RevealPositionInCenter(Position position)
        {
            if (position is object) _editor.revealPositionInCenter(position);

            return this;
        }

        /// <summary>Scrolls a range into view.</summary>
        public EditorSurface RevealRange(TextRange range)
        {
            if (range is object) _editor.revealRange(range);

            return this;
        }

        /// <summary>Scrolls a range to the middle of the viewport.</summary>
        public EditorSurface RevealRangeInCenter(TextRange range)
        {
            if (range is object) _editor.revealRangeInCenter(range);

            return this;
        }

        /// <summary>Centres a range only if it is off-screen.</summary>
        public EditorSurface RevealRangeInCenterIfOutsideViewport(TextRange range)
        {
            if (range is object) _editor.revealRangeInCenterIfOutsideViewport(range);

            return this;
        }

        /// <summary>Scrolls a range to the top of the viewport.</summary>
        public EditorSurface RevealRangeAtTop(TextRange range)
        {
            if (range is object) _editor.revealRangeAtTop(range);

            return this;
        }

        /// <summary>The vertical scroll offset, in pixels.</summary>
        public double GetScrollTop() => _editor.getScrollTop();

        /// <summary>Scrolls vertically to an absolute offset.</summary>
        public EditorSurface SetScrollTop(double scrollTop)
        {
            _editor.setScrollTop(scrollTop);

            return this;
        }

        /// <summary>The horizontal scroll offset, in pixels.</summary>
        public double GetScrollLeft() => _editor.getScrollLeft();

        /// <summary>Scrolls horizontally to an absolute offset.</summary>
        public EditorSurface SetScrollLeft(double scrollLeft)
        {
            _editor.setScrollLeft(scrollLeft);

            return this;
        }

        /// <summary>The full scrollable height, in pixels.</summary>
        public double GetScrollHeight() => _editor.getScrollHeight();

        /// <summary>
        /// The height the content actually needs, in pixels - what to size a container to when the editor
        /// should not scroll. Pair with <see cref="OnContentSizeChanged"/>.
        /// </summary>
        public double GetContentHeight() => _editor.getContentHeight();

        /// <summary>The width the content actually needs, in pixels.</summary>
        public double GetContentWidth() => _editor.getContentWidth();

        #endregion

        #region Focus

        /// <summary>Gives the editor keyboard focus.</summary>
        public EditorSurface Focus()
        {
            _editor.focus();

            return this;
        }

        /// <summary>Whether the text area currently has focus.</summary>
        public bool HasTextFocus() => _editor.hasTextFocus();

        #endregion

        #region Decorations

        /// <summary>
        /// A decoration collection owned by this editor. Update it with
        /// <see cref="DecorationCollection.Set"/> rather than creating a new one per change: a collection
        /// keeps its ranges tracked across edits, and replacing it re-does work Monaco has already done.
        /// </summary>
        public DecorationCollection CreateDecorations(ReadOnlyArray<TextDecoration> initial = null)
        {
            return new DecorationCollection(_editor.createDecorationsCollection(initial));
        }

        #endregion

        #region Widgets and view zones

        /// <summary>Places a widget inside the text, anchored to a document position.</summary>
        public EditorSurface AddContentWidget(ContentWidget widget)
        {
            if (widget is object) _editor.addContentWidget(widget.Descriptor());

            return this;
        }

        /// <summary>Re-places a content widget after its <see cref="ContentWidget.Position"/> changed.</summary>
        public EditorSurface LayoutContentWidget(ContentWidget widget)
        {
            if (widget is object) _editor.layoutContentWidget(widget.Descriptor());

            return this;
        }

        /// <summary>Removes a content widget.</summary>
        public EditorSurface RemoveContentWidget(ContentWidget widget)
        {
            if (widget is object) _editor.removeContentWidget(widget.Descriptor());

            return this;
        }

        /// <summary>Pins a widget to a corner of the editor, outside the scrolling text.</summary>
        public EditorSurface AddOverlayWidget(OverlayWidget widget)
        {
            if (widget is object) _editor.addOverlayWidget(widget.Descriptor());

            return this;
        }

        /// <summary>Removes an overlay widget.</summary>
        public EditorSurface RemoveOverlayWidget(OverlayWidget widget)
        {
            if (widget is object) _editor.removeOverlayWidget(widget.Descriptor());

            return this;
        }

        /// <summary>
        /// Opens a band of space between two lines and renders the zone's element into it. The zone's
        /// <see cref="ViewZone.ZoneId"/> is filled in so it can be removed again.
        /// </summary>
        public EditorSurface AddViewZone(ViewZone zone)
        {
            if (zone is null) return this;

            var descriptor = zone.Descriptor();

            _editor.changeViewZones(accessor => zone.ZoneId = accessor.addZone(descriptor));

            return this;
        }

        /// <summary>Closes a zone previously opened by <see cref="AddViewZone"/>.</summary>
        public EditorSurface RemoveViewZone(ViewZone zone)
        {
            if (zone is null || zone.ZoneId is null) return this;

            var zoneId = zone.ZoneId;

            _editor.changeViewZones(accessor => accessor.removeZone(zoneId));

            zone.ZoneId = null;

            return this;
        }

        #endregion

        #region Actions, commands and context keys

        /// <summary>
        /// Adds an action: a labelled command with optional keybindings and a context-menu entry. Actions
        /// are per editor, unlike language providers, so no gating is needed.
        /// </summary>
        public EditorSurface AddAction(EditorActionDescriptor action)
        {
            if (action is null || string.IsNullOrWhiteSpace(action.Id) || action.Run is null) return this;

            _disposables.Add(_editor.addAction(action.ToMonaco()));

            return this;
        }

        /// <summary>
        /// Binds a keybinding to a handler without adding a context-menu entry. Build
        /// <paramref name="keybinding"/> from <see cref="KeyMod"/> and <see cref="KeyCode"/>, e.g.
        /// <c>KeyMod.With(KeyMod.CtrlCmd, KeyCode.KeyS)</c>.
        /// </summary>
        public EditorSurface AddCommand(int keybinding, Action handler, string context = null)
        {
            if (handler is object) _editor.addCommand(keybinding, handler, context);

            return this;
        }

        /// <summary>
        /// A named boolean this editor's keybindings and menu entries can be gated on, via an action's
        /// <see cref="EditorActionDescriptor.Precondition"/>.
        /// </summary>
        public IContextKey CreateContextKey(string key, bool defaultValue = false)
        {
            return _editor.createContextKey(key, defaultValue);
        }

        /// <summary>
        /// Runs one of Monaco's built-in commands by id - <c>"editor.action.formatDocument"</c>,
        /// <c>"actions.find"</c>, <c>"editor.action.commentLine"</c>,
        /// <c>"editor.action.revealDefinition"</c>, and the rest.
        ///
        /// This is also the reliable way to exercise a formatter: Monaco takes VS Code's per-platform
        /// keybindings, so Format Document is Shift+Alt+F on Windows, Shift+Option+F on macOS and
        /// Ctrl+Shift+I on Linux - and a host that hardcodes one of those looks broken on the others.
        ///
        /// Prefer this over <see cref="RunAction"/> for anything navigational. The two reach different
        /// registries: <c>getAction</c> only knows the editor's own actions, while <c>trigger</c> also
        /// reaches commands bound by keybinding rule - which is what go-to-definition and
        /// find-all-references are. <c>RunAction("editor.action.revealDefinition")</c> reports "no such
        /// action" while <c>Trigger</c> on the same id works.
        /// </summary>
        public EditorSurface Trigger(string actionId, object payload = null, string source = "tss-monaco")
        {
            if (!string.IsNullOrWhiteSpace(actionId)) _editor.trigger(source, actionId, payload);

            return this;
        }

        /// <summary>
        /// Runs a built-in <b>editor action</b> and reports whether it existed and was enabled -
        /// <see cref="Trigger"/> is silent about an id that matched nothing, which is easy to misread as a
        /// broken provider.
        ///
        /// Only editor actions are visible here, not commands bound by keybinding rule: this returns false
        /// for <c>editor.action.revealDefinition</c> even though <see cref="Trigger"/> runs it.
        /// </summary>
        public bool RunAction(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return false;

            var action = _editor.getAction(actionId);

            if (action is null) return false;

            // Monaco logs an unhandled rejection if an action's promise faults and nobody is listening.
            action.run()?.Then(null, new Action<object>(error => console.error("Tesserae.Monaco: the action '" + actionId + "' failed", error)), null);

            return true;
        }

        /// <summary>Whether Monaco has an action with this id, and it is currently enabled.</summary>
        public bool IsActionSupported(string actionId)
        {
            var action = string.IsNullOrWhiteSpace(actionId) ? null : _editor.getAction(actionId);

            return action is object && action.isSupported();
        }

        /// <summary>
        /// Closes the transient message Monaco shows over the caret - the little bubble that says
        /// "No definition found for 'x'".
        ///
        /// The case that wants this is a definition provider that <i>did</i> resolve the symbol but
        /// answered with no in-editor location, because it opened the documentation somewhere of its
        /// own instead: Monaco shows the message whenever a provider yields nothing, and there it is
        /// simply wrong. Call it after the provider's promise has settled - Monaco shows the message
        /// on the turn after that, so a <c>setTimeout(0)</c> is the earliest it can be closed.
        ///
        /// Everything below the contribution id is Monaco-internal and has been renamed between
        /// releases, so a missing contribution is a no-op rather than a throw. The cast is direct
        /// rather than an <c>as</c>: an <c>[External]</c> interface has no emitted metadata, so a
        /// runtime type test throws instead of answering false, while a direct cast emits nothing at
        /// all and leaves a missing contribution as the null it already is.
        /// </summary>
        public EditorSurface CloseMessage()
        {
            var controller = (IMessageController)_editor.getContribution(MESSAGE_CONTROLLER_ID);

            controller?.closeMessage();

            return this;
        }

        private const string MESSAGE_CONTROLLER_ID = "editor.contrib.messageController";

        /// <summary>
        /// Opens the documentation pane of the suggest widget and leaves it open, instead of the user
        /// having to press Ctrl+Space a second time for it.
        ///
        /// Worth doing when the completions carry real documentation - an API list where the detail is
        /// the point - and not otherwise, since it makes the widget considerably taller. Call it once
        /// the editor exists; the widget is created with it.
        /// </summary>
        public EditorSurface ShowSuggestDetails(bool visible = true)
        {
            var controller = (ISuggestController)_editor.getContribution(SUGGEST_CONTROLLER_ID);
            var widget     = controller?.widget?.value;

            widget?._setDetailsVisible(visible);

            return this;
        }

        private const string SUGGEST_CONTROLLER_ID = "editor.contrib.suggestController";

        #endregion

        #region Options

        /// <summary>Applies an options object, leaving anything it does not mention untouched.</summary>
        public EditorSurface UpdateOptions(EditorOptions options)
        {
            if (options is object) _editor.updateOptions(options);

            return this;
        }

        /// <summary>
        /// Sets one option that <see cref="EditorOptions"/> does not declare. The typed setters on the
        /// components cover what a host normally wants; this is the escape hatch for the rest of Monaco's
        /// several hundred options, and the one place a JavaScript name is still a string.
        /// </summary>
        public EditorSurface SetRawOption(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) return this;

            var options = new EditorOptions();

            Script.Set(options, name, value);

            return UpdateOptions(options);
        }

        /// <summary>
        /// Reads a numeric option by its <c>MonacoApi.editor.EditorOption</c> id, e.g.
        /// <c>MonacoApi.editor.EditorOption.lineHeight</c>.
        /// </summary>
        public double GetNumberOption(int optionId) => _editor.getNumberOption(optionId);

        /// <summary>Re-measures the editor against its container.</summary>
        public EditorSurface Layout()
        {
            _editor.layout();

            return this;
        }

        #endregion

        #region Events

        // Every subscription's handle goes into the component's bag and is released on teardown: Monaco
        // hands one back from each `on...` call, and disposing the editor does not release the ones a
        // host added. Native is there for a subscription that has to end sooner.
        private EditorSurface Keep(IJsDisposable registration)
        {
            _disposables.Add(registration);

            return this;
        }

        /// <summary>The text area gained focus.</summary>
        public EditorSurface OnFocus(Action handler) => handler is null ? this : Keep(_editor.onDidFocusEditorText(handler));

        /// <summary>
        /// The text area lost focus - the hook for "save on blur", and the reason a host should not have
        /// to poll.
        /// </summary>
        public EditorSurface OnBlur(Action handler) => handler is null ? this : Keep(_editor.onDidBlurEditorText(handler));

        /// <summary>The editor or any of its widgets gained focus.</summary>
        public EditorSurface OnWidgetFocus(Action handler) => handler is null ? this : Keep(_editor.onDidFocusEditorWidget(handler));

        /// <summary>The editor and all of its widgets lost focus.</summary>
        public EditorSurface OnWidgetBlur(Action handler) => handler is null ? this : Keep(_editor.onDidBlurEditorWidget(handler));

        /// <summary>A key went down. The event carries Monaco's <see cref="KeyCode"/>, not the browser's.</summary>
        public EditorSurface OnKeyDown(Action<IKeyboardEvent> handler) => handler is null ? this : Keep(_editor.onKeyDown(handler));

        /// <summary>A key came up.</summary>
        public EditorSurface OnKeyUp(Action<IKeyboardEvent> handler) => handler is null ? this : Keep(_editor.onKeyUp(handler));

        /// <summary>A mouse button went down somewhere in the editor.</summary>
        public EditorSurface OnMouseDown(Action<IEditorMouseEvent> handler) => handler is null ? this : Keep(_editor.onMouseDown(handler));

        /// <summary>A mouse button came up.</summary>
        public EditorSurface OnMouseUp(Action<IEditorMouseEvent> handler) => handler is null ? this : Keep(_editor.onMouseUp(handler));

        /// <summary>The pointer moved over the editor.</summary>
        public EditorSurface OnMouseMove(Action<IEditorMouseEvent> handler) => handler is null ? this : Keep(_editor.onMouseMove(handler));

        /// <summary>The pointer left the editor.</summary>
        public EditorSurface OnMouseLeave(Action<IEditorMouseEvent> handler) => handler is null ? this : Keep(_editor.onMouseLeave(handler));

        /// <summary>The context menu was requested. Read the target to know what was right-clicked.</summary>
        public EditorSurface OnContextMenu(Action<IEditorMouseEvent> handler) => handler is null ? this : Keep(_editor.onContextMenu(handler));

        /// <summary>Text was pasted, with the range it landed in.</summary>
        public EditorSurface OnPaste(Action<IPasteEvent> handler) => handler is null ? this : Keep(_editor.onDidPaste(handler));

        /// <summary>The editor scrolled.</summary>
        public EditorSurface OnScrollChanged(Action<IScrollEvent> handler) => handler is null ? this : Keep(_editor.onDidScrollChange(handler));

        /// <summary>The caret moved.</summary>
        public EditorSurface OnCursorPositionChanged(Action<ICursorPositionChangedEvent> handler) => handler is null ? this : Keep(_editor.onDidChangeCursorPosition(handler));

        /// <summary>The selection changed.</summary>
        public EditorSurface OnCursorSelectionChanged(Action<ICursorSelectionChangedEvent> handler) => handler is null ? this : Keep(_editor.onDidChangeCursorSelection(handler));

        /// <summary>The content changed, with what changed and the new version id.</summary>
        public EditorSurface OnContentChanged(Action<IContentChangedEvent> handler) => handler is null ? this : Keep(_editor.onDidChangeModelContentDetailed(handler));

        /// <summary>The editor was pointed at a different model.</summary>
        public EditorSurface OnModelChanged(Action handler) => handler is null ? this : Keep(_editor.onDidChangeModel(handler));

        /// <summary>The model's language changed.</summary>
        public EditorSurface OnLanguageChanged(Action<ILanguageChangedEvent> handler) => handler is null ? this : Keep(_editor.onDidChangeModelLanguage(handler));

        /// <summary>An option changed, whether from <c>updateOptions</c> or a built-in action.</summary>
        public EditorSurface OnConfigurationChanged(Action handler) => handler is null ? this : Keep(_editor.onDidChangeConfiguration(handler));

        /// <summary>The editor re-laid out.</summary>
        public EditorSurface OnLayoutChanged(Action handler) => handler is null ? this : Keep(_editor.onDidLayoutChange(handler));

        /// <summary>
        /// The content's own size changed. The right signal for growing a container to fit, and cheaper
        /// than watching decorations.
        /// </summary>
        public EditorSurface OnContentSizeChanged(Action<IContentSizeChangedEvent> handler) => handler is null ? this : Keep(_editor.onDidContentSizeChange(handler));

        /// <summary>
        /// The user tried to type into a read-only editor - the hook for explaining why they can't, rather
        /// than letting the keystroke vanish.
        /// </summary>
        public EditorSurface OnAttemptReadOnlyEdit(Action handler) => handler is null ? this : Keep(_editor.onDidAttemptReadOnlyEdit(handler));

        /// <summary>The editor was disposed.</summary>
        public EditorSurface OnDisposed(Action handler) => handler is null ? this : Keep(_editor.onDidDispose(handler));

        #endregion
    }

    /// <summary>
    /// A set of decorations Monaco keeps tracked across edits. Prefer updating one collection over
    /// creating a new one per change - the ranges move with the text, and Monaco only re-renders the
    /// difference.
    /// </summary>
    public sealed class DecorationCollection
    {
        private IEditorDecorationsCollection _native;

        internal DecorationCollection(IEditorDecorationsCollection native)
        {
            _native = native;
        }

        /// <summary>The underlying Monaco collection.</summary>
        public IEditorDecorationsCollection Native => _native;

        /// <summary>How many decorations the collection holds.</summary>
        public int Count => _native is null ? 0 : _native.length;

        /// <summary>Replaces every decoration in the collection.</summary>
        public DecorationCollection Set(ReadOnlyArray<TextDecoration> decorations)
        {
            _native?.set(decorations ?? new TextDecoration[0]);

            return this;
        }

        /// <summary>Adds decorations, keeping the ones already there.</summary>
        public DecorationCollection Append(ReadOnlyArray<TextDecoration> decorations)
        {
            if (_native is object && decorations is object && decorations.Length > 0) _native.append(decorations);

            return this;
        }

        /// <summary>Removes every decoration.</summary>
        public DecorationCollection Clear()
        {
            _native?.clear();

            return this;
        }

        /// <summary>
        /// Where the decorations are <b>now</b> - after the edits since they were set, since Monaco moves
        /// tracked ranges with the text.
        /// </summary>
        public TextRange[] GetRanges() => _native is null ? new TextRange[0] : _native.getRanges();

        /// <summary>One decoration's current range, or null when the index is out of bounds.</summary>
        public TextRange GetRange(int index) => _native?.getRange(index);
    }

    /// <summary>
    /// A labelled command on one editor: a keybinding, a context-menu entry, or both. Added with
    /// <see cref="EditorSurface.AddAction"/>.
    ///
    /// Distinct from <see cref="EditorAction"/>, which is the raw Monaco descriptor: this one hands the
    /// callback an <see cref="EditorSurface"/> so an action body has the same API as everything else.
    /// </summary>
    public sealed class EditorActionDescriptor
    {
        /// <summary>Unique id, e.g. <c>"myapp.saveDocument"</c>.</summary>
        public string Id { get; set; }

        /// <summary>The text shown in the context menu and the command palette.</summary>
        public string Label { get; set; }

        /// <summary>What to run. Receives the editor's surface.</summary>
        public Action<EditorSurface> Run { get; set; }

        /// <summary>
        /// Keybindings, built from <see cref="KeyMod"/> and <see cref="KeyCode"/>. Optional - an action
        /// with none is still reachable from the context menu and by <see cref="EditorSurface.Trigger"/>.
        /// </summary>
        public int[] Keybindings { get; set; }

        /// <summary>
        /// Which context-menu group to appear in - <c>"navigation"</c>, <c>"1_modification"</c>,
        /// <c>"9_cutcopypaste"</c>, <c>"view"</c>. Null keeps the action out of the menu.
        /// </summary>
        public string ContextMenuGroupId { get; set; }

        /// <summary>Sort order within the group.</summary>
        public double ContextMenuOrder { get; set; }

        /// <summary>A context-key expression gating the action - e.g. a key this editor created.</summary>
        public string Precondition { get; set; }

        public EditorActionDescriptor() { }

        public EditorActionDescriptor(string id, string label, Action<EditorSurface> run)
        {
            Id    = id;
            Label = label;
            Run   = run;
        }

        internal EditorAction ToMonaco()
        {
            return new EditorAction
            {
                id                 = Id,
                label              = Label ?? Id,
                keybindings        = Keybindings,
                contextMenuGroupId = ContextMenuGroupId,
                contextMenuOrder   = ContextMenuOrder,
                precondition       = Precondition,

                // A fresh surface is fine: it holds no state of its own beyond the editor and the bag.
                run = editor => Run(new EditorSurface(editor, null))
            };
        }
    }
}
