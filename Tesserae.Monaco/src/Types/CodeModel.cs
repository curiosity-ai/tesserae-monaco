using System;
using Transpose;
using Tesserae;

namespace Tesserae.Monaco
{
    /// <summary>Line endings a model can be normalised to, matching Monaco's <c>EndOfLineSequence</c>.</summary>
    [Enum(Emit.Value)]
    public enum EndOfLineSequence
    {
        LF   = 0,
        CRLF = 1
    }

    /// <summary>
    /// A Monaco text model - a document, independent of any editor showing it.
    ///
    /// Models matter for three things the wrapper could not otherwise do: showing several documents in
    /// one editor (create a model per file, <c>SetModel</c> to switch, and pair it with
    /// <c>SaveViewState</c>/<c>RestoreViewState</c> so each keeps its caret and scroll), editing without
    /// destroying the undo stack (<see cref="ApplyEdits"/> rather than <see cref="Text"/>'s setter), and
    /// giving a document a <see cref="Uri"/> - which is how Monaco's bundled TypeScript and JSON
    /// services address files.
    ///
    /// A model you create is yours to <see cref="Dispose"/>. Monaco does not dispose models handed to an
    /// editor, so a host that creates one per file has to release them itself.
    /// </summary>
    public sealed class CodeModel
    {
        /// <summary>The underlying Monaco <c>ITextModel</c>.</summary>
        public ITextModel Native { get; private set; }

        private CodeModel(ITextModel native)
        {
            Native = native;
        }

        internal static CodeModel Wrap(ITextModel native)
        {
            return native is null ? null : new CodeModel(native);
        }

        /// <summary>
        /// Creates a model. Give it a <paramref name="uri"/> when a language service needs to address the
        /// document - the TypeScript service resolves imports by URI, and a JSON schema is matched
        /// against one - and leave it null otherwise, in which case Monaco invents one.
        /// </summary>
        /// <param name="text">The initial content.</param>
        /// <param name="language">A Monaco language id, or null for plaintext.</param>
        /// <param name="uri">
        /// A URI string such as <c>"file:///src/main.ts"</c> or <c>"inmemory://model/1"</c>. Creating a
        /// second model for a URI that is already taken throws inside Monaco, so use
        /// <see cref="MonacoEditor.GetModel"/> first when the same file may be opened twice.
        /// </param>
        public static CodeModel Create(string text, string language = null, string uri = null)
        {
            if (!MonacoEditor.IsLoaded) return null;

            var resource = string.IsNullOrWhiteSpace(uri) ? null : MonacoUri.parse(uri);

            return new CodeModel(MonacoApi.editor.createModel(text ?? "", language ?? "plaintext", resource));
        }

        /// <summary>The whole document.</summary>
        public string Text
        {
            get => Native is null ? "" : Native.getValue();
            set => Native?.setValue(value ?? "");
        }

        /// <summary>The model's URI as a string.</summary>
        public string Uri => Native?.uri?.asString();

        /// <summary>The model's language id.</summary>
        public string Language => Native?.getLanguageId();

        /// <summary>Changes the language, keeping the content and undo stack.</summary>
        public CodeModel SetLanguage(string language)
        {
            if (Native != null) MonacoApi.editor.setModelLanguage(Native, language ?? "plaintext");

            return this;
        }

        /// <summary>How many lines the document has.</summary>
        public int LineCount => Native is null ? 0 : Native.getLineCount();

        /// <summary>The version id, which increases on every change - useful for discarding stale async work.</summary>
        public int VersionId => Native is null ? 0 : Native.getVersionId();

        /// <summary>One line's text, without its line ending. Lines are one-based.</summary>
        public string GetLineContent(int lineNumber)
        {
            if (Native is null || lineNumber < 1 || lineNumber > LineCount) return null;

            return Native.getLineContent(lineNumber);
        }

        /// <summary>The text inside a range.</summary>
        public string GetValueInRange(TextRange range)
        {
            if (Native is null || range is null) return null;

            return Native.getValueInRange(range);
        }

        /// <summary>The whole document's range.</summary>
        public TextRange GetFullRange() => Native?.getFullModelRange();

        /// <summary>The character offset of a position - what most compiler APIs address code by.</summary>
        public int GetOffsetAt(Position position)
        {
            return Native is null || position is null ? 0 : Native.getOffsetAt(position);
        }

        /// <summary>The position of a character offset - the inverse of <see cref="GetOffsetAt"/>.</summary>
        public Position GetPositionAt(int offset) => Native?.getPositionAt(offset);

        /// <summary>The word at a position, or null when the position isn't on one.</summary>
        public string GetWordAt(Position position)
        {
            if (Native is null || position is null) return null;

            return Native.getWordAtPosition(position)?.word;
        }

        /// <summary>
        /// Every match for <paramref name="searchString"/>, as ranges. Pair with a decoration collection
        /// to highlight them.
        /// </summary>
        public TextRange[] FindMatches(
            string searchString,
            bool   isRegex   = false,
            bool   matchCase = false,
            bool   wholeWord = false,
            int    limit     = 1000)
        {
            if (Native is null || string.IsNullOrEmpty(searchString)) return new TextRange[0];

            // Monaco takes the word separators rather than a "whole word" flag: null means substring.
            var separators = wholeWord ? WORD_SEPARATORS : null;
            var matches    = Native.findMatches(searchString, true, isRegex, matchCase, separators, false, limit);
            var ranges     = new TextRange[matches.Length];

            for (var i = 0; i < matches.Length; i++)
            {
                ranges[i] = matches[i].range;
            }

            return ranges;
        }

