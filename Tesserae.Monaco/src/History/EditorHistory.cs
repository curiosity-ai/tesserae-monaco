using System;
using System.Threading.Tasks;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;
using es5 = Transpose.Core.es5;

namespace Tesserae.Monaco
{
    /// <summary>
    /// How an editor's history is kept: where it goes, how often, how much of it, and what happens on
    /// the way back in.
    ///
    /// <see cref="Scope"/> and <see cref="DocumentId"/> are the only two that must be set. Everything
    /// else has a default that suits an editor a person types in.
    /// </summary>
    public sealed class EditorHistoryOptions
    {
        /// <summary>
        /// The partition - a user id, a workspace id, a project id, or a composite. Required: an empty
        /// scope persists nothing, on the grounds that silently pooling every user's history into one
        /// bucket is worse than not saving.
        /// </summary>
        public string Scope { get; set; }

        /// <summary>
        /// The document within the scope, and the key a reload looks the history up by, so it has to be
        /// the same string next time. A file path or a model URI is the usual answer.
        /// </summary>
        public string DocumentId { get; set; }

        /// <summary>Where it goes. Defaults to <see cref="IndexedDbHistoryStore.Default"/>.</summary>
        public IEditorHistoryStore Store { get; set; }

        /// <summary>
        /// How long typing has to stop before a revision is taken, in milliseconds. Long enough that a
        /// burst of typing is one revision rather than fifty; short enough that a tab closed in a hurry
        /// loses a sentence rather than a paragraph.
        /// </summary>
        public int SnapshotDebounceMs { get; set; } = 1_500;

        /// <summary>
        /// How long the caret and scroll have to settle before the place is written, in milliseconds.
        /// Shorter than <see cref="SnapshotDebounceMs"/> because the write is small.
        /// </summary>
        public int PlaceDebounceMs { get; set; } = 500;

        /// <summary>How many revisions of this document to keep, newest first. 0 keeps every one.</summary>
        public int MaxEntries { get; set; } = 50;

        /// <summary>
        /// How long a revision is kept, in milliseconds. Defaults to thirty days. 0 keeps them
        /// regardless of age.
        /// </summary>
        public double MaxAge { get; set; } = 30d * 24 * 60 * 60 * 1000;

        /// <summary>
        /// Whether to look the document up and put it back when the editor is created. On by default,
        /// which is what makes a reload continue where it left off; turn it off for an editor whose
        /// content the host supplies and which only wants the history recorded.
        /// </summary>
        public bool RestoreOnMount { get; set; } = true;

        /// <summary>
        /// Whether restoring also restores the caret, selections, scroll offset and folding.
        /// </summary>
        public bool RestorePlace { get; set; } = true;

        /// <summary>
        /// The last word on whether a stored revision is put back. Called with what was found, before
        /// anything is touched; answer false to keep the editor as it is.
        ///
        /// This is the hook to use when a server is also an authority on the document - compare the
        /// stored <see cref="EditorHistoryEntry.Timestamp"/> against the version the server just handed
        /// you and answer whether the local draft is still the newer one.
        /// </summary>
        public Func<EditorHistoryEntry, bool> ShouldRestore { get; set; }

        /// <summary>
        /// The clock, in milliseconds since the Unix epoch, UTC. Defaults to the browser's.
        ///
        /// Worth replacing when a server is the authority on time: a device with a wrong clock
        /// otherwise stamps revisions that sort ahead of, or behind, everything a server knows about,
        /// and a mirrored history then orders wrongly. Supplying the server's time - even as an offset
        /// applied to the local clock - keeps one ordering across every device.
        /// </summary>
        public Func<double> Clock { get; set; }

        /// <summary>
        /// Called after every revision is written. The cheapest hook for an external system: no store
        /// to implement, the browser stays the source of truth, and the callback posts what it wants
        /// where it wants. Fired after the store has accepted the entry, so
        /// <see cref="EditorHistoryEntry.Id"/> is filled in.
        /// </summary>
        public Action<EditorHistoryEntry> OnSaved { get; set; }

        /// <summary>Called after a revision has been put back into the editor.</summary>
        public Action<EditorHistoryEntry> OnRestored { get; set; }

