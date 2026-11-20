using System;
using System.Collections.Generic;
using System.Linq;
using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>How finely a piece of AI-written text is tracked, and drawn.</summary>
    public enum AIMarking
    {
        /// <summary>
        /// One mark per line: a tint behind the line and Tesserae's AI accent in the strip beside the line numbers.
        /// Editing a line turns only that line's mark to "edited". The natural unit for code.
        /// </summary>
        Lines,

        /// <summary>
        /// One mark per sentence (a sentence ends at <c>.</c>, <c>!</c> or <c>?</c> followed by a space, or at a line
        /// break): the sentence's own text is tinted and underlined. Editing a sentence turns only it to "edited".
        /// The natural unit for prose.
        /// </summary>
        Sentences,

        /// <summary>One mark over the whole range, drawn like <see cref="Sentences"/>: a suggested phrase or a snippet.</summary>
        Range
    }

    /// <summary>A piece of text the model wrote, as it stands now.</summary>
    public sealed class AIMark
    {
        internal AIMark(TextRange range, AIMarking marking, string originalText, bool isEdited)
        {
            Range        = range;
            Marking      = marking;
            OriginalText = originalText;
            IsEdited     = isEdited;
        }

        /// <summary>Where the text is now. A <see cref="AIMarking.Lines"/> mark covers the part of its line the model wrote.</summary>
        public TextRange Range { get; }

        public AIMarking Marking { get; }

        /// <summary>What the model wrote there.</summary>
        public string OriginalText { get; }

        /// <summary>Whether the user has changed it since.</summary>
        public bool IsEdited { get; }
    }

    /// <summary>
    /// Which parts of a document a model wrote, drawn in the editor and kept true as the user edits: every mark is a
    /// model decoration remembering the text it covered, so it moves with the edits around it, and the moment its own
    /// text differs from what the model wrote it is drawn as edited instead (and goes back if the text is put back -
    /// the same rule as Tesserae's <c>AI</c> / <c>Edited</c> field badge, per line or per sentence). A mark whose text
    /// is deleted entirely is dropped. Reached as <c>editor.AIAnnotations</c>; <c>InsertAI</c> and <c>MarkAI</c> on
    /// the component are the usual way in.
    ///
    /// The drawing is Tesserae's AI language: the <c>--tss-ai-*</c> tint and accent, so it follows the theme. Marks
    /// also show in the overview ruler and the minimap, which is what makes "how much of this file did a model write"
    /// readable at a glance in a long document.
    ///
    /// Replacing the whole text (<c>SetText</c>) clears every mark: Monaco drops a model's decorations with its old
    /// content, and the new text is not the old answer. A remount keeps them.
    /// </summary>
    public sealed class AIAnnotations : IDisposable
    {
        private sealed class Entry
        {
            public string    Id;
            public AIMarking Marking;
            public string    Original;
            public bool      WholeLine;
            public bool      Edited;
            public TextRange Saved;
        }

        private sealed class Request
        {
            public TextRange Range;
            public AIMarking Marking;
        }

        private readonly List<Entry>   _entries   = new List<Entry>();
        private readonly List<Entry>   _saved     = new List<Entry>();
        private readonly List<Request> _requests  = new List<Request>();
        private readonly DisposableBag _listeners = new DisposableBag();

        private ITextModel _model;
        private bool       _disposed;
        private Action     _onChanged;

        /// <summary>The hover over a mark the user has not changed. Null for none. English by default.</summary>
        public string GeneratedHover { get; set; } = "AI-generated";

        /// <summary>The hover over a mark the user has changed. Null for none. English by default.</summary>
        public string EditedHover { get; set; } = "AI-generated, then edited";

        /// <summary>The model the marks are on, or null while detached.</summary>
        public ITextModel Model => _model;

        /// <summary>
        /// Called whenever a mark is added, removed, or turns edited or back - for a summary such as
        /// "12 of 40 lines written by AI".
        /// </summary>
        public AIAnnotations OnChanged(Action handler)
        {
            _onChanged = handler;
            return this;
        }

        /// <summary>Every mark, in document order.</summary>
        public AIMark[] Marks
        {
            get
            {
                if (_model is null)
                {
                    return _saved.Select(e => new AIMark(e.Saved, e.Marking, e.Original, e.Edited)).ToArray();
                }

                return _entries
                   .Select(e => new { e, range = _model.getDecorationRange(e.Id) })
                   .Where(x => x.range is object)
                   .OrderBy(x => x.range.startLineNumber).ThenBy(x => x.range.startColumn)
                   .Select(x => new AIMark(x.range, x.e.Marking, x.e.Original, x.e.Edited))
                   .ToArray();
            }
        }

        /// <summary>How many marks there are.</summary>
        public int Count => _model is null ? _saved.Count : _entries.Count;

        /// <summary>How many of them the user has changed.</summary>
        public int EditedCount => _model is null ? _saved.Count(e => e.Edited) : _entries.Count(e => e.Edited);

        /// <summary>
        /// Marks text that is already in the document as the model's, split as <paramref name="marking"/> says. Before
        /// a model is attached the request is kept and applied to the text the editor mounts with.
        /// </summary>
        public AIAnnotations Mark(TextRange range, AIMarking marking = AIMarking.Lines)
        {
            if (range is null) return this;

            if (_model is null)
            {
                _requests.Add(new Request { Range = range, Marking = marking });
                return this;
            }

            AddMarks(range, marking);
            Changed();
            return this;
        }

        /// <summary>Marks whole lines, <paramref name="first"/> to <paramref name="last"/> inclusive, one mark per line.</summary>
        public AIAnnotations MarkLines(int first, int last)
        {
            return Mark(new TextRange { startLineNumber = first, startColumn = 1, endLineNumber = last, endColumn = int.MaxValue }, AIMarking.Lines);
        }

        /// <summary>
        /// Inserts what the model wrote at <paramref name="at"/> (replacing <paramref name="replace"/> when given) as one
        /// undoable edit, and marks it. Returns the inserted range, or null when nothing was inserted (no model, or the
        /// edit was refused - by <see cref="LockedLines"/>, say).
        /// </summary>
        public TextRange Insert(string text, Position at, AIMarking marking = AIMarking.Lines, TextRange replace = null)
        {
            if (_model is null || string.IsNullOrEmpty(text) || (at is null && replace is null)) return null;

            var target = replace ?? new TextRange { startLineNumber = at.lineNumber, startColumn = at.column, endLineNumber = at.lineNumber, endColumn = at.column };

            // Monaco normalises line breaks to the model's, so measure what it will actually hold.
            text = text.Replace("\r\n", "\n");
            if (_model.getEOL() == "\r\n") text = text.Replace("\n", "\r\n");

            var version = _model.getVersionId();
            var start   = _model.getOffsetAt(new Position { lineNumber = target.startLineNumber, column = target.startColumn });

            _model.pushStackElement();
            _model.pushEditOperations(new TextSelection[0], Script.ToArray(new[] { new TextEdit { range = target, text = text } }), _ => null);
            _model.pushStackElement();

            if (_model.getVersionId() == version) return null;

            var from  = _model.getPositionAt(start);
            var to    = _model.getPositionAt(start + text.Length);
            var range = new TextRange { startLineNumber = from.lineNumber, startColumn = from.column, endLineNumber = to.lineNumber, endColumn = to.column };

            AddMarks(range, marking);
            Changed();

            return range;
        }

        /// <summary>Removes every mark that touches <paramref name="lineNumber"/> - the user accepted that line as their own.</summary>
        public AIAnnotations Unmark(int lineNumber)
        {
            if (_model is null) return this;

            var gone = _entries.Where(e =>
            {
                var r = _model.getDecorationRange(e.Id);
                return r is object && r.startLineNumber <= lineNumber && r.endLineNumber >= lineNumber;
            }).ToList();

            Remove(gone);
            return this;
        }

        /// <summary>Removes every mark.</summary>
        public AIAnnotations Clear()
        {
            _requests.Clear();
            _saved.Clear();

            if (_model is object) Remove(_entries.ToList());
            else Changed();

            return this;
        }

        #region Attaching

        /// <summary>
        /// Draws the marks in <paramref name="model"/> and keeps them up to date. The component calls this itself; a
        /// host with its own models calls it directly. A model attached before is detached first.
        /// </summary>
        public AIAnnotations Attach(CodeModel model)
        {
            AttachModel(model?.Native);
            return this;
        }

        /// <summary>Stops tracking, keeping the marks so a later <see cref="Attach"/> to the same text redraws them.</summary>
        public AIAnnotations Detach()
        {
            if (_model is null) return this;

            _saved.Clear();

            foreach (var e in _entries)
            {
                var r = _model.isDisposed() ? null : _model.getDecorationRange(e.Id);
                if (r is object) { e.Saved = r; _saved.Add(e); }
            }

            if (!_model.isDisposed()) _model.deltaDecorations(Script.ToArray(_entries.Select(e => e.Id).ToArray()), new TextDecoration[0]);

            _entries.Clear();
            _listeners.DisposeAll();
            _model = null;

            return this;
        }

        internal void AttachSurface(EditorSurface surface) => AttachModel(surface?.Model?.Native);

        private void AttachModel(ITextModel model)
        {
            if (_disposed || model is null || model == _model) return;

            Detach();
            AIStyles.Ensure();

            _model = model;

            _listeners.Add(model.onDidChangeContent(OnContentChanged));
            _listeners.Add(model.onWillDispose(() => Detach()));

            // A remount redraws the marks where they were left; a first attach applies what was asked for before it.
            foreach (var e in _saved)
            {
                e.Id = Decorate(null, e.Saved, e);
                _entries.Add(e);
            }

            _saved.Clear();

            foreach (var request in _requests) AddMarks(request.Range, request.Marking);
            _requests.Clear();

            Refresh();
            Changed();
        }

        #endregion

        #region Marks

        private void AddMarks(TextRange range, AIMarking marking)
        {
            range = Clamp(range);
            if (range is null) return;

            switch (marking)
            {
                case AIMarking.Lines:
                {
                    for (var line = range.startLineNumber; line <= range.endLineNumber; line++)
                    {
                        var max   = _model.getLineMaxColumn(line);
                        var start = line == range.startLineNumber ? range.startColumn : 1;
                        var end   = line == range.endLineNumber ? range.endColumn : max;

                        // A blank line is nobody's writing.
                        var part = new TextRange { startLineNumber = line, startColumn = start, endLineNumber = line, endColumn = end };
                        var text = _model.getValueInRange(part);
                        if (text.Trim().Length == 0) continue;

                        Add(part, marking, text, start == 1 && end == max);
                    }

                    break;
                }

                case AIMarking.Sentences:
                {
                    var baseOffset = _model.getOffsetAt(new Position { lineNumber = range.startLineNumber, column = range.startColumn });
                    var text       = _model.getValueInRange(range);

                    foreach (var (from, to) in Sentences(text))
                    {
                        var a = _model.getPositionAt(baseOffset + from);
                        var b = _model.getPositionAt(baseOffset + to);
                        Add(new TextRange { startLineNumber = a.lineNumber, startColumn = a.column, endLineNumber = b.lineNumber, endColumn = b.column },
                            marking, text.Substring(from, to - from), false);
                    }

                    break;
                }

                default:
                {
                    var text = _model.getValueInRange(range);
                    if (text.Trim().Length > 0) Add(range, marking, text, false);
                    break;
                }
            }
        }

        // Sentences of text as [from, to) offsets, surrounding whitespace excluded. A sentence ends after . ! or ? when
        // whitespace (or the end) follows - so "a.b" and "3.14" stay inside one - and at every line break.
        internal static List<(int, int)> Sentences(string text)
        {
            var result = new List<(int, int)>();
            var start  = -1;

            void Emit(int end)
            {
                while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
                if (end > start) result.Add((start, end));
                start = -1;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (start < 0)
                {
                    if (!char.IsWhiteSpace(c)) start = i;
                    else continue;
                }

                if (c == '\n' || c == '\r')
                {
                    Emit(i);
                }
                else if ((c == '.' || c == '!' || c == '?') && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))
                {
                    Emit(i + 1);
                }
            }

            if (start >= 0) Emit(text.Length);

            return result;
        }

        private void Add(TextRange range, AIMarking marking, string original, bool wholeLine)
        {
            var e = new Entry { Marking = marking, Original = original, WholeLine = wholeLine };
            e.Id = Decorate(null, range, e);
            _entries.Add(e);
        }

        private string Decorate(string oldId, TextRange range, Entry e)
        {
            var old = oldId is null ? new string[0] : new[] { oldId };
            var ids = _model.deltaDecorations(Script.ToArray(old), Script.ToArray(new[] { new TextDecoration { range = range, options = OptionsFor(e) } }));
            return ids.Length > 0 ? ids[0] : null;
        }

        private DecorationOptions OptionsFor(Entry e)
        {
            var hoverText = e.Edited ? EditedHover : GeneratedHover;
            var accent    = AIStyles.AccentColor();

            var o = new DecorationOptions
            {
                description   = "ai-generated",
                stickiness    = TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges,
                hoverMessage  = string.IsNullOrEmpty(hoverText) ? null : new MarkdownString { value = hoverText },
                overviewRuler = new OverviewRulerOptions { color = accent, position = e.Marking == AIMarking.Lines ? OverviewRulerLane.Left : OverviewRulerLane.Center },
                minimap       = new DecorationMinimapOptions { color = accent, position = MinimapPosition.Inline }
            };

            if (e.Marking == AIMarking.Lines)
            {
                o.isWholeLine               = true;
                o.className                 = e.Edited ? AIStyles.LineEdited : AIStyles.Line;
                o.linesDecorationsClassName = e.Edited ? AIStyles.GutterEdited : AIStyles.Gutter;
            }
            else
            {
                o.inlineClassName = e.Edited ? AIStyles.RangeEdited : AIStyles.Range;
            }

            // An edited mark stays readable in the ruler without shouting as loudly as an untouched one.
            if (e.Edited)
            {
                o.overviewRuler = null;
                o.minimap       = null;
            }

            return o;
        }

        private void OnContentChanged(IContentChangedEvent e)
        {
            if (e.isFlush)
            {
                // setValue drops every decoration with the old content.
                _entries.Clear();
                Changed();
                return;
            }

            if (Refresh()) Changed();
        }

        // Re-reads every mark against what the model wrote; true when anything changed.
        private bool Refresh()
        {
            var changed = false;
            var gone    = new List<Entry>();

            foreach (var e in _entries)
            {
                var r = _model.getDecorationRange(e.Id);

                if (r is null || (r.startLineNumber == r.endLineNumber && r.startColumn == r.endColumn))
                {
                    gone.Add(e);
                    continue;
                }

                // A whole line the model wrote is changed by anything typed on it, including at its ends - which the
                // decoration does not grow over, so compare the line rather than the range.
                var current = e.WholeLine && r.startLineNumber == r.endLineNumber
                    ? _model.getLineContent(r.startLineNumber)
                    : _model.getValueInRange(r);

                var edited = current != e.Original;

                if (edited != e.Edited)
                {
                    e.Edited = edited;
                    e.Id     = Decorate(e.Id, r, e);
                    changed  = true;
                }
            }

            if (gone.Count > 0)
            {
                Remove(gone, notify: false);
                changed = true;
            }

            return changed;
        }

        private void Remove(List<Entry> gone, bool notify = true)
        {
            if (gone.Count == 0) return;

            _model.deltaDecorations(Script.ToArray(gone.Select(e => e.Id).ToArray()), new TextDecoration[0]);
            foreach (var e in gone) _entries.Remove(e);

            if (notify) Changed();
        }

        private TextRange Clamp(TextRange range)
        {
            var lines = _model.getLineCount();

            var startLine = Math.Max(1, Math.Min(lines, range.startLineNumber));
            var endLine   = Math.Max(startLine, Math.Min(lines, range.endLineNumber));
            var startCol  = Math.Max(1, Math.Min(_model.getLineMaxColumn(startLine), range.startColumn));
            var endCol    = Math.Max(1, Math.Min(_model.getLineMaxColumn(endLine), range.endColumn));

            if (startLine == endLine && endCol <= startCol) return null;

            return new TextRange { startLineNumber = startLine, startColumn = startCol, endLineNumber = endLine, endColumn = endCol };
        }

        private void Changed() => _onChanged?.Invoke();

        #endregion

        /// <summary>Removes every mark and stops tracking.</summary>
        public void Dispose()
        {
            if (_disposed) return;

            if (_model is object) Remove(_entries.ToList(), notify: false);
            Detach();

            _saved.Clear();
            _requests.Clear();
            _disposed = true;
        }

        /// <summary>Whether this tracker has been disposed.</summary>
        public bool IsDisposed => _disposed;
    }
}