        private const string WORD_SEPARATORS = "`~!@#$%^&*()-=+[{]}\\|;:'\",.<>/?";

        /// <summary>
        /// Applies edits <b>without</b> resetting the undo stack or the caret - the difference between
        /// this and assigning <see cref="Text"/>. Coordinates are Monaco's own, i.e. one-based, and the
        /// edits are applied as one undoable step.
        /// </summary>
        public CodeModel ApplyEdits(ReadOnlyArray<TextEdit> edits)
        {
            if (Native is null || edits is null || edits.Length == 0) return this;

            // Script.ToArray strips the $type marker Transpose stamps onto a C# array. Monaco posts
            // edits to its editor worker to minimise them, and postMessage refuses a value carrying it.
            Native.pushEditOperations(new TextSelection[0], Script.ToArray(edits), _ => null);

            return this;
        }

        /// <summary>
        /// Closes the current undo step, so the next edits undo separately. Call it between two
        /// programmatic edits the user should be able to undo one at a time.
        /// </summary>
        public CodeModel PushStackElement()
        {
            Native?.pushStackElement();

            return this;
        }

        /// <summary>Undoes the last step on the model's own undo stack.</summary>
        public CodeModel Undo()
        {
            Native?.undo();

            return this;
        }

        /// <summary>Redoes the last undone step.</summary>
        public CodeModel Redo()
        {
            Native?.redo();

            return this;
        }

        /// <summary>Sets the indentation the editor inserts and reports for this document.</summary>
        public CodeModel SetIndentation(int tabSize, bool insertSpaces = true)
        {
            Native?.updateOptions(new TextModelOptions { tabSize = tabSize, insertSpaces = insertSpaces });

            return this;
        }

        /// <summary>Normalises the document's line endings.</summary>
        public CodeModel SetEndOfLine(EndOfLineSequence eol)
        {
            Native?.setEOL((int)eol);

            return this;
        }

        /// <summary>
        /// The squiggles currently on this model, from every owner - including the ones Monaco's own
        /// bundled workers produce for JSON, TypeScript, CSS and HTML. This is the read side that makes
        /// an error count or a "go to next problem" possible.
        /// </summary>
        public CodeMarker[] GetMarkers()
        {
            if (Native is null || !MonacoEditor.IsLoaded) return new CodeMarker[0];

            return MonacoApi.editor.getModelMarkers(new MarkerFilter { resource = Native.uri });
        }

        /// <summary>
        /// Replaces the squiggles owned by <paramref name="owner"/>, leaving every other owner's alone.
        /// Passing an empty array clears just that owner.
        /// </summary>
        public CodeModel SetMarkers(ReadOnlyArray<CodeMarker> markers, string owner = MonacoEditor.DEFAULT_MARKER_OWNER)
        {
            if (Native != null && MonacoEditor.IsLoaded)
            {
                MonacoApi.editor.setModelMarkers(Native, owner, markers);
            }

            return this;
        }

        /// <summary>Runs <paramref name="handler"/> on every content change. Returns the subscription.</summary>
        public IJsDisposable OnChanged(Action<IContentChangedEvent> handler)
        {
            if (Native is null || handler is null) return null;

            return Native.onDidChangeContent(handler);
        }

        /// <summary>Releases the model. Editors still showing it are left without one, so switch them first.</summary>
        public void Dispose()
        {
            if (Native is null) return;

            Native.dispose();
            Native = null;
        }
    }

    /// <summary>
    /// An editor's caret, selection, scroll position and folding state, as returned by
    /// <c>SaveViewState</c>. Opaque - hand it back to <c>RestoreViewState</c> unchanged.
    /// </summary>
    public sealed class EditorViewState
    {
        internal IViewState Native { get; }

        internal EditorViewState(IViewState native)
        {
            Native = native;
        }

        /// <summary>True when there is actually state to restore.</summary>
        public bool HasValue => Native != null;

        /// <summary>
        /// The same state as a plain object, safe to store or to send.
        ///
        /// Monaco's own typings call the shape <c>saveViewState</c> returns "(serializable)" - it is
        /// cursor state, scroll offset and the folding contribution's state, nothing else - so it goes
        /// through <c>structuredClone</c> into IndexedDB and through <c>JSON.stringify</c> to a server
        /// unchanged. The round trip here is what guarantees that: it strips anything Monaco may have
        /// hung off it that would not clone.
        /// </summary>
        public object ToPlainObject() => Native is null ? null : MonacoEditor.ToPlainObject(Native);

        /// <summary>
        /// Rebuilds a view state from what <see cref="ToPlainObject"/> produced. Monaco reads the plain
        /// shape directly, so this only re-types it; the cast emits nothing, because nothing is emitted
        /// for an <c>[External]</c> interface.
        /// </summary>
        public static EditorViewState FromPlainObject(object value)
        {
            return value is null ? null : new EditorViewState((IViewState)value);
        }
    }
}