        /// <summary>
        /// Called for anything that goes wrong in the store. Nothing here rethrows: a history that
        /// fails to save is not a reason to interrupt someone's typing, and one that fails to load is
        /// not a reason to leave the editor unopened.
        /// </summary>
        public Action<Exception> OnError { get; set; }

        internal EditorHistoryOptions Copy()
        {
            return new EditorHistoryOptions
            {
                Scope              = Scope,
                DocumentId         = DocumentId,
                Store              = Store,
                SnapshotDebounceMs = SnapshotDebounceMs,
                PlaceDebounceMs    = PlaceDebounceMs,
                MaxEntries         = MaxEntries,
                MaxAge             = MaxAge,
                RestoreOnMount     = RestoreOnMount,
                RestorePlace       = RestorePlace,
                ShouldRestore      = ShouldRestore,
                Clock              = Clock,
                OnSaved            = OnSaved,
                OnRestored         = OnRestored,
                OnError            = OnError
            };
        }
    }

    /// <summary>
    /// Records one editor's history and puts it back, against whatever
    /// <see cref="EditorHistoryOptions.Store"/> names.
    ///
    /// Attached with <c>PersistHistory(...)</c> on <see cref="CodeEditor"/> or
    /// <see cref="CodeViewer"/>; the component keeps one across a remount, so a page that detaches and
    /// re-attaches its editor does not restart the history or restore over what the user has since
    /// typed. Everything here is safe to call before the editor exists - it is buffered like the rest
    /// of the component's surface.
    ///
    /// The one asymmetry worth knowing: the automatic snapshot fires on a debounce, so the very last
    /// keystroke before a tab is closed is only saved because the recorder also flushes when the page
    /// is hidden. That is <c>visibilitychange</c> rather than <c>beforeunload</c>, which is the event
    /// a mobile browser actually delivers when an app is switched away from and never comes back.
    /// </summary>
    public sealed class EditorHistory
    {
        private const string AUTOMATIC_LABEL = "typing";

        private readonly EditorHistoryOptions _options;
        private readonly IEditorHistoryStore  _store;

        private EditorSurface _surface;
        private double        _snapshotTimeout;
        private double        _placeTimeout;
        private string        _lastSavedText;
        private bool          _restored;
        private bool          _restoring;
        private Action        _releaseVisibility;

        internal EditorHistory(EditorHistoryOptions options)
        {
            _options = (options ?? new EditorHistoryOptions()).Copy();
            _store   = _options.Store ?? IndexedDbHistoryStore.Default;
        }

        /// <summary>The options this was built with - a copy, so changing the original afterwards does nothing.</summary>
        public EditorHistoryOptions Options => _options;

        /// <summary>The store in use, whether it was given or defaulted to IndexedDB.</summary>
        public IEditorHistoryStore Store => _store;

        /// <summary>The scope every entry is filed under.</summary>
        public string Scope => _options.Scope;

        /// <summary>The document every entry is filed under.</summary>
        public string DocumentId => _options.DocumentId;

        /// <summary>True once the editor has been restored - or once it has been established there was nothing to restore.</summary>
        public bool HasRestored => _restored;

        /// <summary>Whether this history has enough to do anything: a store and a scope.</summary>
        public bool IsConfigured => _store is object && !string.IsNullOrWhiteSpace(_options.Scope);

        /// <summary>
        /// Whether an editor is attached, which is what <see cref="Restore"/> needs somewhere to put a
        /// revision - false before the first mount and again after a teardown.
        /// </summary>
        public bool IsAttached => _surface is object;

        /// <summary>
        /// The document as the editor holds it <b>now</b>, or null when none is attached - the
        /// right-hand side of any comparison against a stored revision.
        /// </summary>
        public string CurrentText => _surface is null ? null : _surface.Text;

        /// <summary>The Monaco language id the attached editor is showing, or null when none is.</summary>
        public string CurrentLanguage
        {
            get
            {
                var model = _surface is null ? null : _surface.Model;

                return model is null ? null : model.Language;
            }
        }

        #region Clock

        /// <summary>The browser's clock in milliseconds since the Unix epoch, UTC.</summary>
        public static double Now() => es5.Date.now();

