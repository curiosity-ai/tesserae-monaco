using System;
using System.Threading.Tasks;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Where a <see cref="MultiEditor"/>'s views live. <see cref="LocalStorageViewStore"/> is the one in the
    /// browser and the default; implement this to put them somewhere else - most usefully a server, so the
    /// views someone made on one machine follow them to the next, or so a team shares them.
    ///
    /// The interface is asynchronous throughout, because a server-backed implementation has to be and the
    /// shell should not care which one it has. There is deliberately no synchronous variant.
    ///
    /// Every method is called with a scope; none of them should ever answer across scopes. A store that
    /// cannot do something should do nothing quietly rather than throw - a list of views that fails to load
    /// must not stop the shell from showing its documents. <see cref="ListAsync"/> never returns null.
    /// </summary>
    public interface IEditorViewStore
    {
        /// <summary>Every view in the scope, in any order. Empty rather than null when there are none.</summary>
        Task<EditorView[]> ListAsync(string scope);

        /// <summary>
        /// Writes a view, replacing the one with the same <see cref="EditorView.Id"/> in the same scope. The
        /// shell assigns the id before calling, so a store can key on it.
        /// </summary>
        Task SaveAsync(EditorView view);

        /// <summary>Removes the view with this id from the scope. A missing view is not an error.</summary>
        Task DeleteAsync(string scope, string id);
    }

    /// <summary>
    /// A store assembled from lambdas - the shortest path to putting views on a server. Every hook is
    /// optional and an absent one degrades rather than fails: a missing reader answers with nothing, a
    /// missing writer discards.
    ///
    /// <code>
    /// shell.Views("workspace:42", new DelegateEditorViewStore
    /// {
    ///     List   = scope      =&gt; GetViews($"/api/views?scope={scope}"),
    ///     Save   = view       =&gt; Post("/api/views", view.ToPlainObject()),
    ///     Delete = (scope, id) =&gt; Delete($"/api/views/{id}?scope={scope}")
    /// });
    /// </code>
    ///
    /// The lambdas are handed and expected to return this package's own types; go through
    /// <see cref="EditorView.ToPlainObject"/> and <see cref="EditorView.FromPlainObject"/> at the HTTP
    /// boundary, which is where the field names are the documented contract. The package itself makes no
    /// HTTP call - how a host talks to its server is the host's.
    /// </summary>
    public sealed class DelegateEditorViewStore : IEditorViewStore
    {
        /// <summary>Lists the views of a scope. Absent means there are none.</summary>
        public Func<string, Task<EditorView[]>> List { get; set; }

        /// <summary>Writes one view. Absent means views are not kept.</summary>
        public Func<EditorView, Task> Save { get; set; }

        /// <summary>Removes one view, given its scope and id. Absent means nothing is removed.</summary>
        public Func<string, string, Task> Delete { get; set; }

        public async Task<EditorView[]> ListAsync(string scope)
        {
            if (List is null) return new EditorView[0];

            var views = await List(scope);

            return views ?? new EditorView[0];
        }

        public Task SaveAsync(EditorView view)
        {
            return Save is null || view is null ? Done() : Save(view);
        }

        public Task DeleteAsync(string scope, string id)
        {
            return Delete is null ? Done() : Delete(scope, id);
        }

        private static Task Done() => Task.FromResult(true);
    }
}
