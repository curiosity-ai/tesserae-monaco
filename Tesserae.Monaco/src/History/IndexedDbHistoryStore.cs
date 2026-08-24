using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Transpose;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The default store: the browser's <b>IndexedDB</b>.
    ///
    /// <b>Why IndexedDB and not one of the others.</b> The requirement is storage that survives a
    /// reload and a closed browser, holding whole documents written while someone types:
    ///
    /// <list type="bullet">
    /// <item><c>sessionStorage</c> is out on the first clause - it is emptied when the tab closes, and
    /// is not even shared between two tabs of the same site.</item>
    /// <item><c>localStorage</c> survives, and is wrong for the rest. It is <b>synchronous</b>, so every
    /// write blocks the main thread - the thread Monaco is laying out and tokenising on - for as long
    /// as it takes to serialise the document. It holds about 5 MB per origin, which a revision log of a
    /// large file passes quickly, and it stores strings only, so a view state costs a
    /// <c>JSON.stringify</c> each way. And it has no index: pruning by age means reading every key.</item>
    /// <item>The <b>Cache API</b> persists and is asynchronous, but it is a store of HTTP responses
    /// keyed by request. Modelling a revision list on it means inventing URLs and re-fetching to
    /// enumerate.</item>
    /// <item><b>File System Access</b> handles are real files and persist, but every one of them costs
    /// a user gesture and a permission prompt. That is right for "save as", wrong for an autosave the
    /// user never asked about.</item>
    /// <item><b>IndexedDB</b> is asynchronous, so a snapshot never blocks typing; storage is negotiated
    /// against available disk rather than a 5 MB cap; values are stored by structured clone, so the
    /// view state goes in as an object; and it has indexes and cursors, which is what makes "this
    /// document's revisions, newest first" and "delete everything older than a month" bounded rather
    /// than full scans.</item>
    /// </list>
    ///
    /// Note that <i>persistent</i> is still not <i>permanent</i> anywhere in the browser: under storage
    /// pressure a user agent may evict a whole origin, and clearing site data always does. That is the
    /// case <see cref="MirroredHistoryStore"/> exists for.
    ///
    /// <b>Two object stores</b>, because the two things being kept have completely different write
    /// rates. <c>revisions</c> holds the text snapshots, keyed by an auto-incremented number, with an
    /// index on the scope/document pair (chronological within a document, because the key generator
    /// hands them out in insertion order) and one on the scope. <c>places</c> holds one overwritten row
    /// per document with the caret and scroll, which change constantly and are worth almost nothing to
    /// keep a log of.
    ///
    /// One transaction per public method, driven by request callbacks: an IndexedDB transaction
    /// auto-commits as soon as the event loop finds it with no pending request, so awaiting anything in
    /// the middle of one closes it out from under the next call.
    /// </summary>
    public sealed class IndexedDbHistoryStore : IEditorHistoryStore
    {
        /// <summary>The database name used unless one is given: <c>tesserae-monaco-history</c>.</summary>
        public const string DEFAULT_DATABASE = "tesserae-monaco-history";

        private const string REVISIONS   = "revisions";
        private const string PLACES      = "places";
        private const string BY_DOCUMENT = "docKey";
        private const string BY_SCOPE    = "scope";
        private const int    SCHEMA      = 1;

        private static IndexedDbHistoryStore _default;

        /// <summary>
        /// The shared store over <see cref="DEFAULT_DATABASE"/>. One instance means one open database
        /// handle for the page however many editors persist their history.
        /// </summary>
        public static IndexedDbHistoryStore Default => _default ?? (_default = new IndexedDbHistoryStore());

        private readonly string _databaseName;

        private Task<IDBDatabase> _opening;

        /// <param name="databaseName">
        /// The IndexedDB database to use. Give a different one to keep an application's history apart
        /// from another application on the same origin; the scope on each entry already keeps users and
        /// workspaces apart within one.
        /// </param>
        public IndexedDbHistoryStore(string databaseName = DEFAULT_DATABASE)
        {
            _databaseName = string.IsNullOrWhiteSpace(databaseName) ? DEFAULT_DATABASE : databaseName;
        }

        /// <summary>The database this store reads and writes.</summary>
        public string DatabaseName => _databaseName;

        /// <summary>
        /// True when the browser exposes IndexedDB at all. False in a context that withholds it -
        /// notably Firefox in a private window, and some embedded webviews - where every call below
        /// quietly does nothing rather than throwing.
        /// </summary>
        public static bool IsAvailable => !(Script.Get<object>(window, "indexedDB") is null);

        #region Opening

        /// <summary>
        /// Opens the database once and hands the same task to every later caller. A failure is not
        /// cached: the field is cleared so the next call retries, which matters because the common
        /// failure here is a version-change block that clears on its own once another tab lets go.
        /// </summary>
        private Task<IDBDatabase> OpenAsync()
        {
            if (_opening is object) return _opening;

            var opened = new TaskCompletionSource<IDBDatabase>();

            _opening = opened.Task;

            if (!IsAvailable)
            {
                opened.TrySetResult(null);

                return _opening;
            }

            IDBOpenDBRequest request;

            try
            {
                request = indexedDB.open(_databaseName, SCHEMA);
            }
            catch (Exception)
            {
                _opening = null;
                opened.TrySetResult(null);

                return opened.Task;
            }

            request.onupgradeneeded = _ =>
            {
                var database = (IDBDatabase)request.result;

                if (!database.objectStoreNames.contains(REVISIONS))
                {
                    // No key path: the value is a plain record and the key generator supplies the key,
                    // which also makes the primary key an insertion counter. That is what lets a cursor
                    // over the document index run in reverse and be in newest-first order without a
                    // second index on the timestamp.
                    var revisions = database.createObjectStore(REVISIONS, new IDBObjectStoreParameters { autoIncrement = true });

                    revisions.createIndex(BY_DOCUMENT, "docKey");
                    revisions.createIndex(BY_SCOPE, "scope");
                }

                if (!database.objectStoreNames.contains(PLACES))
                {
                    database.createObjectStore(PLACES, new IDBObjectStoreParameters { keyPath = "key" });
                }
            };

            request.onsuccess = _ =>
            {
                opened.TrySetResult((IDBDatabase)request.result);
            };

            request.onerror = _ =>
            {
                _opening = null;
                opened.TrySetResult(null);
            };

            request.onblocked = _ =>
            {
                // Another tab is holding the old version open. Nothing to do but give up on this
                // attempt; clearing the field lets the next call try again once that tab has moved on.
                _opening = null;
                opened.TrySetResult(null);
            };

            return _opening;
        }

        #endregion

        #region Writing

        public async Task SaveAsync(EditorHistoryEntry entry)
        {
            if (entry is null || string.IsNullOrEmpty(entry.Scope)) return;

            var database = await OpenAsync();

            if (database is null) return;

            var stored = new TaskCompletionSource<bool>();

            try
            {
                var transaction = database.transaction(REVISIONS, IDBTransactionMode.readwrite);
                var request     = transaction.objectStore(REVISIONS).put(entry.ToPlainObject());

                request.onsuccess = _ =>
                {
                    // The generated key, so the caller can name this exact row later. A number in
                    // IndexedDB; a string on the entry, because a server-backed store's ids are not
                    // going to be numbers and one type for both is worth more than the round trip.
                    var key = request.result;

                    if (key is object) entry.Id = key.ToString();
                };

                transaction.oncomplete = _ => { stored.TrySetResult(true); };
                transaction.onerror    = _ => { stored.TrySetResult(false); };
                transaction.onabort    = _ => { stored.TrySetResult(false); };
            }
            catch (Exception)
            {
                stored.TrySetResult(false);
            }

            await stored.Task;
        }

        public async Task SavePlaceAsync(EditorPlace place)
        {
            if (place is null || string.IsNullOrEmpty(place.Scope)) return;

            var database = await OpenAsync();

            if (database is null) return;

            var stored = new TaskCompletionSource<bool>();

            try
            {
                var transaction = database.transaction(PLACES, IDBTransactionMode.readwrite);

                transaction.objectStore(PLACES).put(place.ToPlainObject());

                transaction.oncomplete = _ => { stored.TrySetResult(true); };
                transaction.onerror    = _ => { stored.TrySetResult(false); };
                transaction.onabort    = _ => { stored.TrySetResult(false); };
            }
            catch (Exception)
            {
                stored.TrySetResult(false);
            }

            await stored.Task;
        }

        #endregion

        #region Reading

        public async Task<EditorHistoryEntry[]> ListAsync(EditorHistoryQuery query)
        {
            var empty = new EditorHistoryEntry[0];

            if (query is null || string.IsNullOrEmpty(query.Scope)) return empty;

            var database = await OpenAsync();

            if (database is null) return empty;

            var found = new TaskCompletionSource<EditorHistoryEntry[]>();

            try
            {
                var transaction = database.transaction(REVISIONS, IDBTransactionMode.@readonly);
                var revisions   = transaction.objectStore(REVISIONS);

                // A query naming a document rides the document index; one naming only a scope rides the
                // scope index. Either way the cursor visits that scope's rows and nothing else, which
                // is the whole reason both indexes exist.
                var byDocument = !string.IsNullOrEmpty(query.DocumentId);
                var index      = revisions.index(byDocument ? BY_DOCUMENT : BY_SCOPE);
                var key        = byDocument ? HistoryRecord.DocKey(query.Scope, query.DocumentId) : query.Scope;
                IDBCursorDirection direction = query.NewestFirst ? (IDBCursorDirection)IDBCursorDirection.prev : IDBCursorDirection.next;
                var results    = new List<EditorHistoryEntry>();
                var request    = index.openCursor(IDBKeyRange.only(key), direction);

                request.onsuccess = _ =>
                {
                    var cursor = (IDBCursorWithValue)request.result;

                    if (cursor is null)
                    {
                        found.TrySetResult(results.ToArray());

                        return;
                    }

                    var record = (HistoryRecord)cursor.value;

                    if (query.Matches(record))
                    {
                        results.Add(EditorHistoryEntry.FromPlainObject(record, KeyOf(cursor)));
                    }

                    // The limit is applied here rather than by advancing past rows, because the
                    // timestamp bounds are checked in this callback too: a row the query rejects must
                    // not count towards it.
                    if (query.Limit > 0 && results.Count >= query.Limit)
                    {
                        found.TrySetResult(results.ToArray());

                        return;
                    }

                    cursor.@continue();
                };

                request.onerror     = _ => { found.TrySetResult(results.ToArray()); };
                transaction.onerror = _ => { found.TrySetResult(results.ToArray()); };
                transaction.onabort = _ => { found.TrySetResult(results.ToArray()); };
            }
            catch (Exception)
            {
                found.TrySetResult(empty);
            }

            return await found.Task;
        }

        public async Task<EditorHistoryEntry> GetLatestAsync(string scope, string documentId)
        {
            var entries = await ListAsync(new EditorHistoryQuery
            {
                Scope       = scope,
                DocumentId  = documentId,
                Limit       = 1,
                NewestFirst = true
            });

            return entries is object && entries.Length > 0 ? entries[0] : null;
        }

        public async Task<EditorPlace> GetPlaceAsync(string scope, string documentId)
        {
            if (string.IsNullOrEmpty(scope)) return null;

            var database = await OpenAsync();

            if (database is null) return null;

            var found = new TaskCompletionSource<EditorPlace>();

            try
            {
                var transaction = database.transaction(PLACES, IDBTransactionMode.@readonly);
                var request     = transaction.objectStore(PLACES).get(HistoryRecord.DocKey(scope, documentId));

                request.onsuccess = _ => { found.TrySetResult(EditorPlace.FromPlainObject(request.result)); };
                request.onerror   = _ => { found.TrySetResult(null); };

                transaction.onerror = _ => { found.TrySetResult(null); };
                transaction.onabort = _ => { found.TrySetResult(null); };
            }
            catch (Exception)
            {
                found.TrySetResult(null);
            }

            return await found.Task;
        }

        /// <summary>
        /// Every document that has a revision in <paramref name="scope"/>, newest first. What a "recent
        /// files" list is built from, and the reason the scope index exists alongside the document one.
        /// </summary>
        public async Task<string[]> ListDocumentsAsync(string scope)
        {
            var entries = await ListAsync(new EditorHistoryQuery { Scope = scope, NewestFirst = true });
            var seen    = new HashSet<string>();
            var ordered = new List<string>();

            foreach (var entry in entries)
            {
                if (entry.DocumentId is null || !seen.Add(entry.DocumentId)) continue;

                ordered.Add(entry.DocumentId);
            }

            return ordered.ToArray();
        }

        #endregion

        #region Deleting

        public async Task DeleteAsync(string scope, string documentId)
        {
            if (string.IsNullOrEmpty(scope)) return;

            var database = await OpenAsync();

            if (database is null) return;

            var deleted = new TaskCompletionSource<bool>();

            try
            {
                var transaction = database.transaction(Script.ToArray(new[] { REVISIONS, PLACES }), IDBTransactionMode.readwrite);
                var revisions   = transaction.objectStore(REVISIONS);
                var places      = transaction.objectStore(PLACES);
                var byDocument  = !string.IsNullOrEmpty(documentId);
                var index       = revisions.index(byDocument ? BY_DOCUMENT : BY_SCOPE);
                var key         = byDocument ? HistoryRecord.DocKey(scope, documentId) : scope;
                var request     = index.openCursor(IDBKeyRange.only(key), IDBCursorDirection.next);

                // The places rows are deleted as they are encountered rather than up front, because
                // "every document in this scope" is only knowable by walking the revisions.
                request.onsuccess = _ =>
                {
                    var cursor = (IDBCursorWithValue)request.result;

                    if (cursor is null) return;

                    var record = (HistoryRecord)cursor.value;

                    places.delete(record.docKey);
                    cursor.delete();
                    cursor.@continue();
                };

                if (byDocument) places.delete(HistoryRecord.DocKey(scope, documentId));

                transaction.oncomplete = _ => { deleted.TrySetResult(true); };
                transaction.onerror    = _ => { deleted.TrySetResult(false); };
                transaction.onabort    = _ => { deleted.TrySetResult(false); };
            }
            catch (Exception)
            {
                deleted.TrySetResult(false);
            }

            await deleted.Task;
        }

        public async Task PruneAsync(string scope, string documentId, int maxEntries, double maxAge)
        {
            if (string.IsNullOrEmpty(scope)) return;

            if (maxEntries <= 0 && maxAge <= 0) return;

            var database = await OpenAsync();

            if (database is null) return;

            var pruned = new TaskCompletionSource<bool>();

            try
            {
                var transaction = database.transaction(REVISIONS, IDBTransactionMode.readwrite);
                var revisions   = transaction.objectStore(REVISIONS);
                var byDocument  = !string.IsNullOrEmpty(documentId);
                var index       = revisions.index(byDocument ? BY_DOCUMENT : BY_SCOPE);
                var key         = byDocument ? HistoryRecord.DocKey(scope, documentId) : scope;
                var cutoff      = maxAge > 0 ? EditorHistory.Now() - maxAge : 0;
                var kept        = 0;

                // Newest first, so the count cap is "keep the first N and delete the rest" in one pass.
                // Walking oldest-first would mean counting the rows before knowing which to drop.
                var request = index.openCursor(IDBKeyRange.only(key), IDBCursorDirection.prev);

                request.onsuccess = _ =>
                {
                    var cursor = (IDBCursorWithValue)request.result;

                    if (cursor is null) return;

                    var record   = (HistoryRecord)cursor.value;
                    var tooMany  = maxEntries > 0 && kept >= maxEntries;
                    var tooOld   = cutoff > 0 && record.timestamp < cutoff;

                    if (tooMany || tooOld)
                    {
                        cursor.delete();
                    }
                    else
                    {
                        kept++;
                    }

                    cursor.@continue();
                };

                transaction.oncomplete = _ => { pruned.TrySetResult(true); };
                transaction.onerror    = _ => { pruned.TrySetResult(false); };
                transaction.onabort    = _ => { pruned.TrySetResult(false); };
            }
            catch (Exception)
            {
                pruned.TrySetResult(false);
            }

            await pruned.Task;
        }

        #endregion

        /// <summary>
        /// The cursor's primary key as a string. A cursor opened on an index carries both keys, and it
        /// is <c>primaryKey</c> - the auto-incremented number - that names the row.
        /// </summary>
        private static string KeyOf(IDBCursor cursor)
        {
            var key = cursor?.primaryKey;

            return key is null ? null : key.ToString();
        }
    }
}
