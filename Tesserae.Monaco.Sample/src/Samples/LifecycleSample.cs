using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// Every kind of surface opened and closed around one editor that stays - the page
    /// <c>scripts/lifecycle-check.mjs</c> drives to prove that closing things leaves nothing behind and
    /// breaks nothing that is still open.
    /// </summary>
    [SampleDetails(Group = "Runtime and hosting", Order = 11, Icon = UIcons.ArrowsRepeat)]
    public class LifecycleSample : IComponent, ISample
    {
        /// <summary>Stable hooks for the browser check.</summary>
        internal const string LOG_CLASS    = "tssm-lifecycle-log";
        internal const string COUNTS_CLASS = "tssm-lifecycle-counts";

        private const string SCOPE    = "gallery:lifecycle";
        private const string DOCUMENT = "survivor.cs";

        private const string SURVIVOR_TEXT =
            "// survivor.cs - stays open while everything else comes and goes\n" +
            "var kept = survivorOne;\n";

        private const string ALPHA_TEXT =
            "// alpha.cs - completion, hover, a validator and a keybinding of its own\n" +
            "var first = alphaOne;\n" +
            "// TODO: flagged by alpha's validator\n";

        private const string VIEWER_TEXT =
            "// viewer.cs - read-only, wired to nothing\n" +
            "var second = survivorOne;\n";

        private const string DIFF_ORIGINAL = "// diff.cs\nvar third = diffOne;\n";
        private const string DIFF_MODIFIED = "// diff.cs\nvar third = diffTwo;\n// changed on the right\n";

        private readonly IComponent   _content;
        private readonly Stack        _alphaSlot  = VStack().WS();
        private readonly Stack        _viewerSlot = VStack().WS();
        private readonly Stack        _diffSlot   = VStack().WS();
        private readonly Stack        _multiSlot  = VStack().WS();
        private readonly List<string> _entries    = new List<string>();
        private readonly TextBlock    _log        = TextBlock("nothing yet").Small().Secondary().Class(LOG_CLASS);
        private readonly TextBlock    _counts     = TextBlock("").Small().Secondary().Class(COUNTS_CLASS);
        private readonly CodeEditor   _survivor;

        private CodeEditor  _alpha;
        private CodeViewer  _viewer;
        private DiffViewer  _diff;
        private MultiEditor _multi;

        public LifecycleSample()
        {
            _survivor = BuildEditor("survivor", SURVIVOR_TEXT, new[] { "survivorOne", "survivorTwo" });

            // A history whose list always has one revision in it, so opening it shows a diff straight
            // away. The diff lives in a modal and is disposed when the modal closes, while the survivor
            // stays - the arrangement that used to break every suggest list on the page.
            var saved = new List<EditorHistoryEntry> { Checkpoint() };

            _survivor.PersistHistory(new EditorHistoryOptions
            {
                Scope          = SCOPE,
                DocumentId     = DOCUMENT,
                RestoreOnMount = false,
                RestorePlace   = false,
                Store          = new DelegateHistoryStore
                {
                    Save = entry =>
                    {
                        saved.Add(entry);

                        return Task.CompletedTask;
                    },
                    List = query => Task.FromResult(saved.OrderByDescending(entry => entry.Timestamp).ToArray())
                }
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(LifecycleSample), UIcons.ArrowsRepeat, "Open everything, close everything, and keep the one that stays working")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A real application does not build its editors once. It opens a diff to review a change, a modal to edit one value, a shell with a tab per document, and closes them again - while some other editor stays on screen the whole time. Every one of those closings has to release what it took: its Monaco editors, the models it created, the provider registrations it made globally, and the widgets it rendered into the shared popup host."),
                        TextBlock("And a closing must not break what is still open. Monaco keeps some state page-wide - the keybinding service, the provider registries, the hover-delegate factory - and an editor that leaves a dangling reference in one of them breaks every other editor on the page. This page is the regression check for both halves.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Close what you open. Removing a component from the DOM tears its editor down, but the component re-arms itself and rebuilds when it is added back - call .Dispose() when it is finished with for good. A diff editor disposes the two models it was given; a model you created yourself with MonacoEditor.CreateModel(...) is yours to dispose."),
                        TextBlock("The counts under the buttons come from monaco.editor.getEditors() and getModels(). After Close all they should be back to what they were before anything was opened, however many rounds you go.").MT(8),
                        TextBlock("Why the diff opens on its own: Monaco consults some page-wide state only the first time an editor needs it - an editor builds its suggest list the first time Ctrl+Space is pressed in it. So a dangling reference left by a closed diff only shows in an editor that had not completed anything yet, and only while the diff was the last editor created. The diff button makes that order deterministic.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("The editor that stays"),
                        TextBlock("Ctrl+Space here offers survivorOne/survivorTwo and hovering survivorOne documents it - before, during and after everything below has been opened and closed."),
                        _survivor.WS().H(110.px()).MT(8),
                        SampleSubTitle("Everything that comes and goes"),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().Children(
                            Button("Open editors").Primary().OnClick(OpenEditors),
                            Button("Open diff").OnClick(OpenDiff),
                            Button("Close diff").OnClick(CloseDiff),
                            Button("Close all").OnClick(CloseAll),
                            Button("Editor in a modal").SetIcon(UIcons.WindowMaximize).OnClick(OpenModal),
                            Button("History of the survivor").SetIcon(UIcons.ClockFuturePast).OnClick(() => _survivor.ShowHistory()),
                            Button("Refresh counts").OnClick(RefreshCounts)),
                        _counts.MT(8),
                        VStack().WS().PT(8).Children(
                            TextBlock("Provider log (newest first)").Small().SemiBold(),
                            _log.MT(4)),
                        HStack().WS().Wrap().MT(8).Children(
                            Pane("alpha.cs (editor)", _alphaSlot).PR(6),
                            Pane("viewer.cs (read-only viewer)", _viewerSlot).PL(6)),
                        Pane("diff.cs (diff, modified side editable)", _diffSlot).W(100.percent()).MT(8),
                        Pane("a MultiEditor shell with two tabs", _multiSlot).W(100.percent()).MT(8),
                        SampleHint("Open the editors, then the diff, close the diff and press Ctrl+Space in an editor you have not used yet - then Close all and check the counts. Repeat.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(MultipleEditorsSample), typeof(RemountSample), typeof(ModalSample), typeof(HistoryPersistenceSample));

            MonacoEditor.WhenLoaded(RefreshCounts);
        }

        private void OpenEditors()
        {
            CloseAll();

            _alpha  = BuildEditor("alpha", ALPHA_TEXT, new[] { "alphaOne", "alphaTwo" });

            _alpha.ValidateAsYouType(code =>
            {
                var diagnostics = new List<CodeDiagnostic>();
                var lines       = code.Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var at = lines[i].IndexOf("TODO");

                    if (at >= 0) diagnostics.Add(new CodeDiagnostic(i, at, i, at + 4, "TODO found by alpha", MarkerSeverity.Warning));
                }

                return Task.FromResult<ReadOnlyArray<CodeDiagnostic>>(diagnostics.ToArray());
            });

            _viewer = MonacoEditor.Viewer().SetLanguage("csharp").SetText(VIEWER_TEXT);

            _multi = MonacoEditor.MultiEditor()
               .ConfigureEditor((doc, editor) =>
               {
                   var word = doc.Title.Replace(".cs", "") + "Item";

                   editor.OnCompletion(context =>
                   {
                       Log(doc.Title + ": completion");

                       return Task.FromResult(Items(new[] { word }, doc.Title));
                   });

                   editor.OnHover(context => Task.FromResult(context.Word is null ? null : "**" + context.Word + "** in " + doc.Title));
               });

            _multi.Documents(new[]
            {
                new EditorDocument("one.cs", "one.cs") { Load = () => Task.FromResult("// one.cs\nvar one = 1;\n"), Save = text => Task.FromResult(true) },
                new EditorDocument("two.cs", "two.cs") { Load = () => Task.FromResult("// two.cs\nvar two = 2;\n"), Save = text => Task.FromResult(true) }
            });

            _alphaSlot.Add(_alpha.WS().H(110.px()));
            _viewerSlot.Add(_viewer.WS().H(110.px()));
            _multiSlot.Add(_multi.WS().H(220.px()));

            _multi.Open("one.cs");
            _multi.Open("two.cs");

            Log("opened the editors");
            RefreshCountsSoon();
        }

        private void OpenDiff()
        {
            CloseDiff();

            _diff = MonacoEditor.Diff()
               .SetLanguage("csharp")
               .Editable()
               .SetContent(DIFF_ORIGINAL, DIFF_MODIFIED);

            _diff.OnCompletion("csharp", context =>
            {
                Log("diff: completion");

                return Task.FromResult(Items(new[] { "diffOne", "diffTwo" }, "diff"));
            });

            _diffSlot.Add(_diff.WS().H(140.px()));

            Log("opened the diff");
            RefreshCountsSoon();
        }

        private void CloseDiff()
        {
            if (_diff is null) return;

            _diff.Dispose();
            _diffSlot.Clear();
            _diff = null;

            Log("closed the diff");
            RefreshCountsSoon();
        }

        /// <summary>
        /// Disposes what has a Dispose, and takes the MultiEditor out of the DOM with its tabs still
        /// open - the shell has no Dispose of its own, and leaving a page is exactly that: the shell's
        /// editors are torn down by leaving the document.
        /// </summary>
        private void CloseAll()
        {
            _alpha?.Dispose();
            _viewer?.Dispose();
            _diff?.Dispose();

            _alphaSlot.Clear();
            _viewerSlot.Clear();
            _diffSlot.Clear();
            _multiSlot.Clear();

            if (_alpha is object || _multi is object || _diff is object) Log("closed all");

            _alpha  = null;
            _viewer = null;
            _diff   = null;
            _multi  = null;

            RefreshCountsSoon();
        }

        private void OpenModal()
        {
            var editor = BuildEditor("modal", "// modal.cs - closes with the modal\nvar inside = modalOne;\n", new[] { "modalOne", "modalTwo" });

            Modal("Editor in a modal")
               .W(60.vw())
               .H(40.vh())
               .Content(editor.WS().HS())
               .OnHide(_ =>
               {
                   editor.Dispose();
                   RefreshCountsSoon();
               })
               .Show();
        }

        private CodeEditor BuildEditor(string name, string text, string[] words)
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(text);

            editor.OnCompletion(context =>
            {
                Log(name + ": completion");

                return Task.FromResult(Items(words, name));
            });

            editor.OnHover(context =>
            {
                if (context.Word is null || !words.Contains(context.Word)) return Task.FromResult<string>(null);

                Log(name + ": hover on " + context.Word);

                return Task.FromResult("**" + context.Word + "**\n\nDocumented by the " + name + " editor.");
            });

            editor.AddCommand(KeyMod.With(KeyMod.CtrlCmd | KeyMod.Alt, KeyCode.KeyK), () => Log(name + ": Ctrl+Alt+K"));

            return editor;
        }

        private static CompletionItem[] Items(string[] words, string owner)
        {
            return words.Select(word => new CompletionItem
            {
                label  = word,
                kind   = CompletionItemKind.Variable,
                detail = "from the " + owner + " editor"
            }).ToArray();
        }

        private static EditorHistoryEntry Checkpoint()
        {
            return new EditorHistoryEntry
            {
                Scope      = SCOPE,
                DocumentId = DOCUMENT,
                Timestamp  = EditorHistory.Now() - 60 * 60 * 1000,
                Text       = "// survivor.cs, an hour ago\nvar kept = survivorTwo;\n",
                Language   = "csharp",
                Label      = "checkpoint",
                Author     = "lifecycle",
                Origin     = EditorHistoryOrigin.Remote,
                Id         = "lifecycle-checkpoint"
            };
        }

        private static IComponent Pane(string caption, IComponent surface)
        {
            return VStack().W(50.percent()).MinWidth(260.px()).Children(
                TextBlock(caption).Small().SemiBold(),
                surface.MT(4));
        }

        /// <summary>Monaco disposes asynchronously in places, so the readout waits a beat.</summary>
        private void RefreshCountsSoon() => window.setTimeout(_ => RefreshCounts(), 300);

        private void RefreshCounts()
        {
            _counts.Text = "editors: " + MonacoEditor.GetEditors().Length + " · models: " + MonacoEditor.GetModels().Length;
        }

        private void Log(string entry)
        {
            _entries.Insert(0, entry);

            while (_entries.Count > 8) _entries.RemoveAt(_entries.Count - 1);

            _log.Text = string.Join(" · ", _entries);
        }

        public HTMLElement Render() => _content.Render();
    }
}
