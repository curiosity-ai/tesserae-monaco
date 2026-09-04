using System;
using System.Collections.Generic;
using Tesserae;
using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A named subset of a <see cref="MultiEditor"/>'s catalog: the documents and folders someone wants to
    /// see while working on one thing, out of everything the shell knows about. Picking a view filters the
    /// tree to it; "all documents" is the absence of one.
    ///
    /// A view holds ids and paths, never documents: the catalog is the host's, may arrive after the view
    /// does, and may not contain everything the view names. An id the catalog lacks is kept - the document
    /// may come back - and simply matches nothing while it is gone. A folder member is a <i>prefix</i>:
    /// every document whose <see cref="EditorDocument.Folder"/> is that path or lies beneath it belongs to
    /// the view, including documents created after the view was, which is what makes "everything under
    /// <c>endpoints/search</c>" stay true as the feature grows.
    ///
    /// Every view is partitioned by <see cref="Scope"/>, the same way an editor's history is - a user, a
    /// workspace, a project - so one origin can hold several people's views side by side.
    /// </summary>
    public sealed class EditorView
    {
        /// <summary>
        /// The store's key for this view. Empty on a view that has never been saved; the shell assigns one
        /// (<c>Guid.NewGuid().ToString("N")</c>) before the first save, so a store can rely on it.
        /// </summary>
        public string Id { get; set; }

        /// <summary>The partition this view belongs to. Filled in by the shell from <see cref="MultiEditor.Views"/>.</summary>
        public string Scope { get; set; }

        /// <summary>What the picker shows.</summary>
        public string Name { get; set; }

        /// <summary>Document ids the view lists directly.</summary>
        public List<string> Documents { get; } = new List<string>();

        /// <summary>
        /// Folder paths the view lists, normalized like <see cref="EditorDocument.Folder"/> (slash-separated,
        /// no leading or trailing slash). Each one stands for every document at or beneath it.
        /// </summary>
        public List<string> Folders { get; } = new List<string>();

        /// <summary>When the view was last saved, in milliseconds since the epoch.</summary>
        public double Timestamp { get; set; }

        /// <summary>True when the view names nothing at all.</summary>
        public bool IsEmpty => Documents.Count == 0 && Folders.Count == 0;

        /// <summary>
        /// Whether <paramref name="doc"/> belongs to the view: listed by id, or lying at or beneath a listed folder.
        /// </summary>
        public bool Contains(EditorDocument doc)
        {
            if (doc is null) return false;

            return ListsDocument(doc.Id) || ContainsFolder(doc.FolderPath);
        }

        /// <summary>
        /// Whether a document in the folder <paramref name="path"/> (normalized) would belong to the view
        /// through one of its folder members - the path itself, or a folder above it.
        /// </summary>
        public bool ContainsFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            foreach (var folder in Folders)
            {
                if (path == folder || path.StartsWith(folder + "/")) return true;
            }

            return false;
        }

        /// <summary>Whether the view lists this document id directly, as opposed to covering it through a folder.</summary>
        public bool ListsDocument(string id) => id is object && Documents.Contains(id);

        /// <summary>Whether the view lists exactly this folder path.</summary>
        public bool ListsFolder(string path) => path is object && Folders.Contains(path);

        /// <summary>Adds a document id; a no-op when it is already there.</summary>
        public EditorView AddDocument(string id)
        {
            if (!string.IsNullOrEmpty(id) && !Documents.Contains(id)) Documents.Add(id);

            return this;
        }

        /// <summary>Adds a folder path (normalized on the way in); a no-op when it is already there.</summary>
        public EditorView AddFolder(string path)
        {
            var normalized = EditorDocument.NormalizeFolder(path);

            if (normalized.Length > 0 && !Folders.Contains(normalized)) Folders.Add(normalized);

            return this;
        }

        /// <summary>Removes a document id.</summary>
        public EditorView RemoveDocument(string id)
        {
            Documents.Remove(id);

            return this;
        }

        /// <summary>Removes a folder path.</summary>
        public EditorView RemoveFolder(string path)
        {
            Folders.Remove(EditorDocument.NormalizeFolder(path));

            return this;
        }

        /// <summary>
        /// The wire and storage form: a plain object with no prototype and no Transpose bookkeeping, so it
        /// survives <c>JSON.stringify</c> to local storage or to a server. The field names it produces
        /// <b>are</b> the contract an external store implements against - <c>id</c>, <c>scope</c>,
        /// <c>name</c>, <c>documents</c>, <c>folders</c>, <c>timestamp</c>.
        ///
        /// The two lists are written through <see cref="Script.ToArray"/>: a C# array carries a
        /// <c>$type</c> stamp, and while <c>JSON.stringify</c> would ignore it, a store that hands the
        /// object to <c>structuredClone</c> (IndexedDB, <c>postMessage</c>) would not.
        /// </summary>
        public object ToPlainObject()
        {
            return new ViewRecord
            {
                id        = Id,
                scope     = Scope,
                name      = Name ?? "",
                documents = Script.ToArray(Documents.ToArray()),
                folders   = Script.ToArray(Folders.ToArray()),
                timestamp = Timestamp
            };
        }

        /// <summary>
        /// Reads back what <see cref="ToPlainObject"/> wrote - from local storage, or from a server's JSON
        /// response. Returns null for null, and for a record with no <c>id</c>.
        /// </summary>
        public static EditorView FromPlainObject(object value)
        {
            if (value is null) return null;

            // A cast to an [ObjectLiteral] is a runtime check the plain object passes: the type is emitted
            // as `$literal: true` with no constructor to test against, so the cast hands the same object
            // straight back. Same trick as EditorHistoryEntry.FromPlainObject.
            var record = (ViewRecord)value;

            if (string.IsNullOrEmpty(record.id)) return null;

            var view = new EditorView
            {
                Id        = record.id,
                Scope     = record.scope,
                Name      = record.name ?? "",
                Timestamp = record.timestamp
            };

            // The arrays are whatever JSON.parse produced - bare JavaScript arrays, with none of the
            // bookkeeping a C# array carries - so they are read by index rather than enumerated.
            var documents = record.documents;
            var folders   = record.folders;

            if (documents is object)
            {
                for (var i = 0; i < documents.Length; i++)
                {
                    view.AddDocument(documents[i]);
                }
            }

            if (folders is object)
            {
                for (var i = 0; i < folders.Length; i++)
                {
                    view.AddFolder(folders[i]);
                }
            }

            return view;
        }

        /// <summary>A fresh id for a view that has none.</summary>
        internal static string NewId() => Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// The stored shape of a view. An [ObjectLiteral] so that it is a plain object on the way out and so
    /// that a plain object read back can be cast to it; the array fields are <see cref="ReadOnlyArray{T}"/>,
    /// which is the underlying JavaScript array at runtime - what <c>JSON.parse</c> hands back.
    /// </summary>
    [ObjectLiteral]
    internal sealed class ViewRecord
    {
        public string                id;
        public string                scope;
        public string                name;
        public ReadOnlyArray<string> documents;
        public ReadOnlyArray<string> folders;
        public double                timestamp;
    }
}
