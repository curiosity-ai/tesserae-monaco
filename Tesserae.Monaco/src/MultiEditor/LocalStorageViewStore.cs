using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tesserae;
using Transpose;
using Transpose.Core;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The default <see cref="IEditorViewStore"/>: the browser's <c>localStorage</c>, one key per scope
    /// holding the scope's views as a JSON array of <see cref="EditorView.ToPlainObject"/> records.
    ///
    /// <b>Why localStorage here when history uses IndexedDB.</b> Every reason history had against it is
    /// absent: a scope's views are a few hundred bytes, not a revision log of a whole document; they are
    /// read once when the shell mounts and written only when the user adds or removes something, not on a
    /// debounce while typing; there is nothing to index, prune or cursor over; and the value is a list of
    /// strings, which is what JSON is for. Synchronous access at that size costs nothing measurable, and the
    /// shell already keeps its layout there (<see cref="MultiEditor.PersistLayout"/>). <c>sessionStorage</c>
    /// is still out - it is emptied when the tab closes, and a view is exactly the kind of thing that should
    /// outlive one.
    ///
    /// Persistent is not permanent: a user agent may clear an origin's storage under pressure, and a private
    /// window forgets everything. A host that needs views to survive that puts them behind a
    /// <see cref="DelegateEditorViewStore"/> instead.
    /// </summary>
    public sealed class LocalStorageViewStore : IEditorViewStore
    {
        /// <summary>The key prefix used unless one is given: <c>tesserae-monaco-views:</c>.</summary>
        public const string DEFAULT_PREFIX = "tesserae-monaco-views:";

        private static LocalStorageViewStore _default;

        /// <summary>The shared store over <see cref="DEFAULT_PREFIX"/>.</summary>
        public static LocalStorageViewStore Default => _default ?? (_default = new LocalStorageViewStore());

        private readonly string _prefix;

        /// <param name="prefix">
        /// Prepended to the scope to form the storage key. Give a different one to keep an application's
        /// views apart from another application on the same origin; the scope already keeps users and
        /// workspaces apart within one.
        /// </param>
        public LocalStorageViewStore(string prefix = DEFAULT_PREFIX)
        {
            _prefix = string.IsNullOrEmpty(prefix) ? DEFAULT_PREFIX : prefix;
        }

        /// <summary>The key a scope's views are stored under.</summary>
        public string KeyFor(string scope) => _prefix + (scope ?? "");

        /// <summary>
        /// True when the browser exposes local storage at all. Merely touching <c>window.localStorage</c>
        /// throws in some sandboxed frames and with some privacy settings, so this probes inside a guard;
        /// when it is false every call below quietly does nothing.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                try
                {
                    return !(Script.Get<object>(window, "localStorage") is null);
                }
                catch
                {
                    return false;
                }
            }
        }

        public Task<EditorView[]> ListAsync(string scope)
        {
            return Task.FromResult(Read(scope).ToArray());
        }

        public Task SaveAsync(EditorView view)
        {
            if (view is null) return Done();

            if (string.IsNullOrEmpty(view.Id)) view.Id = EditorView.NewId();

            var views = Read(view.Scope);
            var index = views.FindIndex(v => v.Id == view.Id);

            if (index >= 0)
            {
                views[index] = view;
            }
            else
            {
                views.Add(view);
            }

            Write(view.Scope, views);

            return Done();
        }

        public Task DeleteAsync(string scope, string id)
        {
            var views   = Read(scope);
            var removed = views.RemoveAll(v => v.Id == id);

            if (removed > 0) Write(scope, views);

            return Done();
        }

        private List<EditorView> Read(string scope)
        {
            var views = new List<EditorView>();

            try
            {
                var json = window.localStorage.getItem(KeyFor(scope));

                if (string.IsNullOrEmpty(json)) return views;

                // What JSON.parse hands back is a bare array of plain objects - read by index, and each
                // element through the same cast-back FromPlainObject uses.
                var records = es5.JSON.parse(json).As<ReadOnlyArray<object>>();

                if (records is null) return views;

                for (var i = 0; i < records.Length; i++)
                {
                    var view = EditorView.FromPlainObject(records[i]);

                    if (view is object) views.Add(view);
                }
            }
            catch (Exception exception)
            {
                console.warn("Tesserae.Monaco views: could not read local storage", exception);
            }

            return views;
        }

        private void Write(string scope, List<EditorView> views)
        {
            try
            {
                var key = KeyFor(scope);

                if (views.Count == 0)
                {
                    window.localStorage.removeItem(key);

                    return;
                }

                var records = new object[views.Count];

                for (var i = 0; i < views.Count; i++)
                {
                    records[i] = views[i].ToPlainObject();
                }

                window.localStorage.setItem(key, es5.JSON.stringify(Script.ToArray(records)));
            }
            catch (Exception exception)
            {
                console.warn("Tesserae.Monaco views: could not write local storage", exception);
            }
        }

        private static Task Done() => Task.FromResult(true);
    }
}
