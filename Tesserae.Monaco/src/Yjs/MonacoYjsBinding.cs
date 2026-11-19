using System;
using System.Collections.Generic;
using Tesserae;
using Transpose;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Binds a shared <see cref="Y.Text"/> to a Monaco text model and the editors showing it - the C# counterpart of
    /// <see href="https://github.com/yjs/y-monaco">y-monaco</see>, written against this package's declarations of Monaco
    /// rather than bundling its JavaScript, and fixing what that library leaves open.
    /// </summary>
    /// <remarks>
    /// <para><b>Content.</b> The model takes the shared text's content when it is attached. A local edit is written
    /// into the shared text in one transaction whose origin is the binding, its changes applied right to left so each
    /// one's offsets are still those Monaco reported; a remote change is applied to the model with
    /// <c>applyEdits</c>, so it never lands on this user's undo stack, and each editor's selections are carried
    /// through it as Yjs relative positions.</para>
    /// <para><b>Line endings.</b> Monaco normalises every line break of a model to one sequence, so a shared text
    /// holding <c>\r\n</c> (written by a client whose model is CRLF) is one character longer per line than the model
    /// showing it. The binding sets the model to LF and, while the shared text contains a <c>\r</c>, maps offsets
    /// through a copy of it (a <c>\r\n</c> pair is one model character), so mixed line endings stay consistent on
    /// both sides instead of drifting.</para>
    /// <para><b>Presence.</b> With an <see cref="Awareness"/>, the focused editor's selection is published as the
    /// awareness field <c>selection</c> = <c>{ anchor, head }</c> of relative positions, the shape y-monaco
    /// writes and reads, so JavaScript y-monaco clients and this binding see each other's cursors. Remote selections
    /// are drawn as decorations in the collaborator's colour (the awareness <c>user.color</c> /
    /// <c>user.colorLight</c>, or <see cref="YjsUserColors"/>), with a caret that shows the collaborator's name on
    /// hover and for a moment after it moves.</para>
    /// <para><b>Undo.</b> With <c>undo</c> on (the default), a <see cref="Y.UndoManager"/> that tracks only this
    /// binding's transactions takes over Ctrl+Z, Ctrl+Shift+Z and Ctrl+Y in the bound editors, so undo reverts this
    /// user's edits and never a collaborator's, and puts the caret back where the undone change started.</para>
    /// <para>Most code reaches this through <c>CodeEditor.BindYjs(...)</c>, which also re-attaches it when the editor
    /// is re-created on a remount. A host managing its own editors uses <see cref="Attach"/>,
    /// <see cref="AddEditor"/> and <see cref="RemoveEditor"/>.</para>
    /// </remarks>
    public sealed class MonacoYjsBinding : IDisposable
    {
        /// <summary>The awareness field the local selection is published in - y-monaco's name for it.</summary>
        public const string AwarenessSelectionField = "selection";

        // How long a collaborator's name stays up after their caret moved.
        private const int RecentMilliseconds = 1600;

        private static int _nextId;

        private readonly Y.Text         _text;
        private readonly Y.Doc          _doc;
        private readonly Awareness      _awareness;
        private readonly Y.UndoManager  _undo;
        private readonly string         _metaKey;
        private readonly List<BoundEditor> _editors = new List<BoundEditor>();
        private readonly DisposableBag  _modelListeners = new DisposableBag();
        private readonly Dictionary<int, double> _recent = new Dictionary<int, double>();
        private readonly HashSet<int>   _styled = new HashSet<int>();

        private readonly Action<Y.TextEvent, Y.Transaction>    _onTextChanged;
        private readonly Action<Y.Doc>                         _onBeforeAllTransactions;
        private readonly Action<AwarenessChanges, object>      _onAwarenessChange;
        private readonly Action<Y.UndoStackEvent, Y.UndoManager> _onStackItemAdded;
        private readonly Action<Y.UndoStackEvent, Y.UndoManager> _onStackItemPopped;

        private ITextModel        _model;
        private string            _shadow;
        private bool              _applyingLocal;
        private bool              _applyingRemote;
        private bool              _disposed;
        private string            _published;
        private RelativeSelection _lastSelection;
        private double            _recentTimer;

        internal Action OnDisposed;

        /// <summary>
        /// Binds <paramref name="text"/>. Nothing is shown until a model is attached - <see cref="Attach"/>,
        /// <see cref="AddEditor"/>, or <c>CodeEditor.BindYjs</c>, which does both.
        /// </summary>
        /// <param name="text">The shared text. It must be in a document, and Yjs must be loaded (<see cref="Yjs.LoadAsync"/>).</param>
        /// <param name="awareness">Presence for remote cursors and the local selection, or null for none.</param>
        /// <param name="undo">Whether undo and redo go through a <see cref="Y.UndoManager"/> that only reverts this binding's edits.</param>
        public MonacoYjsBinding(Y.Text text, Awareness awareness = null, bool undo = true)
        {
            if (!Yjs.IsLoaded) throw new InvalidOperationException("Yjs is not loaded yet: await Yjs.LoadAsync() first.");
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.doc == null) throw new ArgumentException("The text has to be part of a Y.Doc.", nameof(text));

            _text      = text;
            _doc       = text.doc;
            _awareness = awareness;
            _metaKey   = "tssm-yjs-binding-" + (++_nextId);

            _onTextChanged           = (e, transaction) => OnTextChanged(e, transaction);
            _onBeforeAllTransactions = d => SaveSelections();
            _onAwarenessChange       = (changes, origin) => OnAwarenessChange(changes);

            _text.observe(_onTextChanged);
            _doc.onBeforeAllTransactions(_onBeforeAllTransactions);

            if (_awareness != null) _awareness.onChange(_onAwarenessChange);

            if (undo)
            {
                _undo = new Y.UndoManager(_text);
                _undo.addTrackedOrigin(this);
                _undo.removeTrackedOrigin(null);

                _onStackItemAdded  = (e, manager) => OnStackItemAdded(e);
                _onStackItemPopped = (e, manager) => OnStackItemPopped(e);
                _undo.onStackItemAdded(_onStackItemAdded);
                _undo.onStackItemPopped(_onStackItemPopped);
            }
        }

        /// <summary>The shared text.</summary>
        public Y.Text Text => _text;

        /// <summary>The presence the cursors come from, or null.</summary>
        public Awareness Awareness => _awareness;

        /// <summary>The undo manager behind Ctrl+Z, or null when the binding was created without one.</summary>
        public Y.UndoManager UndoManager => _undo;

        /// <summary>The attached model, or null.</summary>
        public ITextModel Model => _model;

        /// <summary>How many editors the binding manages.</summary>
        public int EditorCount => _editors.Count;

        /// <summary>Whether the binding has been disposed.</summary>
        public bool IsDisposed => _disposed;

        /// <summary>Undoes this user's last change. Does nothing without an undo manager.</summary>
        public void Undo()
        {
            if (_undo != null && !_disposed) _undo.undo();
        }

        /// <summary>Redoes this user's last undone change. Does nothing without an undo manager.</summary>
        public void Redo()
        {
            if (_undo != null && !_disposed) _undo.redo();
        }

        #region Model and editors

        /// <summary>
        /// Attaches <paramref name="model"/>: sets it to LF line endings and gives it the shared text's content
        /// (with <c>setValue</c>, only when it differs). A model attached before is detached first, with its editors.
        /// </summary>
        public MonacoYjsBinding Attach(ITextModel model)
        {
            ThrowIfDisposed();

            if (model == null) throw new ArgumentNullException(nameof(model));
            if (_model == model) return this;

            DetachModel();

            _model = model;

            var content = _text.asString();
            _shadow = content.IndexOf('\r') >= 0 ? content : null;

            _applyingRemote = true;

            try
            {
                // setValue detects the line ending from the content, so LF is set after it.
                if (_model.getValue() != Normalize(content)) _model.setValue(content);
                EnsureLineFeeds();
            }
            finally
            {
                _applyingRemote = false;
            }

            _modelListeners.Add(_model.onDidChangeContent(OnModelChanged));
            _modelListeners.Add(_model.onWillDispose(DetachModel));

            return this;
        }

        /// <summary>
        /// Adds an editor showing the attached model (attaching the editor's model when none is): its selection is
        /// published and carried through remote changes, remote cursors are drawn in it, and - with undo on - its
        /// Ctrl+Z goes to the binding.
        /// </summary>
        public MonacoYjsBinding AddEditor(IStandaloneCodeEditor editor)
        {
            ThrowIfDisposed();

            if (editor == null) throw new ArgumentNullException(nameof(editor));

            foreach (var existing in _editors)
            {
                if (existing.Editor == editor) return this;
            }

            var model = editor.getModel();

            if (_model == null) Attach(model);
            else if (model != _model) throw new ArgumentException("The editor shows another model than the one attached to the binding.", nameof(editor));

            var bound = new BoundEditor(editor);
            bound.Decorations = editor.createDecorationsCollection(new TextDecoration[0]);
            bound.Listeners.Add(editor.onDidChangeCursorSelection(_ => OnLocalSelection(bound)));
            bound.Listeners.Add(editor.onDidFocusEditorText(() => OnLocalSelection(bound)));

            if (_undo != null)
            {
                // Registered through a surface of its own whose bag is never released: Monaco cannot remove a command
                // rule, and one that did nothing after the binding was disposed would leave the editor without undo.
                // The handlers fall back to Monaco's own undo once this editor is no longer bound.
                var surface = new EditorSurface(editor, new DisposableBag());
                surface.AddCommand(KeyMod.With(KeyMod.CtrlCmd, KeyCode.KeyZ),                     () => UndoOrDefault(editor, "undo"));
                surface.AddCommand(KeyMod.With(KeyMod.CtrlCmd | KeyMod.Shift, KeyCode.KeyZ),      () => UndoOrDefault(editor, "redo"));
                surface.AddCommand(KeyMod.With(KeyMod.CtrlCmd, KeyCode.KeyY),                     () => UndoOrDefault(editor, "redo"));
            }

            _editors.Add(bound);
            RenderCursors();

            return this;
        }

        /// <summary>Stops managing <paramref name="editor"/> and clears the remote cursors drawn in it.</summary>
        public void RemoveEditor(IStandaloneCodeEditor editor)
        {
            for (var i = 0; i < _editors.Count; i++)
            {
                if (_editors[i].Editor != editor) continue;

                _editors[i].Release();
                _editors.RemoveAt(i);
                return;
            }
        }

        /// <summary>Detaches the model and every editor. The shared text keeps going; <see cref="Attach"/> picks it up again.</summary>
        public void DetachModel()
        {
            foreach (var bound in _editors) bound.Release();

            _editors.Clear();
            _modelListeners.DisposeAll();
            _model  = null;
            _shadow = null;
        }

        /// <summary>Attaches the editor behind <paramref name="surface"/> and its model. Used by the components on mount.</summary>
        internal void AttachSurface(EditorSurface surface)
        {
            if (_disposed || surface == null) return;

            Attach(surface.Native.getModel());
            AddEditor(surface.Native);
        }

        /// <summary>Detaches the editor behind <paramref name="surface"/>, and the model with the last editor. Used by the components before teardown.</summary>
        internal void DetachSurface(EditorSurface surface)
        {
            if (_disposed || surface == null) return;

            RemoveEditor(surface.Native);

            if (_editors.Count == 0) DetachModel();
        }

        #endregion

        #region Local changes

        private void OnModelChanged(IContentChangedEvent e)
        {
            if (_applyingRemote || _disposed || _model == null) return;

            var changes = e.changes;
            if (changes.Length > 0) WriteLocalChanges(changes);

            // A host that replaces the content (setValue detects the line ending from it) or switches the model to CRLF
            // changes the line ending under the binding, whose offsets count a line break as one model character. The model
            // goes back to LF; its lines do not change, and neither does the shared text.
            _applyingRemote = true;

            try
            {
                EnsureLineFeeds();
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private void WriteLocalChanges(IModelContentChange[] changes)
        {
            // An LF model never reports a \r; text with one comes from a host replacing the content of the model. From then
            // on the mapping needs the copy, which is the shared text as it is before this change.
            if (_shadow == null)
            {
                foreach (var change in changes)
                {
                    if (change.text.IndexOf('\r') < 0) continue;

                    _shadow = _text.asString();
                    break;
                }
            }

            // Monaco reports every change of one event against the document as it was before the event, so applying them
            // from the end backwards keeps each one's offsets valid. Sorted as indexes: a BCL collection over an
            // [External] type cannot be constructed.
            var order = new List<int>();
            for (var i = 0; i < changes.Length; i++) order.Add(i);
            order.Sort((a, b) => changes[b].rangeOffset - changes[a].rangeOffset);

            _applyingLocal = true;

            try
            {
                _doc.transact(_ =>
                {
                    foreach (var i in order)
                    {
                        var change = changes[i];
                        var start  = ModelToY(change.rangeOffset);
                        var end    = ModelToY(change.rangeOffset + change.rangeLength);

                        if (end > start) _text.@delete(start, end - start);
                        if (change.text.Length > 0) _text.insert(start, change.text, null);

                        if (_shadow != null) _shadow = _shadow.Substring(0, start) + change.text + _shadow.Substring(end);
                    }
                }, this);
            }
            finally
            {
                _applyingLocal = false;
            }
        }

        private void OnLocalSelection(BoundEditor bound)
        {
            if (_applyingRemote || _applyingLocal || _disposed || _model == null) return;
            if (!bound.Editor.hasTextFocus()) return;

            var selection = bound.Editor.getSelection();
            if (selection == null) return;

            var anchor = Y.createRelativePositionFromTypeIndex(_text, ModelToY(OffsetOf(selection.selectionStartLineNumber, selection.selectionStartColumn)), 0);
            var head   = Y.createRelativePositionFromTypeIndex(_text, ModelToY(OffsetOf(selection.positionLineNumber, selection.positionColumn)), 0);

            _lastSelection = new RelativeSelection(new[] { anchor }, new[] { head }, bound.Editor);

            if (_awareness == null) return;

            // The relative positions themselves, as y-monaco publishes them: JSON-encoded on the wire they keep their null
            // fields ({ type: null, tname, item: null, assoc }), which y-monaco hands to createAbsolutePositionFromRelativePosition
            // as they are. relativePositionToJSON drops the nulls, and that function then reads an absent item as an ID.
            var state = new YjsSelectionState { anchor = anchor, head = head };
            var key   = Transpose.Core.es5.JSON.stringify(state);

            if (key == _published) return;

            _published = key;
            _awareness.setLocalStateField(AwarenessSelectionField, state);
        }

        #endregion

        #region Remote changes

        private void SaveSelections()
        {
            if (_applyingLocal || _disposed || _model == null) return;

            foreach (var bound in _editors)
            {
                var selections = bound.Editor.getSelections();

                if (selections == null)
                {
                    bound.Saved = null;
                    continue;
                }

                var anchors = new Y.RelativePosition[selections.Length];
                var heads   = new Y.RelativePosition[selections.Length];

                for (var i = 0; i < selections.Length; i++)
                {
                    var s = selections[i];
                    anchors[i] = Y.createRelativePositionFromTypeIndex(_text, ModelToY(OffsetOf(s.selectionStartLineNumber, s.selectionStartColumn)), 0);
                    heads[i]   = Y.createRelativePositionFromTypeIndex(_text, ModelToY(OffsetOf(s.positionLineNumber, s.positionColumn)), 0);
                }

                bound.Saved = new RelativeSelection(anchors, heads, bound.Editor);
            }
        }

        private void OnTextChanged(Y.TextEvent e, Y.Transaction transaction)
        {
            if (transaction.origin == (object)this || _disposed) return;

            if (_model == null)
            {
                _shadow = null;
                return;
            }

            var delta = e.delta;
            var index = 0;

            _applyingRemote = true;

            try
            {
                for (var i = 0; i < delta.Length; i++)
                {
                    var op = delta[i];

                    if (op.retain.HasValue)
                    {
                        index += op.retain.Value;
                    }
                    else if (op.insert is string inserted)
                    {
                        // Until now the shared text had no \r, so it is exactly what the model shows at this point.
                        if (_shadow == null && inserted.IndexOf('\r') >= 0) _shadow = _model.getValue();

                        var at = _model.getPositionAt(YToModel(index));
                        _model.applyEdits(new[] { new TextEdit { range = Span(at, at), text = inserted } });

                        if (_shadow != null) _shadow = _shadow.Substring(0, index) + inserted + _shadow.Substring(index);

                        index += inserted.Length;
                    }
                    else if (op.@delete.HasValue)
                    {
                        var count = op.@delete.Value;
                        var from  = _model.getPositionAt(YToModel(index));
                        var to    = _model.getPositionAt(YToModel(index + count));

                        _model.applyEdits(new[] { new TextEdit { range = Span(from, to), text = "" } });

                        if (_shadow != null) _shadow = _shadow.Substring(0, index) + _shadow.Substring(index + count);
                    }
                    else if (op.insert != null)
                    {
                        throw new InvalidOperationException("The shared text holds an embed, which a code editor cannot show.");
                    }
                }

                foreach (var bound in _editors)
                {
                    if (bound.Saved != null) Restore(bound.Saved);
                }
            }
            finally
            {
                _applyingRemote = false;
            }

            RenderCursors();
        }

        private void Restore(RelativeSelection saved)
        {
            var selections = new List<TextSelection>();

            for (var i = 0; i < saved.Anchors.Length; i++)
            {
                var anchor = Y.createAbsolutePositionFromRelativePosition(saved.Anchors[i], _doc);
                var head   = Y.createAbsolutePositionFromRelativePosition(saved.Heads[i],   _doc);

                if (anchor == null || head == null || anchor.type != (Y.AbstractType)_text || head.type != (Y.AbstractType)_text) continue;

                var a = _model.getPositionAt(YToModel(anchor.index));
                var h = _model.getPositionAt(YToModel(head.index));

                selections.Add(new TextSelection
                {
                    selectionStartLineNumber = a.lineNumber,
                    selectionStartColumn     = a.column,
                    positionLineNumber       = h.lineNumber,
                    positionColumn           = h.column
                });
            }

            if (selections.Count > 0 && saved.Editor.getModel() == _model) saved.Editor.setSelections(selections.ToArray());
        }

        #endregion

        #region Undo

        private void UndoOrDefault(IStandaloneCodeEditor editor, string command)
        {
            var bound = !_disposed && _undo != null && IsBound(editor);

            if (!bound)
            {
                editor.trigger("keyboard", command, null);
                return;
            }

            if (command == "undo") _undo.undo();
            else _undo.redo();
        }

        private void OnStackItemAdded(Y.UndoStackEvent e)
        {
            // The selection from before the change: the caret moves after Monaco reported the edit, and the step is added
            // inside the transaction that wrote it.
            if (_lastSelection != null && !e.stackItem.meta.has(_metaKey)) e.stackItem.meta.set(_metaKey, _lastSelection);
        }

        private void OnStackItemPopped(Y.UndoStackEvent e)
        {
            if (_model == null || !e.stackItem.meta.has(_metaKey)) return;

            var saved = (RelativeSelection)e.stackItem.meta.get(_metaKey);

            if (IsBound(saved.Editor))
            {
                Restore(saved);
            }
            else if (_editors.Count > 0)
            {
                Restore(new RelativeSelection(saved.Anchors, saved.Heads, _editors[0].Editor));
            }
        }

        private bool IsBound(IStandaloneCodeEditor editor)
        {
            foreach (var bound in _editors)
            {
                if (bound.Editor == editor) return true;
            }

            return false;
        }

        #endregion

        #region Remote cursors

        private void OnAwarenessChange(AwarenessChanges changes)
        {
            if (_disposed) return;

            var now = DateTime.UtcNow.Ticks / 10000.0;

            for (var i = 0; i < changes.added.Length; i++)   _recent[changes.added[i]]   = now;
            for (var i = 0; i < changes.updated.Length; i++) _recent[changes.updated[i]] = now;

            RenderCursors();

            if (_recentTimer != 0) window.clearTimeout(_recentTimer);
            _recentTimer = window.setTimeout(_ =>
            {
                _recentTimer = 0;
                RenderCursors();
            }, RecentMilliseconds + 50);
        }

        private void RenderCursors()
        {
            if (_disposed || _awareness == null || _model == null || _editors.Count == 0) return;

            var decorations = new List<TextDecoration>();
            var shown       = new HashSet<int>();
            var now         = DateTime.UtcNow.Ticks / 10000.0;

            _awareness.getStates().forEach((state, clientId, map) =>
            {
                if (clientId == _doc.clientID || state == null) return;

                var field = state.field(AwarenessSelectionField);
                if (field == null) return;

                var selection = (YjsSelectionState)field;
                if (selection.anchor == null || selection.head == null) return;

                var anchor = Y.createAbsolutePositionFromRelativePosition(Y.createRelativePositionFromJSON(selection.anchor), _doc);
                var head   = Y.createAbsolutePositionFromRelativePosition(Y.createRelativePositionFromJSON(selection.head),   _doc);

                if (anchor == null || head == null || anchor.type != (Y.AbstractType)_text || head.type != (Y.AbstractType)_text) return;

                var user = state.user;
                var name = user != null ? user.name : null;

                if (!_styled.Contains(clientId))
                {
                    YjsCursorStyles.Acquire(clientId);
                    _styled.Add(clientId);
                }

                YjsCursorStyles.Update(clientId, name, user != null ? user.color : null, user != null ? user.colorLight : null);
                shown.Add(clientId);

                var a = _model.getPositionAt(YToModel(anchor.index));
                var h = _model.getPositionAt(YToModel(head.index));

                if (anchor.index != head.index)
                {
                    var anchorFirst = anchor.index < head.index;

                    decorations.Add(new TextDecoration
                    {
                        range   = anchorFirst ? Span(a, h) : Span(h, a),
                        options = new DecorationOptions
                        {
                            className    = YjsCursorStyles.SelectionClassFor(clientId),
                            hoverMessage = string.IsNullOrWhiteSpace(name) ? null : new MarkdownString { value = EscapeMarkdown(name) },
                            stickiness   = TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges,
                            description  = "tssm-yjs-selection"
                        }
                    });
                }

                var caretClass = YjsCursorStyles.CaretClassFor(clientId);
                if (_recent.TryGetValue(clientId, out var changed) && now - changed < RecentMilliseconds) caretClass += " " + YjsCursorStyles.RecentClass;
                if (h.lineNumber == 1) caretClass += " " + YjsCursorStyles.TopLineClass;

                decorations.Add(new TextDecoration
                {
                    range   = Span(h, h),
                    options = new DecorationOptions
                    {
                        beforeContentClassName = caretClass,
                        stickiness             = TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges,
                        description            = "tssm-yjs-caret"
                    }
                });
            });

            var array = decorations.ToArray();
            foreach (var bound in _editors) bound.Decorations.set(array);

            var gone = new List<int>();
            foreach (var clientId in _styled) if (!shown.Contains(clientId)) gone.Add(clientId);

            foreach (var clientId in gone)
            {
                _styled.Remove(clientId);
                _recent.Remove(clientId);
                YjsCursorStyles.Release(clientId);
            }
        }

        private static string EscapeMarkdown(string text)
        {
            var escaped = "";

            foreach (var c in text)
            {
                escaped += "\\`*_{}[]()#+-.!<>|~".IndexOf(c) >= 0 ? "\\" + c : c.ToString();
            }

            return escaped;
        }

        #endregion

        #region Offsets

        // A model offset and a shared-text index are the same number while the shared text has no \r. Once it has,
        // _shadow is a copy of it, and a \r\n pair counts as the one line break the (LF) model shows for it.

        private int YToModel(int index)
        {
            if (_shadow == null) return index;

            var offset = index;
            var limit  = Math.Min(index, _shadow.Length - 1);

            for (var i = 0; i < limit; i++)
            {
                if (_shadow[i] == '\r' && _shadow[i + 1] == '\n') offset--;
            }

            return offset;
        }

        private int ModelToY(int offset)
        {
            if (_shadow == null) return offset;

            var index = 0;
            var count = 0;

            while (count < offset && index < _shadow.Length)
            {
                index += _shadow[index] == '\r' && index + 1 < _shadow.Length && _shadow[index + 1] == '\n' ? 2 : 1;
                count++;
            }

            return index;
        }

        private int OffsetOf(int lineNumber, int column) => _model.getOffsetAt(new Position { lineNumber = lineNumber, column = column });

        private static TextRange Span(Position from, Position to) => new TextRange
        {
            startLineNumber = from.lineNumber,
            startColumn     = from.column,
            endLineNumber   = to.lineNumber,
            endColumn       = to.column
        };

        private void EnsureLineFeeds()
        {
            if (_model.getEOL() != "\n") _model.setEOL((int)EndOfLineSequence.LF);
        }

        private static string Normalize(string content) => content.Replace("\r\n", "\n").Replace("\r", "\n");

        #endregion

        /// <summary>
        /// Unbinds everything: the shared text, the document, the awareness and every editor. The remote cursors are
        /// removed, the local selection is withdrawn from the awareness, and the undo manager is destroyed. The model
        /// and the shared text keep their content.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            DetachModel();

            _text.unobserve(_onTextChanged);
            _doc.offBeforeAllTransactions(_onBeforeAllTransactions);

            if (_awareness != null)
            {
                _awareness.offChange(_onAwarenessChange);
                if (_published != null) _awareness.setLocalStateField(AwarenessSelectionField, null);
            }

            if (_undo != null)
            {
                _undo.offStackItemAdded(_onStackItemAdded);
                _undo.offStackItemPopped(_onStackItemPopped);
                _undo.destroy();
            }

            if (_recentTimer != 0) window.clearTimeout(_recentTimer);

            foreach (var clientId in _styled) YjsCursorStyles.Release(clientId);
            _styled.Clear();

            _disposed = true;

            var onDisposed = OnDisposed;
            OnDisposed = null;
            onDisposed?.Invoke();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MonacoYjsBinding));
        }

        private sealed class BoundEditor
        {
            public BoundEditor(IStandaloneCodeEditor editor) => Editor = editor;

            public IStandaloneCodeEditor        Editor      { get; }
            public DisposableBag                Listeners   { get; } = new DisposableBag();
            public IEditorDecorationsCollection Decorations { get; set; }
            public RelativeSelection            Saved       { get; set; }

            public void Release()
            {
                Decorations?.clear();
                Listeners.DisposeAll();
                Saved = null;
            }
        }

        private sealed class RelativeSelection
        {
            public RelativeSelection(Y.RelativePosition[] anchors, Y.RelativePosition[] heads, IStandaloneCodeEditor editor)
            {
                Anchors = anchors;
                Heads   = heads;
                Editor  = editor;
            }

            public Y.RelativePosition[]  Anchors { get; }
            public Y.RelativePosition[]  Heads   { get; }
            public IStandaloneCodeEditor Editor  { get; }
        }
    }

    /// <summary>
    /// The awareness <c>selection</c> field: where a collaborator's selection starts and where its caret is, as Yjs
    /// relative positions - the shape y-monaco writes and reads. What arrives from a peer is their JSON form; read it with
    /// <see cref="Y.createRelativePositionFromJSON"/>.
    /// </summary>
    [ObjectLiteral]
    public class YjsSelectionState
    {
        public object anchor;
        public object head;
    }
}
