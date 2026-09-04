using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 8, Icon = UIcons.WindowRestore)]
    public class MultiEditorSample : IComponent, ISample
    {
        private readonly IComponent _content;

        // The "server" behind the shell: the text of every document, kept across visits to the page so a
        // save is visible after navigating away and back.
        private static readonly Dictionary<string, string> _store = new Dictionary<string, string>();

        private static int _untitled;

        public MultiEditorSample()
        {
            var log = TextBlock("").Small().Secondary();

            var shell = MonacoEditor.MultiEditor()
               .PersistInUrl()
               .PersistLayout("gallery:multi-editor")
               .Views("gallery:multi-editor")
               .FilterPlaceholder("Filter documents...")
               .Landing(() => VStack().AlignItemsCenter().Gap(8.px()).Children(
                    Icon(UIcons.WindowRestore, size: TextSize.Large, color: Theme.Secondary.Foreground),
                    TextBlock("Pick a document on the left, or press Ctrl+P").Secondary()))
               .ConfigureEditor((doc, editor) =>
                {
                    // Every editor the shell creates comes through here: the place for a host's providers.
                    editor.GoToDefinitionOnClickOnly()
                          .OnHover(context => Task.FromResult(context.Word is null ? null : "**" + context.Word + "** in " + doc.Title));
                })
               .Search(term => Task.FromResult(_store.Where(kv => kv.Value.ToLowerInvariant().Contains(term.ToLowerInvariant())).Select(kv => kv.Key).ToArray()))
               .OnActiveChanged(doc => log.Text = doc is null ? "no tab open" : "active: " + doc.Title)
               .OnSaved(doc => log.Text = "saved " + doc.Title)
               .OnDirtyChanged((doc, dirty) => log.Text = doc.Title + (dirty ? " has unsaved changes" : " is clean"));

            // Folders can be declared to get an icon and commands, and to appear while still empty.
            shell.Folder("endpoints", UIcons.Globe, new TreeCommand(UIcons.Plus).Tooltip("New endpoint").OnClick(() => NewDocument(shell, "endpoints", log)));
            shell.Folder("tasks",     UIcons.Clock, new TreeCommand(UIcons.Plus).Tooltip("New task").OnClick(() => NewDocument(shell, "tasks", log)));
            shell.Folder("indexes",   UIcons.Database);

            shell.Documents(BuildCatalog(shell, log));

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(MultiEditorSample), UIcons.WindowRestore, "A tree of documents, a tab per open one, and everything in between")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("MonacoEditor.MultiEditor() is the shell an application puts around its editors: a tree of documents on the left, one tab per open document on the right, unsaved-changes markers on the tabs and a prompt before a dirty one closes, Ctrl+S, the open set and the active tab in the URL, the tree's folders and width remembered across visits, a filter over the tree, Ctrl+P to open by name, and views - named subsets of the documents to show while working on one thing."),
                        TextBlock("It is composed from Tesserae - SplitView, Tree, Pivot, SearchBox, CommandPalette, TabSaveIndicator and UnsavedChangesGuard - and adds only the wiring between them and the documents. A document is a description: an id, a title, a folder, and how to load and save its text. The editor exists only while its tab is open.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Attach language providers in .ConfigureEditor((doc, editor) => ...), which runs for every editor the shell creates - the document tells you which providers it wants. A document with .Content set shows that instead of an editor, so forms and viewers can sit in tabs next to code; such a tab reports its own dirty state through .MarkDirty(id, dirty)."),
                        TextBlock("Hidden tabs stay mounted, so switching away and back keeps the caret, the scroll offset, the undo history and the markers. Call .Documents(...) again whenever the catalog changes - open tabs are re-bound by id and keep their state - and .SetStatus(id, status, message) to flag a document after a compile without rebuilding anything.").MT(8),
                        TextBlock("Mount the shell before its catalog has arrived if you like: a document the URL names is opened as soon as .Documents(...) delivers it.").MT(8),
                        TextBlock(".Views(scope) turns on views: a row above the filter picks one, and the right-click menu of any file or folder adds it to a view or takes it out. A folder in a view stands for everything under it, including documents created later. Views live in the browser's local storage by default, one list per scope; to keep them on a server instead - so they follow the user across machines, or so a team shares them - pass a store: .Views(scope, new DelegateEditorViewStore { List = s => GetViews(s), Save = v => Post(v.ToPlainObject()), Delete = (s, id) => DeleteView(s, id) }). The package makes no HTTP call of its own.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Open a few documents, edit one and watch its tab's marker, press Ctrl+S, then middle-click a tab or drag it along the strip. Type in the filter box - it also searches the documents' text. The + on a folder opens an untitled document that joins the catalog when it is saved; a document that still contains a TODO is flagged after saving. Reload the page and the same tabs come back. Right-click a file or folder and pick Add to view, New view... to start a view, then choose it in the dropdown above the filter: the tree shows only that view, the URL carries it, and Ctrl+P still opens anything. The dots button beside the dropdown renames or deletes the view."),
                        shell.WS().H(560.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Save all").SetIcon(UIcons.Disk).OnClick(() => shell.SaveAllAsync().FireAndForget()),
                            Button("Close all").OnClick(() => shell.CloseAllAsync().FireAndForget()),
                            Button("Flag people.cs").OnClick(() => shell.SetStatus("endpoints/search/people.cs", DocumentStatus.Error, "Flagged from the button")),
                            Button("Clear flag").OnClick(() => shell.SetStatus("endpoints/search/people.cs", DocumentStatus.None)),
                            log.PL(8.px())),
                        SampleHint("The open tabs and the active one are in the URL: copy it into another browser tab and the same editors open.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(SeveralDocumentsSample), typeof(NavigationSample), typeof(RemountSample));
        }

        private static List<EditorDocument> BuildCatalog(MultiEditor shell, TextBlock log)
        {
            Seed("endpoints/search/people.cs",    SampleCode.Navigable);
            Seed("endpoints/search/documents.cs", SampleCode.Order);
            Seed("endpoints/upload.cs",           "// TODO: validate the file size before accepting it\npublic class Upload\n{\n    public string Path { get; set; }\n}\n");
            Seed("tasks/nightly-import.cs",       "public class NightlyImport\n{\n    public int BatchSize = 500;\n}\n");
            Seed("indexes/people.json",           "{\n  \"name\": \"people\",\n  \"fields\": [ \"name\", \"email\" ]\n}\n");
            Seed("readme.md",                     "# Sample workspace\n\nEvery document here lives in memory; a save keeps it until the page is reloaded.\n");

            var docs = new List<EditorDocument>();

            foreach (var id in _store.Keys.ToArray())
            {
                docs.Add(Describe(shell, id, log));
            }

            // A tab that is not a code editor: a form, reporting its own dirty state.
            docs.Add(new EditorDocument("settings", "Settings")
            {
                Icon    = UIcons.Settings,
                Content = () => SettingsForm(shell, log),
                Save    = _ =>
                {
                    log.Text = "settings saved";

                    return Task.FromResult(true);
                }
            });

            return docs;
        }

        private static EditorDocument Describe(MultiEditor shell, string id, TextBlock log)
        {
            var slash    = id.LastIndexOf('/');
            var title    = slash >= 0 ? id.Substring(slash + 1) : id;
            var folder   = slash >= 0 ? id.Substring(0, slash) : null;
            var hasTodo  = _store[id].Contains("TODO");

            return new EditorDocument(id, title)
            {
                Folder        = folder,
                Icon          = title.EndsWith(".json") ? UIcons.BracketsCurly : title.EndsWith(".md") ? UIcons.Document : UIcons.FileCode,
                Status        = hasTodo ? DocumentStatus.Error : DocumentStatus.None,
                StatusMessage = hasTodo ? "Contains a TODO" : null,
                Load          = () => Task.FromResult(_store[id]),
                Save          = text => SaveAsync(shell, id, text, log),
                Commands      = () => new[]
                {
                    new TreeCommand(UIcons.Copy).SetText("Duplicate").OnClick(() =>
                    {
                        var copy = id.Replace(title, "copy-of-" + title);

                        Seed(copy, _store[id]);
                        shell.Add(Describe(shell, copy, log));
                        shell.Open(copy);
                    }),
                    new TreeCommand(UIcons.Trash).SetText("Delete").Danger().OnClick(() =>
                    {
                        _store.Remove(id);
                        shell.Remove(id);
                        shell.CloseAsync(id, discardChanges: true).FireAndForget();
                    })
                }
            };
        }

        private static async Task<bool> SaveAsync(MultiEditor shell, string id, string text, TextBlock log)
        {
            await Task.Delay(150); // a round trip

            _store[id] = text ?? "";

            var hasTodo = _store[id].Contains("TODO");

            shell.SetStatus(id, hasTodo ? DocumentStatus.Error : DocumentStatus.None, hasTodo ? "Contains a TODO" : null);

            return true;
        }

        private static void NewDocument(MultiEditor shell, string folder, TextBlock log)
        {
            _untitled++;

            var id    = folder + "/untitled-" + _untitled + ".cs";
            var title = "untitled-" + _untitled + ".cs";

            // Not in the catalog until saved - so not in the tree, and not in the URL.
            shell.Open(new EditorDocument(id, title)
            {
                Folder = folder,
                Icon   = UIcons.FileCode,
                Text   = "// " + title + "\n",
                Save   = async text =>
                {
                    Seed(id, text ?? "");
                    shell.Add(Describe(shell, id, log));

                    return await SaveAsync(shell, id, text, log);
                }
            });
        }

        private static IComponent SettingsForm(MultiEditor shell, TextBlock log)
        {
            var name = TextBox("sample workspace").WS();

            name.OnInput((s, e) => shell.MarkDirty("settings", true));

            return VStack().S().P(16).Gap(8.px()).Children(
                TextBlock("Workspace name").SemiBold(),
                name,
                TextBlock("This tab is a form rather than an editor: the document has .Content, and reports edits with .MarkDirty. Ctrl+S saves it through the same .Save delegate.").Small().Secondary(),
                Button("Save").Primary().OnClick(() => shell.SaveAsync("settings").FireAndForget()));
        }

        private static void Seed(string id, string text)
        {
            if (!_store.ContainsKey(id)) _store[id] = text;
        }

        public HTMLElement Render() => _content.Render();
    }
}
