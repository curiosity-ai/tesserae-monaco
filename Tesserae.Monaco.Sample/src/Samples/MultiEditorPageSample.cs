using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// The MultiEditor as a whole page, the way Mosaik's Build view hosts it: the shell fills the page,
    /// the filter button sits beside the filter box (FilterActions) and the loading state under it
    /// (TreeHeader). The catalog is
    /// shaped like Mosaik's too - category folders with a "+" and a "..." that are always showing, nested
    /// endpoint paths, compile errors as document status, a built-in section behind a filter, a landing
    /// pane while nothing is open - so a change to the shell's rendering can be checked here against the
    /// layout it actually ships in, rather than inside a card on a scrolling page.
    /// </summary>
    [SampleDetails(Group = "Runtime and hosting", Order = 9, Icon = UIcons.SiteBrowser)]
    public class MultiEditorPageSample : IComponent, ISample
    {
        private const string STYLE_ID   = "tssm-sample-build-styles";
        private const string SHELL_CLASS = "tssm-sample-build";

        private const string FOLDER_ENDPOINTS    = "Endpoints";
        private const string FOLDER_AI_TOOLS     = "AI Tools";
        private const string FOLDER_PROMPTS      = "System Prompts";
        private const string FOLDER_AGENTS       = "Agents";
        private const string FOLDER_CODE_TASKS   = "Code Tasks";
        private const string FOLDER_CODE_INDEXES = "Code Indexes";
        private const string FOLDER_MIGRATIONS   = "Migrations";
        private const string FOLDER_SKILLS       = "Skills";
        private const string FOLDER_BUILT_IN     = "Built-in AI Tools";

        // The "server": every item's text, kept across visits so a save survives navigating away.
        private static readonly Dictionary<string, CatalogItem> _store = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);

        private static int _created;

        private readonly MultiEditor              _shell;
        private readonly Stack                    _content;
        private readonly TextBlock                _loading = TextBlock().Small().Secondary().NoWrap().PB(4).Collapse();
        private readonly SettableObservable<bool> _showBuiltIn = new SettableObservable<bool>(false);
        private readonly SettableObservable<bool> _onlyFailing = new SettableObservable<bool>(false);

        public MultiEditorPageSample()
        {
            EnsureStyles();
            Seed();

            _shell = MonacoEditor.MultiEditor()
               .Class(SHELL_CLASS)
               .FilterPlaceholder("Search across endpoints, tools and other items...")
               .FilterActions(BuildFilterButton())
               .TreeHeader(_loading)
               .Search(SearchAsync)
               .PersistInUrl("open", "active", viewKey: null)
               .PersistLayout("gallery:multi-editor-page")
               .TreeWidth(360)
               .Landing(BuildLanding)
               .ConfigureEditor((doc, editor) => editor.OnHover(context => Task.FromResult(context.Word is null ? null : "**" + context.Word + "** in " + doc.Title)));

            DeclareFolders();

            _showBuiltIn.ObserveFutureChanges(_ => RebuildDocuments());
            _onlyFailing.ObserveFutureChanges(_ => RebuildDocuments());

            _content = VStack().S().Children(_shell.S());

            _content.WhenMounted(() => LoadAsync().FireAndForget());
        }

        public HTMLElement Render() => _content.Render();

        // Beside the filter box, the row whose results it narrows, rather than in a toolbar of its own.
        private IComponent BuildFilterButton()
        {
            var filterButton = Button(UIcons.Filter).Tooltip("Filter items");

            Action refresh = () => filterButton.SetIcon(UIcons.Filter, _showBuiltIn.Value || _onlyFailing.Value ? Theme.Primary.Background : null);

            _showBuiltIn.ObserveFutureChanges(_ => refresh());
            _onlyFailing.ObserveFutureChanges(_ => refresh());

            filterButton.OnClick(() => Tippy.ShowFor(filterButton, BuildFilterContent(), out var _, TooltipAnimation.ShiftAway, TooltipPlacement.BottomEnd, delayHide: 200, maxWidth: 320));

            return filterButton;
        }

        private IComponent BuildFilterContent()
        {
            var builtIn = CheckBox("Show built-in features").Checked(_showBuiltIn.Value);
            builtIn.AsObservable().Observe(v => _showBuiltIn.Value = v);

            var failing = CheckBox("Only failing compilation").Checked(_onlyFailing.Value);
            failing.AsObservable().Observe(v => _onlyFailing.Value = v);

            return VStack().WS().P(8).Children(builtIn, failing);
        }

        // What the right pane shows while no tab is open: one card per category, the place Mosaik puts
        // its usage metrics and recent errors.
        private IComponent BuildLanding()
        {
            var cards = HStack().WS().Wrap().Gap(12.px());

            foreach (var group in _store.Values.Where(i => !i.BuiltIn).GroupBy(i => i.Category))
            {
                var failing = group.Count(i => i.Text.Contains("TODO"));

                cards.Add(Card(VStack().Gap(4.px()).Children(
                    HStack().AlignItemsCenter().Gap(8.px()).Children(Icon(CategoryIcon(group.Key)), TextBlock(group.Key).SemiBold()),
                    TextBlock(group.Count() + (group.Count() == 1 ? " item" : " items")).Small().Secondary(),
                    failing > 0 ? (IComponent)TextBlock(failing + " failing").Small().Foreground(Theme.Danger.Background) : TextBlock("all compile").Small().Secondary())).W(220.px()));
            }

            return VStack().S().ScrollY().P(24).Gap(12.px()).Children(
                TextBlock("Build").MediumPlus().SemiBold(),
                TextBlock("Pick an item from the tree, or press Ctrl+P to open one by name.").Secondary(),
                cards);
        }

        private async Task LoadAsync()
        {
            _loading.Text = "Loading Build catalog...";
            _loading.Show();

            // A round trip, so the shell mounts before its catalog has arrived - the open set in the URL is
            // held until Documents(...) delivers it, which is how Mosaik's view loads.
            await Task.Delay(400);

            RebuildDocuments();

            _loading.FadeThenCollapse();
        }

        private void RebuildDocuments()
        {
            if (_showBuiltIn.Value) _shell.Folder(FOLDER_BUILT_IN, UIcons.Tools);

            _shell.Documents(_store.Values
               .Where(i => _showBuiltIn.Value || !i.BuiltIn)
               .Where(i => !_onlyFailing.Value || i.Text.Contains("TODO"))
               .OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
               .Select(Describe)
               .ToList());
        }

        private void DeclareFolders()
        {
            foreach (var category in new[] { FOLDER_ENDPOINTS, FOLDER_AI_TOOLS, FOLDER_PROMPTS, FOLDER_AGENTS, FOLDER_CODE_TASKS, FOLDER_CODE_INDEXES, FOLDER_MIGRATIONS, FOLDER_SKILLS })
            {
                var captured = category;

                _shell.Folder(category, CategoryIcon(category),
                    new TreeCommand(UIcons.Plus).Tooltip("Create in " + category).OnClick(() => Create(captured)),
                    new TreeCommand(UIcons.MenuDots).Tooltip("More...").OnClickMenu(() => new[]
                    {
                        new TreeCommand(UIcons.FileExport).SetText("Export " + captured.ToLowerInvariant()).OnClick(() => Toast().Information("Export", "Would export every item in " + captured)),
                        new TreeCommand(UIcons.FileImport).SetText("Import " + captured.ToLowerInvariant()).OnClick(() => Toast().Information("Import", "Would import into " + captured))
                    }));
            }
        }

        private EditorDocument Describe(CatalogItem item)
        {
            var path     = item.Path;
            var slash    = path.LastIndexOf('/');
            var title    = path.Substring(slash + 1);
            var failing  = item.Text.Contains("TODO");
            var language = item.Category == FOLDER_PROMPTS || item.Category == FOLDER_SKILLS ? "markdown" : "csharp";

            var doc = new EditorDocument(path, title)
            {
                Folder        = path.Substring(0, slash),
                Icon          = CategoryIcon(item.Category),
                Language      = language,
                ReadOnly      = item.BuiltIn,
                Status        = failing ? DocumentStatus.Error : DocumentStatus.None,
                StatusMessage = failing ? "This code does not compile" : null,
                Keywords      = item.Category,
                Load          = () => Task.FromResult(item.Text),
                Save          = text => SaveAsync(item, text),
                Commands      = item.BuiltIn ? (Func<TreeCommand[]>)null : () => Menu(item)
            };

            // An agent opens as a form, the way Mosaik embeds an item's edit modal in its tab.
            if (item.Category == FOLDER_AGENTS) doc.Content = () => AgentForm(item);

            return doc;
        }

        private TreeCommand[] Menu(CatalogItem item)
        {
            var commands = new List<TreeCommand>();

            if (item.Category == FOLDER_CODE_TASKS || item.Category == FOLDER_MIGRATIONS)
            {
                commands.Add(new TreeCommand(UIcons.Play).SetText("Run now").OnClick(() => Toast().Success("Started", item.Path)));
            }

            commands.Add(new TreeCommand(UIcons.Trash).SetText("Delete").Danger().OnClick(() =>
            {
                _store.Remove(item.Path);
                _shell.CloseAsync(item.Path, discardChanges: true).FireAndForget();
                RebuildDocuments();
            }));

            return commands.ToArray();
        }

        private async Task<bool> SaveAsync(CatalogItem item, string text)
        {
            await Task.Delay(150);

            item.Text = text ?? "";

            var failing = item.Text.Contains("TODO");

            _shell.SetStatus(item.Path, failing ? DocumentStatus.Error : DocumentStatus.None, failing ? "This code does not compile" : null);

            return true;
        }

        private void Create(string category)
        {
            _created++;

            var name = category == FOLDER_PROMPTS || category == FOLDER_SKILLS ? "new-" + _created + ".md" : "New" + _created + ".cs";
            var item = new CatalogItem(category, category + "/" + name, category == FOLDER_PROMPTS || category == FOLDER_SKILLS ? "# New\n" : "public class New" + _created + "\n{\n}\n");

            _store[item.Path] = item;
            RebuildDocuments();
            _shell.Open(item.Path);
        }

        private IComponent AgentForm(CatalogItem item)
        {
            var name         = TextBox(item.Path.Substring(item.Path.LastIndexOf('/') + 1)).WS();
            var instructions = TextArea(item.Text).WS().H(160.px());

            name.OnInput((s, e) => _shell.MarkDirty(item.Path, true));
            instructions.OnInput((s, e) => _shell.MarkDirty(item.Path, true));

            return VStack().S().ScrollY().P(16).Gap(8.px()).Children(
                TextBlock("Name").SemiBold(),
                name,
                TextBlock("Instructions").SemiBold().PT(8),
                instructions,
                HStack().Gap(8.px()).PT(8).Children(
                    Button("Save").Primary().OnClick(() => _shell.SaveAsync(item.Path).FireAndForget()),
                    Button("Run").SetIcon(UIcons.Play)));
        }

        private Task<string[]> SearchAsync(string term)
        {
            var lower = term.ToLowerInvariant();

            return Task.FromResult(_store.Values.Where(i => i.Text.ToLowerInvariant().Contains(lower)).Select(i => i.Path).ToArray());
        }

        private static UIcons CategoryIcon(string category)
        {
            switch (category)
            {
                case FOLDER_ENDPOINTS:    return UIcons.Link;
                case FOLDER_AI_TOOLS:     return UIcons.Tools;
                case FOLDER_PROMPTS:      return UIcons.StoryBook;
                case FOLDER_AGENTS:       return UIcons.UserRobot;
                case FOLDER_CODE_TASKS:   return UIcons.PriorityArrow;
                case FOLDER_CODE_INDEXES: return UIcons.Api;
                case FOLDER_MIGRATIONS:   return UIcons.ArrowsRepeat;
                case FOLDER_SKILLS:       return UIcons.GraduationCap;
                default:                  return UIcons.Tools;
            }
        }

        private static void Seed()
        {
            if (_store.Count > 0) return;

            Add(FOLDER_ENDPOINTS, "search/people", Endpoint("SearchPeople", "return await Q().StartAt(\"Person\").Take(20).EmitAsync();"));
            Add(FOLDER_ENDPOINTS, "search/documents", Endpoint("SearchDocuments", "return await Q().StartAt(\"Document\").Take(20).EmitAsync();"));
            Add(FOLDER_ENDPOINTS, "search/facets/by-author", Endpoint("FacetsByAuthor", "return Body;"));
            Add(FOLDER_ENDPOINTS, "upload", Endpoint("Upload", "// TODO: validate the file size before accepting it\n        return Body;"));
            Add(FOLDER_ENDPOINTS, "health", Endpoint("Health", "return \"ok\";"));
            Add(FOLDER_AI_TOOLS, "Summarize document", Tool("SummarizeDocument"));
            Add(FOLDER_AI_TOOLS, "Find similar", Tool("FindSimilar"));
            Add(FOLDER_AI_TOOLS, "Translate", "// TODO: pick the model from the workspace settings\npublic class Translate\n{\n}\n");
            Add(FOLDER_PROMPTS, "Default assistant", "# Default assistant\n\nYou are a helpful assistant for this workspace.\n");
            Add(FOLDER_PROMPTS, "Support triage", "# Support triage\n\nClassify the ticket and suggest the team that owns it.\n");
            Add(FOLDER_AGENTS, "Researcher", "Read the documents the user names and answer with citations.");
            Add(FOLDER_AGENTS, "Release notes writer", "Summarise the merged pull requests of the week.");
            Add(FOLDER_CODE_TASKS, "Nightly import", "public class NightlyImport\n{\n    public int BatchSize = 500;\n}\n");
            Add(FOLDER_CODE_TASKS, "Weekly digest", "public class WeeklyDigest\n{\n}\n");
            Add(FOLDER_CODE_INDEXES, "Index for Person (name)", "public class PersonNameIndex\n{\n}\n");
            Add(FOLDER_MIGRATIONS, "Rename status field", "public class RenameStatus\n{\n}\n");
            Add(FOLDER_SKILLS, "Writing queries", "# Writing queries\n\nStart at a node type, then traverse edges.\n");
            Add(FOLDER_BUILT_IN, "Search", Tool("Search"), builtIn: true);
            Add(FOLDER_BUILT_IN, "Consult", Tool("Consult"), builtIn: true);

            // Enough endpoints that the tree scrolls, as a real workspace's does.
            for (var i = 1; i <= 24; i++)
            {
                Add(FOLDER_ENDPOINTS, "reports/report-" + i.ToString("00"), Endpoint("Report" + i, "return " + i + ";"));
            }
        }

        private static void Add(string category, string name, string text, bool builtIn = false)
        {
            var item = new CatalogItem(category, category + "/" + name, text) { BuiltIn = builtIn };

            _store[item.Path] = item;
        }

        private static string Endpoint(string name, string body) => "// " + name + "\npublic class " + name + "\n{\n    public async Task<object> RunAsync()\n    {\n        " + body + "\n    }\n}\n";

        private static string Tool(string name) => "public class " + name + "\n{\n    public string Describe() => \"" + name + "\";\n}\n";

        private static void EnsureStyles()
        {
            if (document.getElementById(STYLE_ID) != null) return;

            // Mosaik's msk.build-tree.css, so the tree renders the way it does there: the category folders
            // are bolder and keep their commands showing.
            var style = document.createElement("style");

            style.id          = STYLE_ID;
            style.textContent =
                "." + SHELL_CLASS + " .tss-tree-icon { font-size: 12px !important; }" +
                "." + SHELL_CLASS + " .tss-tree > .tss-tree-item > .tss-tree-item-content > .tss-tree-text { font-weight: var(--tss-font-weight-semibold) !important; }" +
                "." + SHELL_CLASS + " .tss-tree > .tss-tree-item > .tss-tree-item-content > .tss-tree-commands { display: flex !important; }";

            document.head.appendChild(style);
        }

        private sealed class CatalogItem
        {
            public CatalogItem(string category, string path, string text)
            {
                Category = category;
                Path     = path;
                Text     = text;
            }

            public string Category { get; }
            public string Path     { get; }
            public string Text     { get; set; }
            public bool   BuiltIn  { get; set; }
        }
    }
}
