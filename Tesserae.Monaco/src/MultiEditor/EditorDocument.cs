using System;
using System.Threading.Tasks;
using Tesserae;

namespace Tesserae.Monaco
{
    /// <summary>
    /// How a document stands - shown on its row in the tree and on its tab, so a file that fails to
    /// compile is visible without opening it.
    /// </summary>
    public enum DocumentStatus
    {
        None,
        Warning,
        Error
    }

    /// <summary>
    /// One document a <see cref="MultiEditor"/> can show: what to call it, where it sits in the tree, how
    /// to load and save its text, and how it stands. A document is a description, not an editor - the
    /// editor exists only while its tab is open, and a document can be listed in the tree without ever
    /// being opened.
    ///
    /// The text comes from <see cref="Load"/> when set, else from <see cref="Text"/>. A document with a
    /// <see cref="Save"/> is editable and tracks unsaved changes; one without is shown read-only. And a
    /// document with <see cref="Content"/> is not a code editor at all: its tab shows whatever the factory
    /// builds - a form, a viewer, an editor with its own chrome - and reports its own dirty state through
    /// <see cref="MultiEditor.MarkDirty"/>.
    /// </summary>
    public sealed class EditorDocument
    {
        /// <summary>
        /// Creates a document. <paramref name="id"/> is what tabs, the URL and every method on the
        /// <see cref="MultiEditor"/> refer to it by, so it has to be stable and unique across the catalog.
        /// </summary>
        public EditorDocument(string id, string title)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A document needs an id", nameof(id));

            Id    = id;
            Title = string.IsNullOrEmpty(title) ? id : title;
        }

        /// <summary>The stable identity - what the tree, the tabs and the URL refer to.</summary>
        public string Id { get; }

        /// <summary>The name shown on the tree row and the tab.</summary>
        public string Title { get; set; }

        /// <summary>The icon on the row and the tab; a plain file icon when unset.</summary>
        public UIcons? Icon { get; set; }

        /// <summary>
        /// Where the document sits in the tree, as slash-separated folder names - <c>"endpoints/search"</c>
        /// puts it two levels down. Null or empty lists it at the root. Folders are created as needed;
        /// give one an icon or commands with <see cref="MultiEditor.Folder"/>.
        /// </summary>
        public string Folder { get; set; }

        /// <summary>
        /// Monaco's language id (<c>"csharp"</c>, <c>"json"</c>, ...). When unset the extension of
        /// <see cref="Title"/> decides, and a title with no known extension gets plain text.
        /// </summary>
        public string Language { get; set; }

        /// <summary>Shows the editor read-only, whatever <see cref="Save"/> says.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>How the document stands. Changing it after the fact goes through <see cref="MultiEditor.SetStatus"/>.</summary>
        public DocumentStatus Status { get; set; }

        /// <summary>What is wrong, shown as the row's tooltip when <see cref="Status"/> is not <see cref="DocumentStatus.None"/>.</summary>
        public string StatusMessage { get; set; }

        /// <summary>The text, for a document that has it to hand. Ignored when <see cref="Load"/> is set.</summary>
        public string Text { get; set; }

        /// <summary>Fetches the text when the tab is first opened. A spinner stands in until it settles.</summary>
        public Func<Task<string>> Load { get; set; }

        /// <summary>
        /// Persists the text; return whether it worked. Set on a document the user may edit - without it
        /// the editor is read-only. A document with <see cref="Content"/> of its own is handed null and
        /// reads its own state.
        /// </summary>
        public Func<string, Task<bool>> Save { get; set; }

        /// <summary>
        /// Builds the tab's content instead of a code editor. Built once, when the tab opens, and kept
        /// alive while it stays open - so the component keeps its state across tab switches.
        /// </summary>
        public Func<IComponent> Content { get; set; }

        /// <summary>The entries of the row's "..." menu, built when it opens. No menu when unset.</summary>
        public Func<TreeCommand[]> Commands { get; set; }

        /// <summary>Extra words the quick-open palette and the tree filter match on, beyond the title.</summary>
        public string Keywords { get; set; }

        /// <summary>Anything the host wants to keep with the document.</summary>
        public object Tag { get; set; }

        /// <summary><see cref="Folder"/> with stray slashes removed, or an empty string for the root.</summary>
        internal string FolderPath => NormalizeFolder(Folder);

        internal static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return "";

            var parts = folder.Split('/');
            var kept  = new System.Collections.Generic.List<string>(parts.Length);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                if (trimmed.Length > 0) kept.Add(trimmed);
            }

            return string.Join("/", kept);
        }

        /// <summary>The extension of <see cref="Title"/>, dot included, or null when it has none.</summary>
        internal string Extension
        {
            get
            {
                var dot = (Title ?? "").LastIndexOf('.');

                return dot > 0 && dot < Title.Length - 1 ? Title.Substring(dot) : null;
            }
        }
    }
}
