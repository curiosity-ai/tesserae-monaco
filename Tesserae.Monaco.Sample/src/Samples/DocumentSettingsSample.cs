using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 9, Icon = UIcons.SlidersVSquare)]
    public class DocumentSettingsSample : IComponent, ISample
    {
        private const string ENDPOINT = "endpoints/search.cs";
        private const string TASK     = "tasks/nightly-import.cs";

        // The "server" behind the shell: the code, and the settings attached to it. Static, so a save is
        // still there after navigating away and back - and so is an edit that was never saved, which is
        // what .OnOpened re-reports below.
        private static readonly Dictionary<string, string> _code = new Dictionary<string, string>();

        private static EndpointSettings _endpoint      = EndpointSettings.Seed();
        private static EndpointSettings _endpointSaved = EndpointSettings.Seed();
        private static TaskSettings     _task          = TaskSettings.Seed();
        private static TaskSettings     _taskSaved     = TaskSettings.Seed();

        public DocumentSettingsSample()
        {
            Seed(ENDPOINT, "public async Task<IResult> Handle(SearchRequest request)\n{\n    var hits = await Index.SearchAsync(request.Query);\n\n    return Results.Ok(hits);\n}\n");
            Seed(TASK,     "public async Task RunAsync(CancellationToken cancellation)\n{\n    await Importer.RunAsync(BatchSize, cancellation);\n}\n");
            Seed("readme.md", "# Sample workspace\n\nThis document has no settings, so it gets no header strip.\n");

            var log = TextBlock("").Small().Secondary();

            var shell = MonacoEditor.MultiEditor()
               .SettingsText(Text())
               .PersistInUrl("dsopen", "dsactive")
               .FilterPlaceholder("Filter documents...")
               .Landing(() => VStack().AlignItemsCenter().Gap(8.px()).Children(
                    Icon(UIcons.SlidersVSquare, size: TextSize.Large, color: Theme.Secondary.Foreground),
                    TextBlock("Open search.cs - the strip above the editor is its settings").Secondary()))
               // A tab is rebuilt whenever it is opened, so the host re-reports what is still unsaved.
               .OnOpened((doc, editor) => Report(doc.Id))
               .OnSaved(doc => log.Text = "saved " + doc.Title + " - code and settings together")
               .OnDirtyChanged((doc, dirty) => log.Text = doc.Title + (dirty ? " has unsaved changes" : " is clean"));

            shell.Folder("endpoints", UIcons.Globe);
            shell.Folder("tasks",     UIcons.Clock);

            _shell = shell;

            shell.Documents(new List<EditorDocument>
            {
                new EditorDocument(ENDPOINT, "search.cs")
                {
                    Folder          = "endpoints",
                    Icon            = UIcons.FileCode,
                    Language        = "csharp",
                    Load            = () => Task.FromResult(_code[ENDPOINT]),
                    Save            = text => SaveEndpointAsync(text),
                    Settings        = () => EndpointForm(),
                    SettingsTitle   = "Settings - search.cs",
                    SettingsSummary = () => new[]
                    {
                        new SettingSummary("", _endpoint.Method, "Method") { Icon = UIcons.Globe },
                        new SettingSummary("", _endpoint.Path,   "Path") { Tooltip = "Where the endpoint answers" },
                        new SettingSummary("auth", _endpoint.Auth, "Auth"),
                        new SettingSummary("", _endpoint.Enabled ? "enabled" : "disabled", "Enabled")
                    },
                    RevertSettings = () =>
                    {
                        _endpoint = _endpointSaved.Copy();

                        return Task.CompletedTask;
                    }
                },
                new EditorDocument(TASK, "nightly-import.cs")
                {
                    Folder          = "tasks",
                    Icon            = UIcons.FileCode,
                    Language        = "csharp",
                    Load            = () => Task.FromResult(_code[TASK]),
                    Save            = text => SaveTaskAsync(text),
                    Settings        = () => TaskForm(),
                    SettingsSummary = () => new[] { new SettingSummary("", _task.Schedule, "Schedule") { Icon = UIcons.Clock } },
                    RevertSettings  = () =>
                    {
                        _task = _taskSaved.Copy();

                        return Task.CompletedTask;
                    }
                },
                new EditorDocument("readme.md", "readme.md")
                {
                    Icon = UIcons.Document,
                    Load = () => Task.FromResult(_code["readme.md"]),
                    Save = text => { _code["readme.md"] = text ?? ""; return Task.FromResult(true); }
                }
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(DocumentSettingsSample), UIcons.SlidersVSquare, "Structured settings attached to a document's code, edited in an overlay")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Code rarely stands alone: an endpoint has a route, a method and an authorization level, a task has a schedule, an index has a field and a model. Set EditorDocument.Settings and the shell puts a header strip above the editor - the settings worth seeing at a glance as chips, and a button that opens the rest in a DocumentSettingsModal. The strip is the always-visible home for them: a tab title has room for an icon, a name and its unsaved marker and nothing else, and a menu nobody opens is not an affordance."),
                        TextBlock("The shell does not draw the form and knows nothing about the fields, exactly as it knows nothing about a language's completions. The host builds the component - a few Dropdowns, or Tesserae's PropertyGrid over a plain object - and reports what the user changed with .MarkSettingsDirty(id, names). That is the whole seam.").MT(8),
                        TextBlock("The package ships no copy either: every label, tooltip and line comes from the DocumentSettingsText handed to .SettingsText(...), and the phrasing of \"what changed\" is composed by the host from the facts the shell passes it - whether the code changed as well, and which settings did. A member left unset is simply not said, so no fallback English can leak into a translated interface. This page's strings are all in one region of its own source.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("The overlay is an editing surface, not a transaction. Editing a setting makes the document unsaved, exactly as typing in its editor does - so there is no Cancel that discards and no second save of its own: closing it keeps the pending edits and leaves the tab's marker up, Save is the document's own save (the one Ctrl+S runs, which persists the code and the settings together), and Revert - offered only when the host supplies RevertSettings - is how the edits are given up deliberately. Two saves that could disagree about what \"saved\" means is what this shape avoids."),
                        TextBlock("The tab's marker is one dot for the whole document, so the settings say which half changed: the button turns the brand colour and counts them, its tooltip and the overlay's banner name them, a changed chip shows the pending value in the same colour, and the close prompt says whether the code changed as well. Pass no names and the marker still appears, without a count.").MT(8),
                        TextBlock("\"Changed\" means against what was saved, not against a setting's default - a document opened and left alone has nothing changed however far its values sit from the defaults. Keep the baseline you loaded, diff against it, and re-report after a save. A tab is rebuilt when it is reopened, so re-report from .OnOpened as well.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Open search.cs and press the Settings button on the strip (or Ctrl+comma, or Ctrl+P and pick it under Settings). Change the path: the button turns brand-coloured and says \"1 change\", the chip shows the pending value, the tab gets its unsaved dot, and the footer names what is waiting - beside Save, and without moving a single field, which is why it is not a banner above them. Close the overlay - the edits stay. Hover the tab to read what is unsaved. Type in the editor too and the close prompt says both changed. Ctrl+S saves them together and everything comes clean; Revert settings puts the values back and rebuilds the form. nightly-import.cs builds its form with PropertyGrid instead, and readme.md has no settings at all, so it gets no strip."),
                        shell.WS().H(560.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Open settings").SetIcon(UIcons.SlidersVSquare).OnClick(() => shell.ShowSettings(ENDPOINT)),
                            Button("Save all").SetIcon(UIcons.Disk).OnClick(() => shell.SaveAllAsync().FireAndForget()),
                            Button("What changed?").OnClick(() => log.Text = Describe(shell)),
                            log.PL(8.px())),
                        SampleHint("MarkSettingsDirty is the only thing the shell needs to know - the form, the values and the baseline all stay with the host.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(MultiEditorSample), typeof(HistoryPersistenceSample), typeof(EditorOptionsSample));
        }

        private readonly IComponent _content;

        private static MultiEditor _shell;

        #region Every word the settings surface says - the package ships none of it

        // The package is scaffold only: it draws the strip, the overlay and the dirty plumbing, and says
        // nothing. An application that translates its interface puts its own strings here (in Mosaik each
        // of these would carry .t()), and the phrasing of "what changed" is composed from the facts the
        // shell hands over - whether the code changed as well, and which settings did.
        private static DocumentSettingsText Text()
        {
            return new DocumentSettingsText
            {
                SettingsButton   = "Settings",
                SettingsTooltip  = "Edit the settings attached to this document",
                ChangedButton    = changed => "Settings - " + Changes(changed),
                ChangedTooltip   = changed => changed.Length > 0 ? "Unsaved settings: " + string.Join(", ", changed) : "The settings have unsaved changes",
                Title            = title => "Settings - " + title,

                SaveButton       = "Save",
                CloseButton      = "Close",
                RevertButton     = "Revert settings",

                // Short enough for the footer's slot, which clips rather than wraps; the whole sentence is
                // the line's hover text.
                SaveModel        = "Code and settings save together",
                SaveModelTooltip = SAVE_MODEL,
                PendingNotice    = changed => changed.Length > 0
                    ? Changes(changed) + " not saved yet: " + string.Join(", ", changed)
                    : "The settings have unsaved changes",
                PendingTooltip   = changed => changed.Length > 0
                    ? Changes(changed) + " not saved yet: " + string.Join(", ", changed) + ". " + SAVE_MODEL + "."
                    : SAVE_MODEL,

                TabTooltip = (codeChanged, changed) =>
                {
                    if (changed.Length == 0 && !codeChanged) return "Unsaved changes in the settings";
                    if (changed.Length == 0)                 return "Unsaved changes in the code";

                    return codeChanged
                        ? "Unsaved changes: the code and " + Settings(changed)
                        : "Unsaved changes: " + Settings(changed) + " - the code itself is unchanged";
                },

                ClosePrompt = (codeChanged, changed) =>
                {
                    if (changed.Length == 0) return null; // the prompt's own question already says it

                    return codeChanged
                        ? "The code and " + Settings(changed) + " changed."
                        : Settings(changed) + " changed; the code itself did not.";
                },

                PaletteSection  = "Settings",
                PaletteSubtitle = "settings"
            };
        }

        private const string SAVE_MODEL = "Saving the document saves the code and the settings together";

        private static string Changes(string[] changed) => changed.Length == 1 ? "1 change" : changed.Length + " changes";

        private static string Settings(string[] changed)
        {
            const int LISTED = 4;

            var listed = changed.Length <= LISTED
                ? string.Join(", ", changed)
                : string.Join(", ", changed.Take(LISTED)) + " and " + (changed.Length - LISTED) + " more";

            return (changed.Length == 1 ? "1 setting (" : changed.Length + " settings (") + listed + ")";
        }

        #endregion

        #region The settings themselves - the host's own state, which the shell never sees

        private sealed class EndpointSettings
        {
            public string Method  { get; set; }
            public string Path    { get; set; }
            public string Auth    { get; set; }
            public bool   Enabled { get; set; }

            public static EndpointSettings Seed() => new EndpointSettings { Method = "GET", Path = "/api/search", Auth = "user", Enabled = true };

            public EndpointSettings Copy() => new EndpointSettings { Method = Method, Path = Path, Auth = Auth, Enabled = Enabled };

            public string[] ChangesFrom(EndpointSettings saved)
            {
                var changed = new List<string>(4);

                if (Method  != saved.Method)  changed.Add("Method");
                if (Path    != saved.Path)    changed.Add("Path");
                if (Auth    != saved.Auth)    changed.Add("Auth");
                if (Enabled != saved.Enabled) changed.Add("Enabled");

                return changed.ToArray();
            }
        }

        private sealed class TaskSettings
        {
            [PropertyGridDescription("A cron expression, in UTC")]
            public string Schedule { get; set; }

            [PropertyGridLabel("Batch size")]
            public int BatchSize { get; set; }

            [PropertyGridLabel("Stop on error")]
            public bool StopOnError { get; set; }

            public static TaskSettings Seed() => new TaskSettings { Schedule = "0 3 * * *", BatchSize = 500, StopOnError = false };

            public TaskSettings Copy() => new TaskSettings { Schedule = Schedule, BatchSize = BatchSize, StopOnError = StopOnError };

            public string[] ChangesFrom(TaskSettings saved)
            {
                var changed = new List<string>(3);

                if (Schedule    != saved.Schedule)    changed.Add("Schedule");
                if (BatchSize   != saved.BatchSize)   changed.Add("BatchSize");
                if (StopOnError != saved.StopOnError) changed.Add("StopOnError");

                return changed.ToArray();
            }
        }

        #endregion

        #region The seam: diff against the baseline, and report

        private static string[] ChangesOf(string id)
        {
            if (id == ENDPOINT) return _endpoint.ChangesFrom(_endpointSaved);
            if (id == TASK)     return _task.ChangesFrom(_taskSaved);

            return new string[0];
        }

        private static void Report(string id)
        {
            var changed = ChangesOf(id);

            if (changed.Length > 0)
            {
                _shell.MarkSettingsDirty(id, changed);
            }
            else
            {
                _shell.MarkSettingsClean(id);
            }
        }

        #endregion

        #region The forms - anything the host likes, as long as it reports its edits

        private static IComponent EndpointForm()
        {
            var methods = new[] { "GET", "POST", "PUT", "DELETE" };
            var levels  = new[] { "anonymous", "user", "admin" };

            var method = Dropdown().W(160.px()).Items(methods.Select(m => DropdownItem(m)
               .SelectedIf(_endpoint.Method == m)
               .OnSelected(_ => Edit(() => _endpoint.Method = m))).ToArray());

            var path = TextBox(_endpoint.Path).WS();

            path.OnInput((s, e) => Edit(() => _endpoint.Path = s.Text));

            var auth = Dropdown().W(200.px()).Items(levels.Select(l => DropdownItem(l)
               .SelectedIf(_endpoint.Auth == l)
               .OnSelected(_ => Edit(() => _endpoint.Auth = l))).ToArray());

            var enabled = CheckBox("Answer requests").Checked(_endpoint.Enabled)
               .OnChange((s, e) => Edit(() => _endpoint.Enabled = s.IsChecked));

            return VStack().WS().P(4).Gap(14.px()).Children(
                Field("Method", method),
                Field("Path", path, "Where the endpoint answers - the identity of the thing, which is why it is a chip"),
                Field("Authorization", auth),
                Field("Enabled", enabled));
        }

        private static IComponent TaskForm()
        {
            // The short way to a form: reflect the host's own object. Every edit reports through the same seam.
            return PropertyGrid(_task)
               .Order("Schedule", 0)
               .Order("BatchSize", 1)
               .Order("StopOnError", 2)
               .OnChange(_ => Report(TASK)).WS().P(4);
        }

        private static IComponent Field(string label, IComponent control, string help = null)
        {
            var rows = new List<IComponent> { TextBlock(label).Small().SemiBold(), control };

            if (help is object) rows.Add(TextBlock(help).Tiny().Secondary());

            return VStack().WS().Gap(4.px()).Children(rows.ToArray());
        }

        private static void Edit(Action change)
        {
            change();
            Report(ENDPOINT);
        }

        #endregion

        #region Saving - one save for the code and the settings, then a new baseline

        private static async Task<bool> SaveEndpointAsync(string text)
        {
            await Task.Delay(150); // a round trip

            _code[ENDPOINT] = text ?? "";
            _endpointSaved  = _endpoint.Copy();

            return true;
        }

        private static async Task<bool> SaveTaskAsync(string text)
        {
            await Task.Delay(150);

            _code[TASK] = text ?? "";
            _taskSaved  = _task.Copy();

            return true;
        }

        #endregion

        private static string Describe(MultiEditor shell)
        {
            var parts = new List<string>(2);

            foreach (var id in new[] { ENDPOINT, TASK })
            {
                var changed = shell.ChangedSettings(id);

                if (changed.Length > 0) parts.Add(id + ": " + string.Join(", ", changed));
            }

            return parts.Count == 0 ? "no settings changed" : string.Join(" | ", parts);
        }

        private static void Seed(string id, string text)
        {
            if (!_code.ContainsKey(id)) _code[id] = text;
        }

        public HTMLElement Render() => _content.Render();
    }
}
