using System;
using System.Collections.Generic;
using System.Linq;
using Transpose;
using static Transpose.Core.es5;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Lines of a document the user cannot change or delete, while everything around them stays editable.
    ///
    /// A locked line is a <i>line</i>, not a line number: it is tracked by a model decoration, so text added
    /// above it moves it down and text removed above it moves it up, and it is always the same line the host
    /// locked. The user can type before and after it, press Enter at its start (a new line above) or at its
    /// end (a new line below), and delete whole lines next to it - but any edit that would change its text,
    /// remove it, or join it onto a neighbour is refused before it is applied.
    ///
    /// <code>
    /// MonacoEditor.Editor()
    ///    .SetLanguage("markdown")
    ///    .SetText(template)
    ///    .LockLines(line => line.StartsWith("#"));   // every heading the template starts with
    /// </code>
    ///
    /// <para><b>How the refusal works.</b> Monaco has no read-only ranges and no "will change" event, but every
    /// edit a user makes - typing, IME composition, paste, cut, drag and drop, snippets, line commands,
    /// format document - reaches the model through <c>ITextModel.pushEditOperations</c>, and the cursor
    /// treats a null answer from it as "nothing happened". The guard puts its own <c>pushEditOperations</c>
    /// on the model instance, in front of the prototype's, and answers null for a batch that would touch a
    /// locked line. A refused edit therefore never happens: nothing flickers, and the undo stack is exactly
    /// as it was. Undo and redo do not go through that method (they replay edits that were allowed when they
    /// were made), nor does <c>setValue</c> or <c>applyEdits</c>, so a host's own <c>Text</c> assignment and
    /// a collaborator's remote edit are never blocked.</para>
    ///
    /// <para><b>Host edits.</b> <c>CodeModel.ApplyEdits</c> and <c>EditorSurface.ExecuteEdits</c> also go through
    /// <c>pushEditOperations</c>, so they are held to the same rule; wrap one in <see cref="RunUnguarded"/> to
    /// let it through. Replacing the whole document (<c>Text = ...</c>) resets the locks to what they were
    /// first given (the line numbers and the predicates, applied to the new text), since Monaco drops a
    /// model's decorations along with its old content.</para>
    ///
    /// <para>The package ships no stylesheet, so a locked line looks like any other until the host styles
    /// <see cref="LineClassName"/> and <see cref="GutterClassName"/> (or replaces them with
    /// <see cref="Decoration"/>).</para>
    /// </summary>
    public sealed class LockedLines : IDisposable
    {
        /// <summary>The class on every locked line's whole width.</summary>
        public const string LineClassName = "tssm-locked-line";

        /// <summary>The class on the strip between the line numbers and the text of every locked line.</summary>
        public const string GutterClassName = "tssm-locked-line-gutter";

        private const string HOOKED_METHOD = "pushEditOperations";

        private readonly List<int>                _lines      = new List<int>();
        private readonly List<Func<string, bool>> _predicates = new List<Func<string, bool>>();
        private readonly DisposableBag            _listeners  = new DisposableBag();
        // object, not IStandaloneCodeEditor: a BCL generic over an [External] interface cannot be constructed.
        private readonly List<object>             _editors    = new List<object>();

        private ITextModel        _model;
        private string[]          _ids = new string[0];
        private int[]             _restore;
        private int               _unguarded;
        private bool              _disposed;
        private DecorationOptions _decoration;

        private Action<int> _onBlocked;

        /// <summary>
        /// The message shown at the caret when an edit is refused, in Monaco's own read-only bubble. Null
        /// (or empty) shows nothing. English by default; a host that translates sets its own.
        /// </summary>
        public string BlockedMessage { get; set; } = "This line is locked and cannot be changed.";

        /// <summary>The model the locks are on, or null while detached.</summary>
        public ITextModel Model => _model;

        /// <summary>Whether this guard has been disposed.</summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// The locked lines' current line numbers, in order. While detached, the lines that will be locked
        /// when a model is attached.
        /// </summary>
        public int[] LineNumbers
        {
            get
            {
                if (_model is null) return _restore ?? _lines.Distinct().OrderBy(l => l).ToArray();
                return CurrentLines();
            }
        }

        /// <summary>Whether <paramref name="lineNumber"/> is locked now.</summary>
        public bool IsLocked(int lineNumber) => LineNumbers.Contains(lineNumber);

        /// <summary>
        /// Locks lines by number, as the document is when the guard is attached (or now, if it already is).
        /// Out-of-range numbers are ignored.
        /// </summary>
        public LockedLines Lock(params int[] lineNumbers)
        {
            if (lineNumbers is null) return this;

            _lines.AddRange(lineNumbers);

            if (_model is object) SetLocks(CurrentLines().Concat(lineNumbers));
            else if (_restore is object) _restore = _restore.Concat(lineNumbers).ToArray();

            return this;
        }

        /// <summary>
        /// Locks every line whose text matches <paramref name="predicate"/> when the guard is attached (or
        /// now, if it already is). A line the user writes later that happens to match is <i>not</i> locked:
        /// the predicate picks lines out of the document as it was given, which is what "the template's
        /// headings" means.
        /// </summary>
        public LockedLines Lock(Func<string, bool> predicate)
        {
            if (predicate is null) return this;

            _predicates.Add(predicate);

            if (_model is object) SetLocks(CurrentLines().Concat(Matching(predicate)));

            return this;
        }

        /// <summary>Unlocks the line now at <paramref name="lineNumber"/>, if it is locked.</summary>
        public LockedLines Unlock(int lineNumber)
        {
            _lines.Remove(lineNumber);

            if (_model is object) SetLocks(CurrentLines().Where(l => l != lineNumber));
            else if (_restore is object) _restore = _restore.Where(l => l != lineNumber).ToArray();

            return this;
        }

        /// <summary>Unlocks everything, including what a later <c>Text</c> replacement would have re-locked.</summary>
        public LockedLines UnlockAll()
        {
            _lines.Clear();
            _predicates.Clear();
            _restore = null;

            if (_model is object) SetLocks(new int[0]);

            return this;
        }

        /// <summary>
        /// Replaces the decoration every locked line is painted with. The range and the stickiness are the
        /// guard's own (the decoration is how a line is tracked); everything else is the host's.
        /// </summary>
        public LockedLines Decoration(DecorationOptions options)
        {
            _decoration = options;

            if (_model is object) SetLocks(CurrentLines());

            return this;
        }

        /// <summary>
        /// Called with the locked line's number each time an edit is refused - for a host that wants to say
        /// so somewhere other than <see cref="BlockedMessage"/>'s bubble.
        /// </summary>
        public LockedLines OnBlocked(Action<int> handler)
        {
            _onBlocked = handler;
            return this;
        }

        /// <summary>
        /// Runs <paramref name="edit"/> with the guard off, for a host edit through <c>ApplyEdits</c> or
        /// <c>ExecuteEdits</c> that is allowed to touch a locked line. A locked line the edit removes is
        /// unlocked; one it rewrites stays locked at its new content.
        /// </summary>
        public void RunUnguarded(Action edit)
        {
            if (edit is null) return;

            _unguarded++;

            try
            {
                edit();
            }
            finally
            {
                _unguarded--;
            }
        }

        #region Attaching

        /// <summary>
        /// Starts guarding <paramref name="model"/>: the lines given so far are locked in it and edits to them
        /// refused. A model attached before is detached first. The component calls this itself
        /// (<c>LockLines</c>); a host with its own models calls it directly, and <see cref="AddEditor"/> for
        /// each editor that should show <see cref="BlockedMessage"/>.
        /// </summary>
        public LockedLines Attach(CodeModel model)
        {
            AttachModel(model?.Native);
            return this;
        }

        /// <summary>Shows <see cref="BlockedMessage"/> in <paramref name="editor"/> when it has the focus.</summary>
        public LockedLines AddEditor(IStandaloneCodeEditor editor)
        {
            if (editor is object && !_editors.Contains(editor)) _editors.Add(editor);
            return this;
        }

        /// <summary>Stops showing messages in <paramref name="editor"/>.</summary>
        public LockedLines RemoveEditor(IStandaloneCodeEditor editor)
        {
            _editors.Remove(editor);
            return this;
        }

        /// <summary>
        /// Stops guarding the model, keeping the locked lines' current numbers so a later
        /// <see cref="Attach"/> - to the model a remount builds from the same text - locks the same lines.
        /// </summary>
        public LockedLines Detach()
        {
            if (_model is null) return this;

            _restore = CurrentLines();

            if (!_model.isDisposed())
            {
                _model.deltaDecorations(Script.ToArray(_ids), new TextDecoration[0]);

                // The hook is an own property shadowing the prototype's method; deleting it puts Monaco's back.
                Script.Delete(_model, HOOKED_METHOD);
            }

            _listeners.DisposeAll();
            _model = null;
            _ids   = new string[0];

            return this;
        }

        internal void AttachSurface(EditorSurface surface)
        {
            if (surface is null) return;

            AttachModel(surface.Model?.Native);
            AddEditor(surface.Native);
        }

        internal void DetachSurface(EditorSurface surface)
        {
            if (surface is object) RemoveEditor(surface.Native);
            Detach();
        }

        private void AttachModel(ITextModel model)
        {
            if (_disposed || model is null || model == _model) return;

            Detach();

            _model = model;

            var original = Script.Get<Function>(model, HOOKED_METHOD);

            Func<object, object, object, object, object, object> hook = (before, operations, computer, group, reason) =>
            {
                if (_unguarded == 0)
                {
                    var blocked = FindBlockedLine((TextEdit[])operations);

                    if (blocked > 0)
                    {
                        Blocked(blocked);
                        return null;
                    }
                }

                return original.call(model, before, operations, computer, group, reason);
            };

            Script.Set(model, HOOKED_METHOD, hook);

            _listeners.Add(model.onDidChangeContent(OnContentChanged));
            _listeners.Add(model.onWillDispose(() => Detach()));

            // A remount re-locks the lines as they were left; a first attach applies what it was given.
            var initial = _restore ?? Spec();
            _restore = null;

            SetLocks(initial);
        }

        #endregion

        #region Tracking

        private IEnumerable<int> Spec()
        {
            var lines = _lines.ToList();

            foreach (var predicate in _predicates) lines.AddRange(Matching(predicate));

            return lines;
        }

        private IEnumerable<int> Matching(Func<string, bool> predicate)
        {
            var count = _model.getLineCount();

            for (var line = 1; line <= count; line++)
            {
                if (predicate(_model.getLineContent(line))) yield return line;
            }
        }

        private int[] CurrentLines()
        {
            if (_model is null) return new int[0];

            var lines = new List<int>();

            foreach (var id in _ids)
            {
                var range = _model.getDecorationRange(id);
                if (range is object && !lines.Contains(range.startLineNumber)) lines.Add(range.startLineNumber);
            }

            lines.Sort();
            return lines.ToArray();
        }

        private void SetLocks(IEnumerable<int> lines)
        {
            var count = _model.getLineCount();

            var decorations = lines
               .Where(l => l >= 1 && l <= count)
               .Distinct()
               .OrderBy(l => l)
               .Select(l => new TextDecoration
               {
                   range   = new TextRange { startLineNumber = l, startColumn = 1, endLineNumber = l, endColumn = _model.getLineMaxColumn(l) },
                   options = DecorationFor()
               })
               .ToArray();

            _ids = _model.deltaDecorations(Script.ToArray(_ids), Script.ToArray(decorations));
        }

        private DecorationOptions DecorationFor()
        {
            var o = _decoration ?? new DecorationOptions
            {
                className                 = LineClassName,
                linesDecorationsClassName = GutterClassName,
                isWholeLine               = true,
                description               = "locked-line"
            };

            // The tracking depends on these two: a line that grew over text typed at its edges would swallow
            // the new line Enter just made beside it.
            o.stickiness  = TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges;
            o.isWholeLine = true;

            return o;
        }

        private void OnContentChanged(IContentChangedEvent e)
        {
            if (e.isFlush)
            {
                // setValue clears every decoration with the old content: start over from what was given.
                _ids = new string[0];
                SetLocks(Spec());
                return;
            }

            // A locked line normally keeps its exact range (nothing allowed through edits it), but an edit that
            // bypassed the guard - undo past a lock added later, a remote edit, RunUnguarded - can leave a
            // decoration over part of a line, collapsed, or sharing a line with another. Put each back over the
            // whole of the line it starts on.
            var stale = false;
            var seen  = new List<int>();

            foreach (var id in _ids)
            {
                var range = _model.getDecorationRange(id);

                if (range is null || seen.Contains(range.startLineNumber)
                 || range.startColumn != 1 || range.endLineNumber != range.startLineNumber
                 || range.endColumn != _model.getLineMaxColumn(range.startLineNumber))
                {
                    stale = true;
                }

                if (range is object) seen.Add(range.startLineNumber);
            }

            if (stale) SetLocks(seen);
        }

        #endregion

        #region The rule

        private enum Effect { Clear, Edge, Blocked }

        // The first locked line an edit batch would change, delete, join onto a neighbour or move, or 0 when the
        // batch is allowed. Ranges are the document's before the batch, as Monaco hands them in.
        private int FindBlockedLine(TextEdit[] operations)
        {
            if (operations is null || _ids.Length == 0) return 0;

            foreach (var id in _ids)
            {
                var locked = _model.getDecorationRange(id);
                if (locked is null) continue;

                var line  = locked.startLineNumber;
                var edges = 0;

                foreach (var op in operations)
                {
                    if (op is null || op.range is null) continue;

                    var effect = EffectOn(op.range, op.text ?? "", line);

                    if (effect == Effect.Blocked) return line;
                    if (effect == Effect.Edge) edges++;
                }

                // Each edit at an edge is fine on its own, but two in one batch is how a line is moved: Alt+Down
                // inserts the line below above this one and deletes it from below, and the heading has swapped
                // places with its neighbour without its text ever being touched.
                if (edges > 1) return line;
            }

            return 0;
        }

        private Effect EffectOn(TextRange range, string text, int line)
        {
            var lineEnd = _model.getLineMaxColumn(line);

            var startsAfter  = Compare(range.startLineNumber, range.startColumn, line, lineEnd) > 0;
            var endsBefore   = Compare(range.endLineNumber,   range.endColumn,   line, 1)       < 0;

            if (startsAfter || endsBefore) return Effect.Clear;

            var startsAtEnd  = range.startLineNumber == line && range.startColumn == lineEnd;
            var endsAtStart  = range.endLineNumber   == line && range.endColumn   == 1;
            var empty        = range.startLineNumber == range.endLineNumber && range.startColumn == range.endColumn;

            if (empty && text.Length == 0) return Effect.Clear;

            bool allowed;

            if (lineEnd == 1 && empty)
            {
                // An empty locked line: an insertion at it has no side to land on but its own, so only line
                // breaks - which leave an empty line wherever the lock ends up - are allowed.
                allowed = text.Replace("\r", "").Replace("\n", "").Length == 0;
            }
            else if (endsAtStart)
            {
                // The edit ends where the line begins: the line still begins a line afterwards if what now
                // precedes it is a line break - the last character inserted, or, inserting nothing, the
                // character before the range, which is a line break when the range starts a line.
                allowed = text.Length > 0 ? text.EndsWith("\n") : range.startColumn == 1;
            }
            else if (startsAtEnd)
            {
                // The mirror image: what follows the line must still be a line break, or the end of the text.
                allowed = text.Length > 0
                    ? text.StartsWith("\n") || text.StartsWith("\r\n")
                    : range.endColumn == _model.getLineMaxColumn(range.endLineNumber);
            }
            else
            {
                // Anything else overlaps the line's text.
                allowed = false;
            }

            return allowed ? Effect.Edge : Effect.Blocked;
        }

        private static int Compare(int lineA, int columnA, int lineB, int columnB)
        {
            if (lineA != lineB) return lineA < lineB ? -1 : 1;
            if (columnA != columnB) return columnA < columnB ? -1 : 1;
            return 0;
        }

        private void Blocked(int line)
        {
            _onBlocked?.Invoke(line);

            if (string.IsNullOrEmpty(BlockedMessage)) return;

            foreach (var item in _editors)
            {
                var editor = (IStandaloneCodeEditor)item;

                if (!editor.hasTextFocus()) continue;

                var controller = (IMessageController)editor.getContribution("editor.contrib.messageController");
                controller?.showMessage(BlockedMessage, editor.getPosition());
            }
        }

        #endregion

        /// <summary>Unlocks everything and stops guarding for good.</summary>
        public void Dispose()
        {
            if (_disposed) return;

            Detach();

            _disposed = true;
            _editors.Clear();
            OnDisposed?.Invoke();
        }

        internal Action OnDisposed;
    }
}
