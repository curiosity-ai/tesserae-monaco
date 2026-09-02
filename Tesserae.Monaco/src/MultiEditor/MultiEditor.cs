using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tesserae;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A tabbed editor shell: a tree of documents on the left, one tab per open document on the right, and
    /// everything that has to hold between them - unsaved-changes markers and the prompt before a dirty tab
    /// closes, Ctrl+S, the open set and the active tab mirrored into the URL, the tree's folders and the
    /// split width remembered across visits, a filter over the tree, and a quick-open palette.
    ///
    /// It is composed from Tesserae rather than drawn from scratch: <see cref="SplitView"/>, <see cref="Tree"/>,
    /// <see cref="Pivot"/> (closeable, reorderable, cached tabs), <see cref="SearchBox"/>,
    /// <see cref="CommandPalette"/>, <see cref="TabSaveIndicator"/> and <see cref="UnsavedChangesGuard"/>. What
    /// this class adds is the wiring between them and the documents, which is what every hand-rolled editor
    /// shell ends up re-writing.
    ///
    /// A document opens as a <see cref="CodeEditor"/> by default, configured per document through
    /// <see cref="ConfigureEditor"/> - that is where a host attaches its providers. A document with
    /// <see cref="EditorDocument.Content"/> shows that instead, so a shell can mix code editors with forms and
    /// viewers. Hidden tabs stay mounted: switching away and back keeps the caret, the scroll offset, the undo
    /// history and the markers, and costs nothing to come back to.
    /// </summary>
    [Transpose.Name("tssm.MultiEditor")]
    public sealed class MultiEditor : IComponent
    {
        private const string TAB_KIND          = "tssm-doc";
        private const int    SEARCH_DEBOUNCE_MS = 200;
        private const int    SCROLL_SAVE_MS     = 250;

        private readonly SplitView                          _split;
        private readonly Stack                              _treePane;
        private readonly Stack                              _treeScroll;
        private readonly Tree                               _tree;
        private readonly SearchBox                          _searchBox;
        private readonly Stack                              _rightPane;
        private readonly Pivot                              _tabs;
        private readonly CommandPalette                     _palette;
        private readonly List<EditorDocument>               _catalog     = new List<EditorDocument>();
        private readonly Dictionary<string, EditorDocument> _documents   = new Dictionary<string, EditorDocument>();
        private readonly List<FolderSpec>                   _folderSpecs = new List<FolderSpec>();
        private readonly Dictionary<string, OpenTab>        _open        = new Dictionary<string, OpenTab>();
        private readonly List<string>                       _openOrder   = new List<string>();
        private readonly Dictionary<string, bool>           _expanded    = new Dictionary<string, bool>();
        private readonly Dictionary<string, Tree.Item>      _docItems    = new Dictionary<string, Tree.Item>();
        private readonly Dictionary<string, Tree.Item>      _folderItems = new Dictionary<string, Tree.Item>();
        private readonly Dictionary<Tree.Item, string>      _docByItem   = new Dictionary<Tree.Item, string>();
        private readonly List<string>                       _pendingOpen = new List<string>();

        private string                             _activeId;
        private string                             _pendingActive;
        private Func<IComponent>                   _landing;
        private Action<EditorDocument, CodeEditor> _configureEditor;
        private Action<EditorDocument>             _onActiveChanged;
        private Action<EditorDocument, CodeEditor> _onOpened;
        private Action<EditorDocument>             _onClosed;
        private Action<EditorDocument>             _onSaved;
        private Action<EditorDocument, bool>       _onDirtyChanged;
        private Func<string, Task<string[]>>       _searchProvider;
        private string                             _urlOpenKey;
        private string                             _urlActiveKey;
        private bool                               _urlRestored;
        private string                             _layoutKey;
        private bool                               _confirmClose = true;
        private bool                               _quickOpen    = true;
        private bool                               _showingTabs;
        private bool                               _mounted;
        private double                             _searchTimeout;
        private double                             _scrollTimeout;
        private int                                _searchGeneration;
        private string                             _searchTerm = "";
        private HashSet<string>                    _searchHits;

        /// <summary>
        /// Creates an empty shell. Give it documents with <see cref="Documents"/>, and size it with the
        /// usual helpers - it fills whatever it is given.
        /// </summary>
        public MultiEditor()
        {
            _tree = new Tree().Compact();

            _searchBox = SearchBox("Filter...").SearchAsYouType().OnSearch((s, term) => ApplySearch(term));

            _treeScroll = VStack().WS().H(10).Grow().ScrollY().Children(_tree);

            _treeScroll.Render().addEventListener("scroll", _ => ScheduleScrollSave());

            _treePane = VStack().S().Children(
                HStack().WS().NoShrink().P(8).AlignItemsCenter().Children(_searchBox.WS().Grow()),
                _treeScroll);

            _tabs = Pivot().S().EnableCtrlTabSwitching().Reorderable()
               .OnNavigate((s, e) => OnTabSelected(e.TargetPivot))
               .OnReorder((s, e) => OnTabsReordered(e.TabIds));

            _rightPane = VStack().S();

            _split = SplitView().S().Resizable(width => OnSplitResized(width)).LeftIsSmaller(320.px(), minLeftSize: 160.px())
               .Left(_treePane, background: Theme.Secondary.Background)
               .Right(_rightPane);

            _palette                   = new CommandPalette(_split);
            _palette.GlobalShortcutKey = "p";
            _palette.Placeholder       = "Open a document...";

            ShowLanding();

            // Ctrl+S from anywhere in the shell saves the visible document. Monaco answers the key itself
            // while the editor has focus, so a press that came from inside one is left to it.
            _split.Render().addEventListener("keydown", e => OnKeyDown(e.As<KeyboardEvent>()));

            ArmMountObserver();
        }

        /// <summary>The root element - the split view.</summary>
        public HTMLElement Render() => _split.Render();

        #region Documents and folders

        /// <summary>Every document in the catalog, in the order given.</summary>
        public IReadOnlyList<EditorDocument> Catalog => _catalog;

        /// <summary>The document with this id, or null.</summary>
        public EditorDocument GetDocument(string id) => id is object && _documents.TryGetValue(id, out var doc) ? doc : null;

        /// <summary>
        /// Replaces the catalog and rebuilds the tree. Tabs already open stay open - their document is
        /// re-bound by id, so a title or status that changed shows on the tab - and a document listed in the
        /// URL that was not in the catalog before is opened now, which is what lets a host mount the shell
        /// before its catalog has arrived from the server.
        /// </summary>
        public MultiEditor Documents(IEnumerable<EditorDocument> documents)
        {
            _catalog.Clear();
            _documents.Clear();

            foreach (var doc in documents ?? Enumerable.Empty<EditorDocument>())
            {
                if (doc is null || _documents.ContainsKey(doc.Id)) continue;

                _catalog.Add(doc);
                _documents[doc.Id] = doc;

                if (_open.TryGetValue(doc.Id, out var tab)) tab.Rebind(doc);
            }

            RebuildTree();
            RefreshPalette();
            OpenPending();
            UpdateUrl();

            return this;
        }

        /// <summary>Adds one document to the catalog, or replaces the one with the same id.</summary>
        public MultiEditor Add(EditorDocument document)
        {
            if (document is null) return this;

            var index = _catalog.FindIndex(d => d.Id == document.Id);

            if (index >= 0)
            {
                _catalog[index] = document;
            }
            else
            {
                _catalog.Add(document);
            }

            return Documents(_catalog.ToList());
        }

        /// <summary>Removes a document from the catalog. An open tab for it stays open until the user closes it.</summary>
        public MultiEditor Remove(string id)
        {
            if (_documents.ContainsKey(id)) Documents(_catalog.Where(d => d.Id != id).ToList());

            return this;
        }

        /// <summary>
        /// Describes a folder of the tree: its icon, and commands shown on its row - a "+" that creates a
        /// document in it, say. Folders documents mention are created on their own; declaring one here also
        /// makes it appear while it is still empty, and fixes its place: declared folders come first, in the
        /// order declared. <paramref name="path"/> is slash-separated, like <see cref="EditorDocument.Folder"/>.
        /// </summary>
        public MultiEditor Folder(string path, UIcons icon = UIcons.Folder, params TreeCommand[] commands)
        {
            var normalized = EditorDocument.NormalizeFolder(path);

            if (normalized.Length == 0) return this;

            _folderSpecs.RemoveAll(f => f.Path == normalized);
            _folderSpecs.Add(new FolderSpec { Path = normalized, Icon = icon, Commands = commands ?? new TreeCommand[0] });

            RebuildTree();

            return this;
        }

        /// <summary>
        /// Changes how a document stands - after a compile, say - on its row and its tab, without rebuilding
        /// anything. The message becomes the row's tooltip.
        /// </summary>
        public MultiEditor SetStatus(string id, DocumentStatus status, string message = null)
        {
            var doc = GetDocument(id);

            if (doc is null) return this;

            doc.Status        = status;
            doc.StatusMessage = message;

            if (_docItems.TryGetValue(id, out var item)) ApplyStatus(item, doc);
            if (_open.TryGetValue(id, out var tab)) tab.ApplyStatus();

            return this;
        }

        #endregion

        #region Tabs

        /// <summary>The ids of the open documents, in tab order.</summary>
        public string[] OpenDocumentIds => _openOrder.ToArray();

        /// <summary>The id of the document in front of the user, or null when no tab is open.</summary>
        public string ActiveDocumentId => _activeId;

        /// <summary>The document in front of the user, or null.</summary>
        public EditorDocument ActiveDocument => _activeId is object && _open.TryGetValue(_activeId, out var tab) ? tab.Document : null;

        /// <summary>Whether a document is open in a tab.</summary>
        public bool IsOpen(string id) => id is object && _open.ContainsKey(id);

        /// <summary>The editor showing a document, or null when it is not open or shows content of its own.</summary>
        public CodeEditor EditorOf(string id) => id is object && _open.TryGetValue(id, out var tab) ? tab.Editor : null;

        /// <summary>Opens a document of the catalog in a tab, or brings its tab to the front. Unknown ids are ignored.</summary>
        public MultiEditor Open(string id)
        {
            var doc = GetDocument(id);

            if (doc is object) Open(doc);

            return this;
        }

        /// <summary>
        /// Opens a document in a tab, whether or not the catalog lists it - a "new file" that has no
        /// identity yet, say. A document outside the catalog is not written to the URL, since nothing could
        /// re-open it from there.
        /// </summary>
        public MultiEditor Open(EditorDocument document)
        {
            if (document is null) return this;

            if (_open.TryGetValue(document.Id, out var existing))
            {
                existing.Rebind(document);
                Select(document.Id);

                return this;
            }

            var tab = new OpenTab(this, document);

            _open[document.Id] = tab;
            _openOrder.Add(document.Id);

            _tabs.Pivot(document.Id, tab.BuildTitle, tab.BuildContent, cached: true, closeable: true, onClosed: () => OnTabClosed(tab), onBeforeClose: () => CanCloseAsync(tab));

            ShowTabs();
            _tabs.Select(document.Id);
            UpdateUrl();

            return this;
        }

        /// <summary>Brings an open document's tab to the front.</summary>
        public MultiEditor Select(string id)
        {
            if (IsOpen(id)) _tabs.Select(id);

            return this;
        }

        /// <summary>
        /// Closes a document's tab. A dirty document asks first, the same way the tab's own cross does, unless
        /// <paramref name="discardChanges"/> says to drop them. Returns whether the tab closed.
        /// </summary>
        public async Task<bool> CloseAsync(string id, bool discardChanges = false)
        {
            if (!_open.TryGetValue(id, out var tab)) return true;

            if (!discardChanges && !await CanCloseAsync(tab)) return false;

            _tabs.RemoveTab(id);

            return true;
        }

        /// <summary>Closes every tab, asking about each dirty document in turn. Returns whether all of them closed.</summary>
        public async Task<bool> CloseAllAsync(bool discardChanges = false)
        {
            foreach (var id in _openOrder.ToArray())
            {
                if (!await CloseAsync(id, discardChanges)) return false;
            }

            return true;
        }

        #endregion

        #region Saving and unsaved changes

        /// <summary>Whether a document has changes not yet saved.</summary>
        public bool IsDirty(string id) => id is object && _open.TryGetValue(id, out var tab) && tab.IsDirty;

        /// <summary>Whether any open document has changes not yet saved.</summary>
        public bool HasUnsavedChanges => _open.Values.Any(t => t.IsDirty);

        /// <summary>
        /// Reports the dirty state of a document that shows <see cref="EditorDocument.Content"/> of its own -
        /// the shell cannot see inside that, so the content says. A code editor's state is tracked for it.
        /// </summary>
        public MultiEditor MarkDirty(string id, bool dirty = true)
        {
            if (id is object && _open.TryGetValue(id, out var tab)) tab.SetDirty(dirty);

            return this;
        }

        /// <summary>Saves a document through its <see cref="EditorDocument.Save"/>; the active one when no id is given. Returns whether it saved.</summary>
        public Task<bool> SaveAsync(string id = null)
        {
            id = id ?? _activeId;

            return id is object && _open.TryGetValue(id, out var tab) ? tab.SaveAsync() : Task.FromResult(false);
        }

        /// <summary>Saves every dirty document. Returns whether all of them saved.</summary>
        public async Task<bool> SaveAllAsync()
        {
            var all = true;

            foreach (var tab in _open.Values.Where(t => t.IsDirty).ToArray())
            {
                if (!await tab.SaveAsync()) all = false;
            }

            return all;
        }

        /// <summary>
        /// Whether closing a dirty tab asks first (the default). Off, a dirty tab closes and its changes go;
        /// the guard against leaving the page still holds.
        /// </summary>
        public MultiEditor ConfirmClose(bool confirm = true)
        {
            _confirmClose = confirm;

            return this;
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Runs for every code editor the shell creates, before its text arrives - the place to attach the
        /// language providers, options and keybindings a document needs. The document says which.
        /// </summary>
        public MultiEditor ConfigureEditor(Action<EditorDocument, CodeEditor> configure)
        {
            _configureEditor = configure;

            return this;
        }

        /// <summary>What the right pane shows while no tab is open. A one-line hint when unset.</summary>
        public MultiEditor Landing(Func<IComponent> landing)
        {
            _landing = landing;

            if (!_showingTabs) ShowLanding();

            return this;
        }

        /// <summary>The tree pane's starting width; a remembered width (<see cref="PersistLayout"/>) wins over it.</summary>
        public MultiEditor TreeWidth(int pixels)
        {
            _split.LeftIsSmaller(pixels.px(), minLeftSize: 160.px());

            return this;
        }

        /// <summary>The placeholder of the filter box above the tree.</summary>
        public MultiEditor FilterPlaceholder(string placeholder)
        {
            _searchBox.Placeholder = placeholder ?? "";

            return this;
        }

        /// <summary>
        /// Lets the filter box ask the host as well: the delegate is handed the term and answers with the ids
        /// of the documents that match - a search over their contents on the server, say. Its answer is
        /// added to the title match the shell does on its own. Debounced, and a late answer to an earlier
        /// term is dropped.
        /// </summary>
        public MultiEditor Search(Func<string, Task<string[]>> search)
        {
            _searchProvider = search;

            return this;
        }

        /// <summary>
        /// The quick-open palette, on Ctrl+P by default: type part of a title to open a document. Pass
        /// <c>false</c> to leave the key to the browser.
        /// </summary>
        public MultiEditor QuickOpen(bool enabled = true, string shortcutKey = "p")
        {
            _quickOpen                   = enabled;
            _palette.EnableGlobalShortcut = enabled;
            _palette.GlobalShortcutKey    = shortcutKey ?? "p";

            return this;
        }

        /// <summary>
        /// Mirrors the open tabs and the active one into the page's query string, so a refresh or a shared
        /// link lands on the same editors. The active tab is written under its own key and read back before
        /// the tabs are re-opened, since re-opening selects each in turn and would otherwise overwrite it.
        /// Only documents in the catalog are written; an id in the URL the catalog does not know is ignored.
        /// </summary>
        public MultiEditor PersistInUrl(string openKey = "open", string activeKey = "active")
        {
            _urlOpenKey   = openKey;
            _urlActiveKey = activeKey;

            if (_mounted) RestoreFromUrl();

            return this;
        }

        /// <summary>
        /// Remembers the tree's layout in the browser's local storage under <paramref name="key"/>: which
        /// folders are open, how far the tree is scrolled, and how wide the tree pane is. One key per shell,
        /// or two shells share a layout.
        /// </summary>
        public MultiEditor PersistLayout(string key)
        {
            _layoutKey = string.IsNullOrWhiteSpace(key) ? null : key;

            if (_layoutKey is object) RestoreLayout();

            return this;
        }

        #endregion

        #region Events

        /// <summary>The tab in front of the user changed; null when the last tab closed.</summary>
        public MultiEditor OnActiveChanged(Action<EditorDocument> handler)
        {
            _onActiveChanged += handler;

            return this;
        }

        /// <summary>A document's tab opened and, for a code editor, the editor exists. Null editor for content of its own.</summary>
        public MultiEditor OnOpened(Action<EditorDocument, CodeEditor> handler)
        {
            _onOpened += handler;

            return this;
        }

        /// <summary>A document's tab closed.</summary>
        public MultiEditor OnClosed(Action<EditorDocument> handler)
        {
            _onClosed += handler;

            return this;
        }

        /// <summary>A document saved successfully.</summary>
        public MultiEditor OnSaved(Action<EditorDocument> handler)
        {
            _onSaved += handler;

            return this;
        }

        /// <summary>A document became dirty, or clean again.</summary>
        public MultiEditor OnDirtyChanged(Action<EditorDocument, bool> handler)
        {
            _onDirtyChanged += handler;

            return this;
        }

        #endregion

        #region Tree

        private void RebuildTree()
        {
            var scrollTop = _treeScroll.Render().scrollTop;

            _tree.Clear();
            _docItems.Clear();
            _folderItems.Clear();
            _docByItem.Clear();

            foreach (var spec in _folderSpecs)
            {
                EnsureFolder(spec.Path);
            }

            foreach (var doc in _catalog)
            {
                if (doc.FolderPath.Length > 0) EnsureFolder(doc.FolderPath);
            }

            foreach (var doc in _catalog)
            {
                var item = MakeLeaf(doc);

                if (doc.FolderPath.Length == 0)
                {
                    _tree.Add(item);
                }
                else
                {
                    _folderItems[doc.FolderPath].Add(item);
                }
            }

            // The tree was emptied and refilled, which puts its scroll offset back to zero.
            _treeScroll.Render().scrollTop = scrollTop;
        }

        private Tree.Item EnsureFolder(string path)
        {
            if (_folderItems.TryGetValue(path, out var existing)) return existing;

            var slash      = path.LastIndexOf('/');
            var parentPath = slash > 0 ? path.Substring(0, slash) : "";
            var name       = slash > 0 ? path.Substring(slash + 1) : path;
            var spec       = _folderSpecs.FirstOrDefault(f => f.Path == path);
            var item       = new Tree.Item(name, spec is object ? spec.Icon : UIcons.Folder, spec is object ? spec.Commands : new TreeCommand[0]);

            // Top-level folders start open, deeper ones closed, and what the user did last wins over both.
            item.Expanded(_expanded.TryGetValue(path, out var expanded) ? expanded : parentPath.Length == 0);

            item.OnExpanded(_ => RememberExpanded(path, true));
            item.OnCollapsed(_ => RememberExpanded(path, false));

            if (parentPath.Length == 0)
            {
                _tree.Add(item);
            }
            else
            {
                EnsureFolder(parentPath).Add(item);
            }

            _folderItems[path] = item;

            return item;
        }

        private Tree.Item MakeLeaf(EditorDocument doc)
        {
            var commands = doc.Commands is object
                ? new[] { new TreeCommand(UIcons.MenuDots).Tooltip("More...").OnClickMenu(doc.Commands) }
                : new TreeCommand[0];

            var item = new Tree.Item(doc.Title, doc.Icon ?? UIcons.File, commands);

            item.OnClick((s, e) => Open(doc.Id));

            ApplyStatus(item, doc);

            _docItems[doc.Id] = item;
            _docByItem[item]  = doc.Id;

            return item;
        }

        private static void ApplyStatus(Tree.Item item, EditorDocument doc)
        {
            item.Icon = StatusIcon(doc);
            item.IconColor(StatusColor(doc));

            if (!string.IsNullOrEmpty(doc.StatusMessage) && doc.Status != DocumentStatus.None)
            {
                item.Tooltip(doc.StatusMessage);
            }
        }

        internal static UIcons StatusIcon(EditorDocument doc)
        {
            switch (doc.Status)
            {
                case DocumentStatus.Error:   return UIcons.Bug;
                case DocumentStatus.Warning: return UIcons.TriangleWarning;
                default:                     return doc.Icon ?? UIcons.File;
            }
        }

        internal static string StatusColor(EditorDocument doc)
        {
            switch (doc.Status)
            {
                case DocumentStatus.Error:   return Theme.Danger.Background;
                case DocumentStatus.Warning: return Theme.Colors.Orange400;
                default:                     return null;
            }
        }

        private void RememberExpanded(string path, bool expanded)
        {
            _expanded[path] = expanded;
            SaveLayout();
        }

        private void ApplySearch(string term)
        {
            _searchTerm = (term ?? "").Trim();
            _searchHits = null;

            clearTimeout(_searchTimeout);

            if (_searchTerm.Length == 0)
            {
                _tree.ClearFilter();

                return;
            }

            ApplyFilter();

            if (_searchProvider is null) return;

            var generation = ++_searchGeneration;
            var searched   = _searchTerm;

            _searchTimeout = setTimeout(_ => SearchAsync(searched, generation).FireAndForget(), SEARCH_DEBOUNCE_MS);
        }

        private async Task SearchAsync(string term, int generation)
        {
            var ids = await _searchProvider(term);

            // A slower answer to an earlier term must not overwrite the current one.
            if (generation != _searchGeneration || _searchTerm != term) return;

            _searchHits = new HashSet<string>(ids ?? new string[0]);

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var term = _searchTerm.ToLowerInvariant();
            var hits = _searchHits;

            _tree.Filter(item =>
            {
                if (!_docByItem.TryGetValue(item, out var id)) return false;

                if (hits is object && hits.Contains(id)) return true;

                var doc = GetDocument(id);

                return doc is object && Matches(doc, term);
            });
        }

        private static bool Matches(EditorDocument doc, string term)
        {
            return (doc.Title ?? "").ToLowerInvariant().Contains(term)
                || (doc.Keywords ?? "").ToLowerInvariant().Contains(term)
                || doc.FolderPath.ToLowerInvariant().Contains(term);
        }

        private void RefreshPalette()
        {
            var actions = new List<CommandPaletteAction>(_catalog.Count);

            foreach (var doc in _catalog)
            {
                var id    = doc.Id;
                var slash = doc.FolderPath.IndexOf('/');

                actions.Add(new CommandPaletteAction(id, doc.Title)
                {
                    Subtitle = doc.FolderPath.Length > 0 ? doc.FolderPath : null,
                    Section  = doc.FolderPath.Length > 0 ? (slash > 0 ? doc.FolderPath.Substring(0, slash) : doc.FolderPath) : null,
                    Keywords = doc.Keywords,
                    Icon     = StatusIcon(doc),
                    Perform  = () => Open(id)
                });
            }

            _palette.SetActions(actions);
        }

        #endregion

        #region Right pane

        private void ShowLanding()
        {
            _showingTabs = false;
            _rightPane.Clear();

            var landing = _landing?.Invoke() ?? TextBlock("Open a document from the list").Secondary().TextCenter();

            _rightPane.Add(VStack().S().AlignItemsCenter().JustifyContent(ItemJustify.Center).Children(landing));
        }

        private void ShowTabs()
        {
            if (_showingTabs) return;

            _showingTabs = true;
            _rightPane.Clear();
            _rightPane.Add(_tabs);
        }

        private void OnTabSelected(string id)
        {
            if (_activeId == id) return;

            _activeId = id;

            UpdateUrl();

            _onActiveChanged?.Invoke(ActiveDocument);
        }

        private void OnTabsReordered(string[] ids)
        {
            _openOrder.Clear();
            _openOrder.AddRange(ids.Where(id => _open.ContainsKey(id)));

            UpdateUrl();
        }

        private async Task<bool> CanCloseAsync(OpenTab tab)
        {
            if (!tab.IsDirty || !_confirmClose) return true;

            var dialog = new Dialog(
                TextBlock("Save the changes to " + tab.Document.Title + " before closing?"),
                TextBlock("Unsaved changes").SemiBold());

            var response = await dialog.YesNoCancelAsync(
                btnYes:    b => b.SetText("Save and close").Primary(),
                btnNo:     b => b.SetText("Close without saving").Danger(),
                btnCancel: b => b.SetText("Cancel"));

            switch (response)
            {
                case Dialog.Response.Yes: return await tab.SaveAsync();
                case Dialog.Response.No:  return true;
                default:                  return false;
            }
        }

        private void OnTabClosed(OpenTab tab)
        {
            var id = tab.Document.Id;

            _open.Remove(id);
            _openOrder.Remove(id);
            tab.Dispose();

            if (_openOrder.Count == 0)
            {
                _activeId = null;
                ShowLanding();
                _onActiveChanged?.Invoke(null);
            }

            UpdateUrl();

            _onClosed?.Invoke(tab.Document);
        }

        private void OnKeyDown(KeyboardEvent e)
        {
            if (!KeyboardShortcut.Matches(e, "Ctrl", "S")) return;

            // Inside Monaco the editor's own binding already ran; outside it the browser would offer to save the page.
            var target = e.target.As<HTMLElement>();

            if (target is object && target.closest(".monaco-editor") is object) return;

            e.preventDefault();

            if (_activeId is object) SaveAsync(_activeId).FireAndForget();
        }

        #endregion

        #region URL and layout persistence

        private void ArmMountObserver()
        {
            DomObserver.WhenMounted(_split.Render(), () =>
            {
                _mounted = true;

                if (_layoutKey is object) RestoreLayout();
                if (_urlOpenKey is object) RestoreFromUrl();

                UnsavedChangesGuard.TrackOpenTabs();

                DomObserver.WhenRemoved(_split.Render(), () =>
                {
                    _mounted = false;
                    UnsavedChangesGuard.ForgetOpenTabs();
                    ArmMountObserver();
                });
            });
        }

        private void RestoreFromUrl()
        {
            if (_urlRestored || _urlOpenKey is null) return;

            _urlRestored = true;

            var parameters = Router.GetQueryParameters();

            if (!parameters.TryGetValue(_urlOpenKey, out var open) || string.IsNullOrEmpty(open)) return;

            // Read before re-opening: each Select below rewrites the query string.
            parameters.TryGetValue(_urlActiveKey, out var active);

            _pendingActive = string.IsNullOrEmpty(active) ? null : active;

            foreach (var id in open.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!_pendingOpen.Contains(id)) _pendingOpen.Add(id);
            }

            OpenPending();
        }

        // Ids the URL named that the catalog has not delivered yet are kept until it does.
        private void OpenPending()
        {
            if (_pendingOpen.Count == 0) return;

            var opened = false;

            foreach (var id in _pendingOpen.ToArray())
            {
                if (!_documents.ContainsKey(id)) continue;

                _pendingOpen.Remove(id);
                Open(id);
                opened = true;
            }

            if (!opened) return;

            if (_pendingActive is object && _open.ContainsKey(_pendingActive))
            {
                Select(_pendingActive);
                _pendingActive = null;
            }
        }

        private void UpdateUrl()
        {
            if (_urlOpenKey is null || !_urlRestored) return;

            var ids      = _openOrder.Where(id => _documents.ContainsKey(id)).ToList();
            var activeId = _activeId is object && ids.Contains(_activeId) ? _activeId : null;
            var openKey  = _urlOpenKey;
            var active   = _urlActiveKey;

            Router.ReplaceQueryParameters(p =>
            {
                p = ids.Count == 0 ? p.Remove(openKey) : p.With(openKey, string.Join(",", ids));

                if (active is object)
                {
                    p = activeId is object ? p.With(active, activeId) : p.Remove(active);
                }

                return p;
            });
        }

        private void OnSplitResized(int width)
        {
            if (_layoutKey is object && width > 0) window.localStorage.setItem(_layoutKey + ":width", width.ToString());
        }

        private void ScheduleScrollSave()
        {
            if (_layoutKey is null) return;

            clearTimeout(_scrollTimeout);
            _scrollTimeout = setTimeout(_ => window.localStorage.setItem(_layoutKey + ":scroll", _treeScroll.Render().scrollTop.ToString()), SCROLL_SAVE_MS);
        }

        private void SaveLayout()
        {
            if (_layoutKey is null) return;

            var expanded  = string.Join("\n", _expanded.Where(kv => kv.Value).Select(kv => kv.Key));
            var collapsed = string.Join("\n", _expanded.Where(kv => !kv.Value).Select(kv => kv.Key));

            window.localStorage.setItem(_layoutKey + ":expanded",  expanded);
            window.localStorage.setItem(_layoutKey + ":collapsed", collapsed);
        }

        private void RestoreLayout()
        {
            var width = window.localStorage.getItem(_layoutKey + ":width");

            if (int.TryParse(width ?? "", out var pixels) && pixels >= 160) _split.LeftIsSmaller(pixels.px(), minLeftSize: 160.px());

            foreach (var path in Split(window.localStorage.getItem(_layoutKey + ":expanded")))
            {
                _expanded[path] = true;
            }

            foreach (var path in Split(window.localStorage.getItem(_layoutKey + ":collapsed")))
            {
                _expanded[path] = false;
            }

            foreach (var kv in _expanded)
            {
                if (_folderItems.TryGetValue(kv.Key, out var item)) item.Expanded(kv.Value);
            }

            var scroll = window.localStorage.getItem(_layoutKey + ":scroll");

            if (double.TryParse(scroll ?? "", out var scrollTop)) _treeScroll.Render().scrollTop = scrollTop;
        }

        private static string[] Split(string joined) => string.IsNullOrEmpty(joined) ? new string[0] : joined.Split('\n');

        #endregion

        private sealed class FolderSpec
        {
            public string        Path;
            public UIcons        Icon;
            public TreeCommand[] Commands;
        }

        /// <summary>
        /// One open tab: the document, its editor or content, and its dirty state. The title is built once
        /// and updated in place, since the pivot has no way to re-render a title.
        /// </summary>
        private sealed class OpenTab
        {
            private readonly MultiEditor _owner;
            private readonly string      _indicatorId;
            private          Icon        _titleIcon;
            private          TextBlock   _titleText;
            private          string      _savedText;
            private          bool        _dirty;
            private          bool        _disposed;

            public OpenTab(MultiEditor owner, EditorDocument document)
            {
                _owner       = owner;
                Document     = document;
                _indicatorId = TabSaveIndicator.TabId(TAB_KIND, document.Id);

                TabSaveIndicator.OnSave(_indicatorId, SaveAsync);
            }

            public EditorDocument Document { get; private set; }
            public CodeEditor     Editor   { get; private set; }
            public bool           IsDirty  => _dirty;

            public void Rebind(EditorDocument document)
            {
                Document = document;

                if (_titleText is object) _titleText.Text = document.Title;

                ApplyStatus();
            }

            public IComponent BuildTitle()
            {
                _titleIcon = Icon(StatusIcon(Document));
                _titleText = TextBlock(Document.Title).NoWrap().Ellipsis().MaxWidth(220.px());

                ApplyStatus();

                return HStack().AlignItemsCenter().Gap(6.px()).PT(6).PB(6).PL(12).PR(12).Id(_indicatorId).Children(_titleIcon, _titleText);
            }

            public void ApplyStatus()
            {
                if (_titleIcon is null) return;

                _titleIcon.SetIcon(StatusIcon(Document));
                _titleIcon.Foreground(StatusColor(Document) ?? "");
            }

            public IComponent BuildContent()
            {
                if (Document.Content is object)
                {
                    var content = Document.Content();

                    _owner._onOpened?.Invoke(Document, null);

                    return content;
                }

                return Defer(async () =>
                {
                    var text = Document.Load is object ? await Document.Load() : Document.Text;

                    // The tab may have been closed while the text was on its way.
                    if (_disposed) return Raw();

                    _savedText = text ?? "";

                    var editor = MonacoEditor.Editor()
                       .SetText(_savedText)
                       .ReadOnly(Document.ReadOnly || Document.Save is null);

                    if (!string.IsNullOrEmpty(Document.Language))
                    {
                        editor.SetLanguage(Document.Language);
                    }
                    else if (Document.Extension is object)
                    {
                        editor.SetLanguageByExtension(Document.Extension);
                    }

                    editor.OnChanged(() => SetDirty(editor.Text != _savedText));

                    if (Document.Save is object) editor.OnSave(() => SaveAsync());

                    _owner._configureEditor?.Invoke(Document, editor);

                    Editor = editor;

                    _owner._onOpened?.Invoke(Document, editor);

                    return editor.S();
                }).S();
            }

            public void SetDirty(bool dirty)
            {
                if (dirty == _dirty) return;

                _dirty = dirty;

                if (dirty)
                {
                    TabSaveIndicator.MarkDirty(_indicatorId);
                }
                else
                {
                    TabSaveIndicator.MarkClean(_indicatorId);
                }

                _owner._onDirtyChanged?.Invoke(Document, dirty);
            }

            public async Task<bool> SaveAsync()
            {
                if (Document.Save is null || _disposed) return false;

                var text  = Editor?.Text;
                var saved = await Document.Save(text);

                if (!saved) return false;

                if (Editor is object)
                {
                    _savedText = text;
                    SetDirty(Editor.Text != _savedText);
                }
                else
                {
                    SetDirty(false);
                }

                _owner._onSaved?.Invoke(Document);

                return true;
            }

            public void Dispose()
            {
                _disposed = true;

                TabSaveIndicator.Forget(_indicatorId);

                // The tab is gone for good: a plain removal would only tear the editor down until its next mount.
                Editor?.Dispose();
                Editor = null;
            }
        }
    }
}