        /// <summary>An epoch-millisecond value as a UTC <c>DateTime</c>.</summary>
        public static System.DateTime ToDateTime(double epochMilliseconds)
        {
            return EPOCH.AddMilliseconds(epochMilliseconds);
        }

        /// <summary>A UTC <c>DateTime</c> as an epoch-millisecond value - the inverse of <see cref="ToDateTime"/>.</summary>
        public static double ToEpochMilliseconds(System.DateTime value)
        {
            return (value - EPOCH).TotalMilliseconds;
        }

        private static readonly System.DateTime EPOCH = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

        private double Timestamp()
        {
            var clock = _options.Clock;

            return clock is null ? Now() : clock();
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Binds to a freshly created editor: subscribes to the events, and restores on the first mount
        /// only. Called by the component; a second call for the same editor is a no-op.
        /// </summary>
        internal void Attach(EditorSurface surface)
        {
            if (surface is null || _surface == surface) return;

            _surface = surface;

            if (!IsConfigured) return;

            surface.OnContentChanged(_ => ScheduleSnapshot());
            surface.OnCursorPositionChanged(_ => SchedulePlace());
            surface.OnScrollChanged(_ => SchedulePlace());

            ListenForHide();

            // Only on the first mount. A remount already brings its own text and view state back
            // through the component, and re-reading the store there would put back the last revision
            // the debounce happened to catch rather than what is actually in the editor.
            if (!_restored && _options.RestoreOnMount)
            {
                RestoreLatestAsync().FireAndForget();
            }
        }

        /// <summary>
        /// Unbinds from an editor being torn down, flushing whatever the debounce was still holding.
        /// Called by the component before the Monaco instance goes away.
        /// </summary>
        internal void Detach()
        {
            if (_surface is null) return;

            FlushAsync().FireAndForget();

            StopListeningForHide();

            _surface = null;
        }

        /// <summary>
        /// A page being hidden is the last moment a browser reliably runs anything, and on mobile it is
        /// often the only one - a tab switched away from may never see <c>beforeunload</c> or
        /// <c>unload</c> at all. Flushing here is what turns "the debounce was still pending" from lost
        /// work into a saved revision.
        /// </summary>
        private void ListenForHide()
        {
            if (_releaseVisibility is object) return;

            Action<Event> onVisibilityChange = _ =>
            {
                if (document.visibilityState == "hidden") FlushAsync().FireAndForget();
            };

            document.addEventListener("visibilitychange", onVisibilityChange);

            _releaseVisibility = () => document.removeEventListener("visibilitychange", onVisibilityChange);
        }

        private void StopListeningForHide()
        {
            _releaseVisibility?.Invoke();
            _releaseVisibility = null;
        }

        #endregion

        #region Recording

        private void ScheduleSnapshot()
        {
            // An edit this recorder just applied is not a change worth recording: it would write back
            // the revision that was restored, under a new timestamp, on every restore.
            if (_restoring || !IsConfigured) return;

            clearTimeout(_snapshotTimeout);

            _snapshotTimeout = setTimeout(_ => SaveAsync(AUTOMATIC_LABEL).FireAndForget(), _options.SnapshotDebounceMs);
        }

        private void SchedulePlace()
        {
            if (_restoring || !IsConfigured || !_options.RestorePlace) return;

            clearTimeout(_placeTimeout);

            _placeTimeout = setTimeout(_ => SavePlaceAsync().FireAndForget(), _options.PlaceDebounceMs);
        }

        /// <summary>
        /// Takes a revision now, whatever the debounce was doing, and tags it with
        /// <paramref name="label"/> - the hook for "before format", "before the server overwrote this",
        /// or a commit id. Returns what was written, or null when there was nothing to write.
        /// </summary>
        public Task<EditorHistoryEntry> SaveNowAsync(string label = null)
        {
            clearTimeout(_snapshotTimeout);

            return SaveAsync(label ?? "manual");
        }

        /// <summary>
        /// Writes anything the debounce is still holding. Called on teardown and when the page is
        /// hidden; call it directly before doing something that will lose the editor.
        ///
        /// Both payloads are read off the editor <b>before</b> either write is awaited, because
        /// <see cref="Detach"/> starts this and then drops the surface: an <c>async</c> body runs to
        /// its first <c>await</c> synchronously, so anything read after one is read from an editor
        /// that is already gone. Reading the place after awaiting the snapshot is exactly how the
        /// caret stopped being saved on teardown.
        /// </summary>
        public async Task FlushAsync()
        {
            clearTimeout(_snapshotTimeout);
            clearTimeout(_placeTimeout);

            // Both readings first, then both writes. An argument expression is evaluated where it is
            // written, so passing CapturePlace() to the second call would read it after the first
            // await - which is how the caret went on being lost on teardown even once the snapshot
            // had been fixed.
            var pending = Capture(AUTOMATIC_LABEL);
            var place   = CapturePlace();

            await WriteAsync(pending);
            await WritePlaceAsync(place);
        }

        private Task<EditorHistoryEntry> SaveAsync(string label)
        {
            return WriteAsync(Capture(label));
        }

        private Task SavePlaceAsync()
        {
            return WritePlaceAsync(CapturePlace());
        }

        /// <summary>
        /// Reads a revision off the live editor, or answers null when there is nothing worth writing.
        /// Purely synchronous, so a caller that is about to lose the editor can take the reading first
        /// and write it afterwards.
        /// </summary>
        private EditorHistoryEntry Capture(string label)
        {
            if (!IsConfigured || _surface is null) return null;

            var text = _surface.Text;

            // Unchanged text is not a revision. Without this the flush on hide, and every explicit
            // save, would add a duplicate of the newest entry each time.
            if (text == _lastSavedText) return null;

            var model = _surface.Model;

            return new EditorHistoryEntry
            {
                Scope      = _options.Scope,
                DocumentId = _options.DocumentId,
                Timestamp  = Timestamp(),
                Text       = text,
                ViewState  = CapturePlace(),
                Language   = model is null ? null : model.Language,
                VersionId  = model is null ? 0 : model.VersionId,
                Label      = label
            };
        }

        private async Task<EditorHistoryEntry> WriteAsync(EditorHistoryEntry entry)
        {
            if (entry is null) return null;

            try
            {
                await _store.SaveAsync(entry);

                _lastSavedText = entry.Text;

                _options.OnSaved?.Invoke(entry);

                await _store.PruneAsync(_options.Scope, _options.DocumentId, _options.MaxEntries, _options.MaxAge);

                return entry;
            }
            catch (Exception exception)
            {
                Report(exception);

                return null;
            }
        }

        private async Task WritePlaceAsync(object viewState)
        {
            if (!IsConfigured || viewState is null || !_options.RestorePlace) return;

            try
            {
                await _store.SavePlaceAsync(new EditorPlace
                {
                    Scope      = _options.Scope,
                    DocumentId = _options.DocumentId,
                    Timestamp  = Timestamp(),
                    ViewState  = viewState
                });
            }
            catch (Exception exception)
            {
                Report(exception);
            }
        }

        private object CapturePlace()
        {
            var state = _surface?.SaveViewState();

            return state is null ? null : state.ToPlainObject();
        }

        #endregion

        #region Reading and restoring

        /// <summary>
        /// This document's revisions, newest first. <paramref name="limit"/> of 0 means all of them.
        /// </summary>
        public Task<EditorHistoryEntry[]> ListAsync(int limit = 0)
        {
            if (!IsConfigured) return Task.FromResult(new EditorHistoryEntry[0]);

            return _store.ListAsync(new EditorHistoryQuery
            {
                Scope       = _options.Scope,
                DocumentId  = _options.DocumentId,
                Limit       = limit,
                NewestFirst = true
            });
        }

        /// <summary>The newest stored revision of this document, or null.</summary>
        public Task<EditorHistoryEntry> GetLatestAsync()
        {
            if (!IsConfigured) return Task.FromResult<EditorHistoryEntry>(null);

            return _store.GetLatestAsync(_options.Scope, _options.DocumentId);
        }

        /// <summary>Forgets this document's revisions and its place.</summary>
        public async Task ClearAsync()
        {
            if (!IsConfigured) return;

            try
            {
                await _store.DeleteAsync(_options.Scope, _options.DocumentId);

                _lastSavedText = null;
            }
            catch (Exception exception)
            {
                Report(exception);
            }
        }

        /// <summary>
        /// Puts <paramref name="entry"/> back into the editor. Returns false when there is no editor,
        /// or when its text already matches.
        ///
        /// The text goes in as an <b>edit</b> over the document's whole range rather than through
        /// <c>setValue</c>, which is what keeps Monaco's live undo stack intact: restoring a revision
        /// is then one undoable step like any other, and a mis-click is Ctrl+Z rather than a lost
        /// afternoon.
        /// </summary>
        public bool Restore(EditorHistoryEntry entry)
        {
            if (entry is null || _surface is null) return false;

            var text = entry.Text ?? "";

            _restoring = true;

            try
            {
                if (_surface.Text != text)
                {
                    var model = _surface.Model;
                    var range = model?.GetFullRange();

                    if (range is null)
                    {
                        _surface.Text = text;
                    }
                    else
                    {
                        // An undo stop on *both* sides, which is what makes the restore its own step.
                        // Without the leading one Monaco merges the replacement into whatever undo
                        // element the user's last keystrokes are still building, so one Ctrl+Z undoes
                        // the restore *and* the typing that preceded it - measured, and exactly the
                        // "undo ate my work" the edit-rather-than-setValue choice exists to avoid.
                        _surface.PushUndoStop();
                        _surface.ApplyEdits(new[] { new TextEdit { range = range, text = text } }, "tss-monaco-history");
                        _surface.PushUndoStop();
                    }
                }

                if (_options.RestorePlace && entry.ViewState is object)
                {
                    _surface.RestoreViewState(EditorViewState.FromPlainObject(entry.ViewState));
                }

                _lastSavedText = text;

                _options.OnRestored?.Invoke(entry);

                return true;
            }
            finally
            {
                _restoring = false;
            }
        }

        /// <summary>Fetches one revision by the id its store gave it, and puts it back.</summary>
        public async Task<bool> RestoreAsync(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return false;

            var entries = await ListAsync();

            foreach (var entry in entries)
            {
                if (entry.Id == entryId) return Restore(entry);
            }

            return false;
        }

        /// <summary>
        /// Looks the document up and puts the newest revision back, honouring
        /// <see cref="EditorHistoryOptions.ShouldRestore"/>. This is what
        /// <see cref="EditorHistoryOptions.RestoreOnMount"/> runs; call it by hand after loading a
        /// document the host supplied, to offer the local draft on top of it.
        /// </summary>
        public async Task<EditorHistoryEntry> RestoreLatestAsync()
        {
            if (!IsConfigured) return null;

            // Whatever is in the editor right now. If the user types while the store is answering, the
            // answer is stale and applying it would delete what they just wrote.
            var before = _surface?.Text;

            EditorHistoryEntry latest;

            try
            {
                latest = await _store.GetLatestAsync(_options.Scope, _options.DocumentId);
            }
            catch (Exception exception)
            {
                Report(exception);

                _restored = true;

                return null;
            }

            _restored = true;

            if (latest is null || _surface is null || _surface.Text != before) return null;

            var veto = _options.ShouldRestore;

            if (veto is object && !veto(latest)) return null;

            // The place is stored apart from the revisions and is newer than the newest of them
            // whenever someone has only been reading, so it wins over the one frozen into the entry.
            if (_options.RestorePlace)
            {
                try
                {
                    var place = await _store.GetPlaceAsync(_options.Scope, _options.DocumentId);

                    if (place is object && place.ViewState is object && place.Timestamp >= latest.Timestamp)
                    {
                        latest.ViewState = place.ViewState;
                    }
                }
                catch (Exception exception)
                {
                    Report(exception);
                }
            }

            return Restore(latest) ? latest : null;
        }

        #endregion

        private void Report(Exception exception)
        {
            var onError = _options.OnError;

            if (onError is null)
            {
                console.warn("Tesserae.Monaco history: " + exception.Message);

                return;
            }

            onError(exception);
        }
    }
}
