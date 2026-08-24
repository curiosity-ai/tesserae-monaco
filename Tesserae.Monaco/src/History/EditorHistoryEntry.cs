using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// One saved state of one document: its text, where the user was in it, and when it was taken.
    ///
    /// <b>What this is not.</b> It is not Monaco's undo stack. Monaco keeps that in its
    /// <c>UndoRedoService</c> as live objects holding closures over the model - there is no public
    /// accessor and nothing serialisable in it, so no wrapper can round-trip it through storage. What
    /// Monaco does hand out serialisably is the <i>view state</i> (<c>saveViewState</c>, documented as
    /// "(serializable)" in its own typings: caret, selections, scroll offset and folding) and the
    /// document's text. A revision log built from those two is the persistable history, and restoring
    /// one is applied as an ordinary edit - so Monaco's live undo stack still covers it, and the user
    /// can undo a restore.
    ///
    /// Every entry is scoped by <see cref="Scope"/> and <see cref="DocumentId"/> and stamped with
    /// <see cref="Timestamp"/>, which is what lets one origin hold several users', workspaces' or
    /// tabs' histories side by side without them seeing each other.
    /// </summary>
    public sealed class EditorHistoryEntry
    {
        /// <summary>
        /// The partition this entry belongs to - a user id, a workspace id, a project id, or any
        /// composite of them. Everything a store reads and writes is filtered by it, so two scopes on
        /// one origin never see each other's revisions.
        /// </summary>
        public string Scope { get; set; }

        /// <summary>
        /// The document within the scope - a file path, a model URI, or whatever the host addresses
        /// documents by. Stable across sessions is the whole point: it is what a reload looks up.
        /// </summary>
        public string DocumentId { get; set; }

        /// <summary>
        /// When this revision was taken: milliseconds since the Unix epoch, UTC.
        ///
        /// A number rather than a <c>DateTime</c> because the value crosses two boundaries that both
        /// prefer one - IndexedDB's key comparison and a server's JSON - and because a host whose
        /// server is the authority on time supplies it through
        /// <see cref="EditorHistoryOptions.Clock"/>. <see cref="TimestampUtc"/> is the same instant as
        /// a <c>DateTime</c>.
        /// </summary>
        public double Timestamp { get; set; }

        /// <summary><see cref="Timestamp"/> as a UTC <c>DateTime</c>.</summary>
        public System.DateTime TimestampUtc => EditorHistory.ToDateTime(Timestamp);

        /// <summary>The document's whole text at that moment.</summary>
        public string Text { get; set; }

        /// <summary>
        /// The caret, selections, scroll offset and folding at that moment, as a plain object - the
        /// output of <see cref="EditorViewState.ToPlainObject"/>. Null when the entry was written
        /// without a live editor.
        /// </summary>
        public object ViewState { get; set; }

        /// <summary>The Monaco language id the document was showing under.</summary>
        public string Language { get; set; }

        /// <summary>
        /// Monaco's model version id at the time. It increases on every change and never repeats
        /// within a session, so it tells two same-text revisions apart; it restarts from 1 in the next
        /// session, so it is not an ordering key across sessions - <see cref="Timestamp"/> is.
        /// </summary>
        public int VersionId { get; set; }

        /// <summary>
        /// A free-form tag for why this revision was taken - <c>"typing"</c> for the automatic
        /// snapshot, or whatever a host passes to <see cref="EditorHistory.SaveNowAsync"/>
        /// (<c>"before format"</c>, <c>"manual save"</c>, a commit id). Carried through storage
        /// untouched and never interpreted here.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The store's own id for this entry, set when it is written and null before that. The
        /// IndexedDB store fills in its auto-incremented key; a server-backed store should fill in
        /// whatever it keys by, so a later restore or delete can name the same row.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The wire and storage form: a plain object with no prototype and no Transpose bookkeeping, so
        /// it survives <c>structuredClone</c> into IndexedDB and <c>JSON.stringify</c> to a server. The
        /// field names it produces <b>are</b> the contract an external store implements against -
        /// <c>scope</c>, <c>documentId</c>, <c>docKey</c>, <c>timestamp</c>, <c>text</c>,
        /// <c>viewState</c>, <c>language</c>, <c>versionId</c>, <c>label</c>, <c>id</c>.
        /// </summary>
        public object ToPlainObject()
        {
            return new HistoryRecord
            {
                scope      = Scope,
                documentId = DocumentId,
                docKey     = HistoryRecord.DocKey(Scope, DocumentId),
                timestamp  = Timestamp,
                text       = Text ?? "",
                viewState  = ViewState,
                language   = Language,
                versionId  = VersionId,
                label      = Label,
                id         = Id
            };
        }

        /// <summary>
        /// Reads back what <see cref="ToPlainObject"/> wrote - from IndexedDB, or from a server's JSON
        /// response. Returns null for null.
        /// </summary>
        /// <param name="value">The plain object.</param>
        /// <param name="id">The store's key for the row, when it keeps one outside the value.</param>
        public static EditorHistoryEntry FromPlainObject(object value, string id = null)
        {
            if (value is null) return null;

            // A cast to an [ObjectLiteral] is a runtime check the plain object passes: such a type is
            // emitted as `$literal: true` with no constructor to test against, so the check answers
            // true for any object and the cast hands the same object straight back. Confirmed by
            // reading the emit and running it - the alternative would have been a second, [External],
            // declaration of the same shape existing only to read it.
            var record = (HistoryRecord)value;

            return new EditorHistoryEntry
            {
                Scope      = record.scope,
                DocumentId = record.documentId,
                Timestamp  = record.timestamp,
                Text       = record.text,
                ViewState  = record.viewState,
                Language   = record.language,
                VersionId  = record.versionId,
                Label      = record.label,
                Id         = id ?? record.id
            };
        }
    }

    /// <summary>
    /// What to fetch from a store. Everything but <see cref="Scope"/> is optional - a query carrying
    /// only a scope means "every revision of every document in it".
    /// </summary>
    public sealed class EditorHistoryQuery
    {
        /// <summary>The partition to read. Required; a store answers with nothing when it is missing.</summary>
        public string Scope { get; set; }

        /// <summary>One document within the scope, or null for all of them.</summary>
        public string DocumentId { get; set; }

        /// <summary>At most this many entries, or 0 for no limit.</summary>
        public int Limit { get; set; }

        /// <summary>Newest first, which is what a "recent versions" list wants. The default.</summary>
        public bool NewestFirst { get; set; } = true;

        /// <summary>Only entries stamped at or after this epoch-millisecond value, or 0 for no lower bound.</summary>
        public double Since { get; set; }

        /// <summary>Only entries stamped at or before this epoch-millisecond value, or 0 for no upper bound.</summary>
        public double Until { get; set; }

        internal bool Matches(HistoryRecord record)
        {
            if (record is null) return false;

            if (!string.IsNullOrEmpty(DocumentId) && record.documentId != DocumentId) return false;

            if (Since > 0 && record.timestamp < Since) return false;

            if (Until > 0 && record.timestamp > Until) return false;

            return true;
        }
    }

    /// <summary>
    /// The storage shape - what actually goes into IndexedDB, and what a server-backed store should
    /// expect on the wire.
    ///
    /// An <c>[ObjectLiteral]</c>, so it is emitted as a bare object rather than a class instance: a
    /// Transpose class carries a prototype that <c>structuredClone</c> refuses, which is the same trap
    /// <see cref="MonacoEditor.ToPlainObject"/> exists for on the Monaco side.
    ///
    /// <c>docKey</c> is redundant with <c>scope</c> and <c>documentId</c> and stored anyway: IndexedDB
    /// indexes one key path, and an index over the pair is what makes "this document's revisions,
    /// newest first" a cursor rather than a scan of every scope on the origin.
    /// </summary>
    [ObjectLiteral]
    internal sealed class HistoryRecord
    {
        public string scope;
        public string documentId;
        public string docKey;
        public double timestamp;
        public string text;
        public object viewState;
        public string language;
        public int    versionId;
        public string label;

        /// <summary>Set only by a store that keeps its key inside the value; the IndexedDB store does not.</summary>
        public string id;

        /// <summary>
        /// The composite key a scope/document pair is indexed under.
        ///
        /// Length-prefixed rather than joined by a separator, so there is no character to reserve, to
        /// escape, or to collide on: a scope and a document id can each contain anything at all and
        /// still produce one key per pair. ("a" + "bc" and "ab" + "c" both spell "abc"; "1:abc" and
        /// "2:abc" do not.)
        /// </summary>
        public static string DocKey(string scope, string documentId)
        {
            var partition = scope ?? "";

            return partition.Length.ToString() + ":" + partition + (documentId ?? "");
        }
    }

    /// <summary>
    /// Where the user was in a document, kept apart from the revision log on purpose.
    ///
    /// Scrolling and moving the caret happen constantly and change no text, so folding them into the
    /// revision log would rewrite the whole document every time someone merely read it. One row per
    /// scope/document, overwritten in place, is what "put me back where I was" actually needs.
    /// </summary>
    public sealed class EditorPlace
    {
        /// <summary>The partition, as on <see cref="EditorHistoryEntry.Scope"/>.</summary>
        public string Scope { get; set; }

        /// <summary>The document within the scope.</summary>
        public string DocumentId { get; set; }

        /// <summary>When this was last written: milliseconds since the Unix epoch, UTC.</summary>
        public double Timestamp { get; set; }

        /// <summary><see cref="Timestamp"/> as a UTC <c>DateTime</c>.</summary>
        public System.DateTime TimestampUtc => EditorHistory.ToDateTime(Timestamp);

        /// <summary>The plain view state, as <see cref="EditorViewState.ToPlainObject"/> produces it.</summary>
        public object ViewState { get; set; }

        /// <summary>
        /// The wire and storage form, as on <see cref="EditorHistoryEntry.ToPlainObject"/>: <c>key</c>,
        /// <c>scope</c>, <c>documentId</c>, <c>timestamp</c>, <c>viewState</c>.
        /// </summary>
        public object ToPlainObject()
        {
            return new PlaceRecord
            {
                key        = HistoryRecord.DocKey(Scope, DocumentId),
                scope      = Scope,
                documentId = DocumentId,
                timestamp  = Timestamp,
                viewState  = ViewState
            };
        }

        /// <summary>Reads back what <see cref="ToPlainObject"/> wrote. Returns null for null.</summary>
        public static EditorPlace FromPlainObject(object value)
        {
            if (value is null) return null;

            var record = (PlaceRecord)value;

            return new EditorPlace
            {
                Scope      = record.scope,
                DocumentId = record.documentId,
                Timestamp  = record.timestamp,
                ViewState  = record.viewState
            };
        }
    }

    /// <summary>The storage shape of an <see cref="EditorPlace"/>. See <see cref="HistoryRecord"/>.</summary>
    [ObjectLiteral]
    internal sealed class PlaceRecord
    {
        public string key;
        public string scope;
        public string documentId;
        public double timestamp;
        public object viewState;
    }
}
