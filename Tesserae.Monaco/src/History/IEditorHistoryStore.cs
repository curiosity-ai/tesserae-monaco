using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Where an editor's history lives. <see cref="IndexedDbHistoryStore"/> is the one in the browser;
    /// implement this to put it somewhere else - most usefully a server.
    ///
    /// The whole interface is asynchronous because the default implementation has to be: IndexedDB is
    /// event-driven and a snapshot is a whole document, so writing one on the main thread would show up
    /// as a stutter while typing. A remote implementation is asynchronous for the obvious reason. There
    /// is deliberately no synchronous variant to fall back to.
    ///
    /// Three ways to hook an external system in, in increasing order of involvement:
    ///
    /// <list type="bullet">
    /// <item><see cref="EditorHistoryOptions.OnSaved"/> - be told about every revision as it is
    /// written, and keep the browser as the store. A few lines, and enough for "post it to an audit
    /// endpoint".</item>
    /// <item><see cref="DelegateHistoryStore"/> - a store built from lambdas, so a server-backed one is
    /// an object initialiser rather than a class.</item>
    /// <item><see cref="MirroredHistoryStore"/> - the browser and the server together: writes go to
    /// both, reads come from the browser and fall back to the server, which is what a second device
    /// with an empty IndexedDB needs.</item>
    /// </list>
    ///
    /// Every method is called with a scope; none of them should ever answer across scopes. A store that
    /// cannot do something should do nothing quietly rather than throw - a history that fails to load
    /// must not stop an editor from opening. <see cref="EditorHistoryOptions.OnError"/> is where the
    /// failures a store does raise are reported.
    /// </summary>
    public interface IEditorHistoryStore
    {
        /// <summary>
        /// Appends a revision. The entry's <see cref="EditorHistoryEntry.Id"/> is set by the store on
        /// the way out when it assigns one.
        /// </summary>
        Task SaveAsync(EditorHistoryEntry entry);

        /// <summary>The revisions matching <paramref name="query"/>. Never null; empty when there are none.</summary>
        Task<EditorHistoryEntry[]> ListAsync(EditorHistoryQuery query);

        /// <summary>
        /// The newest revision of one document, or null. Separate from <see cref="ListAsync"/> because
        /// it is what every mount does and a store can usually answer it far more cheaply.
        /// </summary>
        Task<EditorHistoryEntry> GetLatestAsync(string scope, string documentId);

        /// <summary>Writes where the user was, replacing whatever was there for this scope and document.</summary>
        Task SavePlaceAsync(EditorPlace place);

        /// <summary>Where the user last was in one document, or null.</summary>
        Task<EditorPlace> GetPlaceAsync(string scope, string documentId);

        /// <summary>
        /// Forgets one document's revisions and place, or - when <paramref name="documentId"/> is null -
        /// the whole scope.
        /// </summary>
        Task DeleteAsync(string scope, string documentId);

        /// <summary>
        /// Drops revisions past the caps. Called after each save, so a store that prunes on its own
        /// schedule (a server with a retention policy) can leave it as a no-op.
        /// </summary>
        /// <param name="scope">The partition.</param>
        /// <param name="documentId">The document, or null for every document in the scope.</param>
        /// <param name="maxEntries">Keep at most this many, newest first. 0 means no cap.</param>
        /// <param name="maxAge">Drop anything older than this many milliseconds. 0 means no cap.</param>
        Task PruneAsync(string scope, string documentId, int maxEntries, double maxAge);
    }

    /// <summary>
    /// A store assembled from lambdas - the shortest path to putting an editor's history on a server.
    ///
    /// Every hook is optional and an absent one degrades rather than fails: a missing reader answers
    /// with nothing, a missing writer discards. That is what makes a write-only mirror (fill in
    /// <see cref="Save"/> alone and let <see cref="MirroredHistoryStore"/> read from IndexedDB) a
    /// three-line object rather than a class with five methods to stub out.
    ///
    /// <code>
    /// var server = new DelegateHistoryStore
    /// {
    ///     Save        = entry =&gt; Post("/api/history", entry.ToPlainObject()),
    ///     GetLatest   = (scope, document) =&gt; GetEntry($"/api/history/latest?scope={scope}&amp;doc={document}"),
    ///     List        = query =&gt; GetEntries("/api/history", query)
    /// };
    /// </code>
    ///
    /// The lambdas are handed and expected to return this package's own types; go through
    /// <see cref="EditorHistoryEntry.ToPlainObject"/> and
    /// <see cref="EditorHistoryEntry.FromPlainObject"/> at the HTTP boundary, which is where the field
    /// names are the documented contract.
    /// </summary>
    public sealed class DelegateHistoryStore : IEditorHistoryStore
    {
        /// <summary>Called for every revision written. Absent means revisions are discarded.</summary>
        public Func<EditorHistoryEntry, Task> Save { get; set; }

        /// <summary>Answers a query. Absent means no revisions are ever found.</summary>
        public Func<EditorHistoryQuery, Task<EditorHistoryEntry[]>> List { get; set; }

        /// <summary>
        /// Answers "the newest revision of this document". Absent falls back to <see cref="List"/> with
        /// a limit of one, so a store only has to supply the cheaper one if it has it.
        /// </summary>
        public Func<string, string, Task<EditorHistoryEntry>> GetLatest { get; set; }

        /// <summary>Called for every view-state write. Absent means the place is not persisted here.</summary>
        public Func<EditorPlace, Task> SavePlace { get; set; }

        /// <summary>Answers "where was the user in this document". Absent means nowhere is known.</summary>
        public Func<string, string, Task<EditorPlace>> GetPlace { get; set; }

        /// <summary>Forgets a document, or a whole scope when the document id is null.</summary>
        public Func<string, string, Task> Delete { get; set; }

        /// <summary>
        /// Applies the retention caps. Absent is the right answer for a server that runs its own
        /// retention policy.
        /// </summary>
        public Func<string, string, int, double, Task> Prune { get; set; }

        public Task SaveAsync(EditorHistoryEntry entry)
        {
            return Save is null ? Done() : Save(entry);
        }

        public Task<EditorHistoryEntry[]> ListAsync(EditorHistoryQuery query)
        {
            if (List is null) return Task.FromResult(new EditorHistoryEntry[0]);

            return List(query);
        }

        public async Task<EditorHistoryEntry> GetLatestAsync(string scope, string documentId)
        {
            if (GetLatest is object) return await GetLatest(scope, documentId);

            if (List is null) return null;

            var entries = await List(new EditorHistoryQuery
            {
                Scope       = scope,
                DocumentId  = documentId,
                Limit       = 1,
                NewestFirst = true
            });

            return entries is object && entries.Length > 0 ? entries[0] : null;
        }

        public Task SavePlaceAsync(EditorPlace place)
        {
            return SavePlace is null ? Done() : SavePlace(place);
        }

        public Task<EditorPlace> GetPlaceAsync(string scope, string documentId)
        {
            if (GetPlace is null) return Task.FromResult<EditorPlace>(null);

            return GetPlace(scope, documentId);
        }

        public Task DeleteAsync(string scope, string documentId)
        {
            return Delete is null ? Done() : Delete(scope, documentId);
        }

        public Task PruneAsync(string scope, string documentId, int maxEntries, double maxAge)
        {
            return Prune is null ? Done() : Prune(scope, documentId, maxEntries, maxAge);
        }

        private static Task Done() => Task.FromResult(true);
    }

    /// <summary>
    /// Two stores at once: a fast local one and a durable remote one.
    ///
    /// Writes go to both. The primary is awaited and its failures propagate; the mirror is awaited too
    /// but its failures are reported through <see cref="OnMirrorError"/> rather than thrown, because a
    /// server that is down should cost an editor its backup, not its history.
    ///
    /// Reads go to the primary, and fall through to the mirror when it has nothing. That fall-through
    /// is the point rather than a detail: it is what makes a second device, a new browser profile or a
    /// cleared origin pick the document back up from the server instead of opening empty. A primary
    /// that answers is trusted - no merge, no conflict resolution, nothing that would need a rule
    /// about whose clock wins.
    /// </summary>
    public sealed class MirroredHistoryStore : IEditorHistoryStore
    {
        private readonly IEditorHistoryStore _primary;
        private readonly IEditorHistoryStore _mirror;

        /// <param name="primary">Read first and written first - normally <see cref="IndexedDbHistoryStore"/>.</param>
        /// <param name="mirror">Also written, and read when the primary has nothing - normally the server.</param>
        public MirroredHistoryStore(IEditorHistoryStore primary, IEditorHistoryStore mirror)
        {
            _primary = primary;
            _mirror  = mirror;
        }

        /// <summary>
        /// The browser's IndexedDB in front of <paramref name="remote"/> - the arrangement a
        /// server-backed history normally wants.
        /// </summary>
        public static MirroredHistoryStore LocalFirst(IEditorHistoryStore remote)
        {
            return new MirroredHistoryStore(IndexedDbHistoryStore.Default, remote);
        }

        /// <summary>
        /// Called when the mirror throws. The operation has already succeeded against the primary by
        /// then, so this is a place to report or retry, not to recover.
        /// </summary>
        public Action<Exception> OnMirrorError { get; set; }

        public async Task SaveAsync(EditorHistoryEntry entry)
        {
            if (_primary is object) await _primary.SaveAsync(entry);

            await Mirror(store => store.SaveAsync(entry));
        }

        public async Task<EditorHistoryEntry[]> ListAsync(EditorHistoryQuery query)
        {
            if (_primary is object)
            {
                var local = await _primary.ListAsync(query);

                if (local is object && local.Length > 0) return local;
            }

            if (_mirror is null) return new EditorHistoryEntry[0];

            try
            {
                return await _mirror.ListAsync(query) ?? new EditorHistoryEntry[0];
            }
            catch (Exception exception)
            {
                OnMirrorError?.Invoke(exception);

                return new EditorHistoryEntry[0];
            }
        }

        public async Task<EditorHistoryEntry> GetLatestAsync(string scope, string documentId)
        {
            if (_primary is object)
            {
                var local = await _primary.GetLatestAsync(scope, documentId);

                if (local is object) return local;
            }

            if (_mirror is null) return null;

            try
            {
                return await _mirror.GetLatestAsync(scope, documentId);
            }
            catch (Exception exception)
            {
                OnMirrorError?.Invoke(exception);

                return null;
            }
        }

        public async Task SavePlaceAsync(EditorPlace place)
        {
            if (_primary is object) await _primary.SavePlaceAsync(place);

            await Mirror(store => store.SavePlaceAsync(place));
        }

        public async Task<EditorPlace> GetPlaceAsync(string scope, string documentId)
        {
            if (_primary is object)
            {
                var local = await _primary.GetPlaceAsync(scope, documentId);

                if (local is object) return local;
            }

            if (_mirror is null) return null;

            try
            {
                return await _mirror.GetPlaceAsync(scope, documentId);
            }
            catch (Exception exception)
            {
                OnMirrorError?.Invoke(exception);

                return null;
            }
        }

        public async Task DeleteAsync(string scope, string documentId)
        {
            if (_primary is object) await _primary.DeleteAsync(scope, documentId);

            await Mirror(store => store.DeleteAsync(scope, documentId));
        }

        public async Task PruneAsync(string scope, string documentId, int maxEntries, double maxAge)
        {
            if (_primary is object) await _primary.PruneAsync(scope, documentId, maxEntries, maxAge);

            await Mirror(store => store.PruneAsync(scope, documentId, maxEntries, maxAge));
        }

        private async Task Mirror(Func<IEditorHistoryStore, Task> operation)
        {
            if (_mirror is null) return;

            try
            {
                await operation(_mirror);
            }
            catch (Exception exception)
            {
                OnMirrorError?.Invoke(exception);
            }
        }
    }
}
