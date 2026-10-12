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
    /// Every kind of surface opened and closed around one editor that stays, and a test that drives
    /// them from the page itself: rounds of opening, using and closing, checking that nothing is left
    /// behind and that nothing still open stops working.
    /// </summary>
    [SampleDetails(Group = "Runtime and hosting", Order = 11, Icon = UIcons.ArrowsRepeat)]
    public class LifecycleSample : IComponent, ISample
    {
        /// <summary>Stable hooks for reading the page from a browser check.</summary>
        internal const string LOG_CLASS     = "tssm-lifecycle-log";
        internal const string COUNTS_CLASS  = "tssm-lifecycle-counts";
        internal const string SUMMARY_CLASS = "tssm-lifecycle-summary";

        private const int ROUNDS      = 3;
        private const int POLL_MS     = 50;
        private const int TIMEOUT_MS  = 15_000;
        private const int POPUP_MS    = 3_000;

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
        private readonly TextBlock    _summary    = TextBlock("not run yet").SemiBold().Class(SUMMARY_CLASS);
        private readonly Stack        _results    = VStack().WS();
        private readonly List<string> _errors     = new List<string>();
        private readonly CodeEditor   _survivor;
        private readonly Button       _run;

        private CodeEditor  _alpha;
        private CodeViewer  _viewer;
        private DiffViewer  _diff;
        private MultiEditor _multi;
        private Modal       _modal;
        private bool        _running;
        private int         _checks;
        private int         _failures;

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

            _run = Button("Run lifecycle test").Primary().SetIcon(UIcons.Play).OnClick(() => RunAsync(ROUNDS).FireAndForget());

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
                        TextBlock("Run lifecycle test drives the buttons below itself, " + ROUNDS + " rounds of: open the editors; open the diff and wait for its worker; close the diff, then the first completion in a tab; an editor in a modal, opened, completed and closed; the survivor's history, opened and closed, then the first completion and hover in the editor; close all. After every closing it checks completion and hover in the survivor, and after every round that editors, diff editors, models, editor DOM nodes and the popup host's children are back to where they started. Any uncaught error or unhandled rejection on the page fails it."),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(_run, _summary),
                        _results.PT(8),
                        SampleHint("Leave the page alone while it runs: it moves the focus and opens popups, and a click of yours lands in the middle of a step.")
                    )).SetTitle("Test")))
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

            _modal = Modal("Editor in a modal")
               .W(60.vw())
               .H(40.vh())
               .Content(editor.WS().HS())
               .OnHide(_ =>
               {
                   editor.Dispose();
                   _modal = null;
                   RefreshCountsSoon();
               });

            _modal.Show();
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

        // ---------------------------------------------------------------------------------------
        // The test.

        private async Task RunAsync(int rounds)
        {
            if (_running) return;

            _running  = true;
            _checks   = 0;
            _failures = 0;
            _errors.Clear();
            _results.Clear();
            _run.Disabled();
            _summary.Text = "running...";

            window.addEventListener("error", OnPageError);
            window.addEventListener("unhandledrejection", OnUnhandledRejection);

            try
            {
                CloseAll();
                await WaitUntil(() => EditorCount() == 1, "only the survivor open");

                var baseline = Snapshot();
                Report(true, "baseline: " + baseline);

                await CheckSurvivor("the start");

                for (var round = 1; round <= rounds; round++)
                {
                    await RoundAsync(round, baseline);
                }
            }
            catch (System.Exception e)
            {
                Report(false, "the test threw: " + e.Message);
            }
            finally
            {
                window.removeEventListener("error", OnPageError);
                window.removeEventListener("unhandledrejection", OnUnhandledRejection);

                CloseAll();
                _modal?.Hide();

                _running = false;
                _run.Disabled(false);
                _summary.Text = _failures == 0
                    ? "passed: " + _checks + " checks"
                    : "FAILED: " + _failures + " of " + _checks + " checks";
            }
        }

        private async Task RoundAsync(int round, string baseline)
        {
            var at = "round " + round + ": ";
            Section("Round " + round);

            // 1. The editors. Nothing completes in them yet: each one's first suggest list is held back
            //    for straight after a diff has closed, which is the only moment a dangling page-wide
            //    reference shows.
            OpenEditors();
            await WaitUntil(() => EditorCount() == 5, at + "the editor, the viewer and two tabs open");
            await WaitUntil(() => MonacoEditor.GetMarkers().Any(m => m.message == "TODO found by alpha"), at + "alpha's validator marks its TODO");

            // 2. The diff, opened last so it is the newest editor on the page when it closes.
            OpenDiff();
            await WaitUntil(() => EditorCount() == 7, at + "the diff open");
            await WaitUntil(() => _diff?.Editor?.getLineChanges() is object && _diff.Editor.getLineChanges().Length > 0, at + "the diff worker's line changes arrive");

            var diffWords = await Suggestions(_diff.Editor.getModifiedEditor());
            Report(diffWords.Contains("diffOne"), at + "completion on the diff's modified side", diffWords);
            ReportErrors(at + "using the diff");

            // 3. Close the diff, then the first completion in an editor that was open all along.
            CloseDiff();
            await WaitUntil(() => EditorCount() == 5 && DiffNodeCount() == 0, at + "the diff closed");

            var oneWords = await Suggestions(FindEditor("// one.cs"));
            Report(oneWords.Contains("oneItem"), at + "first completion in a tab after the diff closed", oneWords);
            ReportErrors(at + "first completion after closing the diff");
            await CheckSurvivor(at + "closing the diff");

            // 4. An editor in a modal.
            OpenModal();
            await WaitUntil(() => EditorCount() == 6, at + "the modal's editor open");

            var modalWords = await Suggestions(FindEditor("// modal.cs"));
            Report(modalWords.Contains("modalOne"), at + "completion in the modal's editor", modalWords);

            _modal?.Hide();
            await WaitUntil(() => EditorCount() == 5, at + "the modal closed");
            await CheckSurvivor(at + "closing the modal");

            // 5. The survivor's history: a diff in a modal, and again the newest editor when it closes.
            var history = _survivor.ShowHistory();
            await WaitUntil(() => EditorCount() == 7 && DiffNodeCount() == 1, at + "the history's diff open");
            await Task.Delay(300);

            history.Hide();
            await WaitUntil(() => EditorCount() == 5 && DiffNodeCount() == 0, at + "the history closed");

            var alphaWords = await Suggestions(_alpha.Editor);
            Report(alphaWords.Contains("alphaOne") && !alphaWords.Contains("survivorOne"), at + "first completion in alpha after the history closed, and only its own words", alphaWords);

            var alphaHover = await HoverText(_alpha.Editor, "alphaOne");
            Report(alphaHover.Contains("Documented by the alpha editor"), at + "hover in alpha", alphaHover);

            var twoWords = await Suggestions(FindEditor("// two.cs"));
            Report(twoWords.Contains("twoItem"), at + "first completion in the second tab after the history closed", twoWords);
            ReportErrors(at + "first completion after closing the history");
            await CheckSurvivor(at + "closing the history");

            // 6. Close all, and back to where the round started.
            CloseAll();
            await WaitUntil(() => EditorCount() == 1, at + "everything closed");
            await Task.Delay(300);

            var after = Snapshot();
            Report(after == baseline, at + "back to the baseline", after);
            await CheckSurvivor(at + "closing all");
        }

        private async Task CheckSurvivor(string after)
        {
            var words = await Suggestions(_survivor.Editor);
            Report(words.Contains("survivorOne") && words.Contains("survivorTwo"), "survivor completion after " + after, words);
            Report(!words.Any(w => w.StartsWith("alpha") || w.StartsWith("diff") || w.StartsWith("modal")), "survivor completion offers nothing of another editor's after " + after, words);

            var hover = await HoverText(_survivor.Editor, "survivorOne");
            Report(hover.Contains("Documented by the survivor editor"), "survivor hover after " + after, hover);

            ReportErrors(after);
        }

        /// <summary>
        /// Opens the suggest list at the start of the editor's last line, which is empty in every
        /// document here so the provider's words all show, reads the visible list and closes it.
        /// </summary>
        private async Task<string[]> Suggestions(IStandaloneCodeEditor editor)
        {
            if (editor is null) return new string[0];

            editor.focus();
            editor.setPosition(new Position { lineNumber = editor.getModel().getLineCount(), column = 1 });

            try
            {
                editor.trigger("lifecycle", "editor.action.triggerSuggest", null);
            }
            catch (System.Exception e)
            {
                _errors.Add("triggerSuggest threw: " + e.Message);
            }

            var words = new string[0];

            for (var waited = 0; waited < POPUP_MS && words.Length == 0; waited += POLL_MS)
            {
                await Task.Delay(POLL_MS);
                words = VisibleTexts(".suggest-widget", ".monaco-list-row .label-name");
            }

            editor.trigger("lifecycle", "hideSuggestWidget", null);

            return words;
        }

        /// <summary>Shows the hover on the first occurrence of a word and returns the visible hover's text.</summary>
        private async Task<string> HoverText(IStandaloneCodeEditor editor, string word)
        {
            if (editor is null) return "";

            var model = editor.getModel();
            var match = model.findMatches(word, false, false, true, null, false, 1);

            if (match.Length == 0) return "";

            editor.focus();
            editor.setPosition(new Position { lineNumber = match[0].range.startLineNumber, column = match[0].range.startColumn + 1 });
            editor.trigger("lifecycle", "editor.action.showHover", null);

            var text = "";

            for (var waited = 0; waited < POPUP_MS && text.Length == 0; waited += POLL_MS)
            {
                await Task.Delay(POLL_MS);
                text = string.Join(" ", VisibleTexts(".monaco-hover", null));
            }

            MonacoEditor.HideHovers();

            return text;
        }

        /// <summary>
        /// The text of every visible match. Every editor owns its own popups inside the shared overflow
        /// host, so the first match in the document is usually some other editor's hidden one.
        /// </summary>
        private static string[] VisibleTexts(string popupSelector, string itemSelector)
        {
            var texts  = new List<string>();
            var popups = document.querySelectorAll(popupSelector);

            for (uint i = 0; i < popups.length; i++)
            {
                var popup = (HTMLElement)popups[i];

                if (popup.offsetHeight <= 0) continue;

                if (itemSelector is null)
                {
                    texts.Add(popup.textContent ?? "");
                    continue;
                }

                var items = popup.querySelectorAll(itemSelector);

                for (uint j = 0; j < items.length; j++) texts.Add((items[j].textContent ?? "").Trim());
            }

            return texts.ToArray();
        }

        private static IStandaloneCodeEditor FindEditor(string marker)
        {
            return MonacoEditor.GetEditors().FirstOrDefault(e => e.getModel() is object && e.getModel().getValue().Contains(marker));
        }

        private static int EditorCount() => MonacoEditor.GetEditors().Length;

        private static int DiffNodeCount() => (int)document.querySelectorAll(".monaco-diff-editor").length;

        /// <summary>Everything "back to the baseline" compares, as one string so a mismatch reads at a glance.</summary>
        private static string Snapshot()
        {
            var host        = (HTMLElement)document.querySelector("[data-monaco-overflow-host=\"1\"]");
            var editorNodes = document.querySelectorAll(".monaco-editor");
            var nodes       = 0;

            for (uint i = 0; i < editorNodes.length; i++)
            {
                if (editorNodes[i] != host) nodes++;
            }

            return "editors " + EditorCount() +
                   ", diff editors " + DiffNodeCount() +
                   ", models " + MonacoEditor.GetModels().Length +
                   ", editor nodes " + nodes +
                   ", popup host children " + (host is object ? (int)host.children.length : 0);
        }

        private async Task WaitUntil(System.Func<bool> condition, string what)
        {
            for (var waited = 0; waited < TIMEOUT_MS; waited += POLL_MS)
            {
                bool met;

                try
                {
                    met = condition();
                }
                catch
                {
                    met = false;
                }

                if (met)
                {
                    Report(true, what);
                    return;
                }

                await Task.Delay(POLL_MS);
            }

            Report(false, "timed out waiting for " + what, Snapshot());
        }

        private void OnPageError(Event e)
        {
            _errors.Add("uncaught error: " + (((ErrorEvent)e).message ?? "(no message)"));
        }

        private void OnUnhandledRejection(Event e)
        {
            _errors.Add("unhandled rejection");
        }

        private void ReportErrors(string during)
        {
            Report(_errors.Count == 0, "no errors during " + during, _errors.Count == 0 ? null : string.Join(" | ", _errors));
            _errors.Clear();
        }

        private void Report(bool ok, string what, string[] detail) => Report(ok, what, "[" + string.Join(", ", detail) + "]");

        private void Report(bool ok, string what, string detail = null)
        {
            _checks++;

            if (!ok) _failures++;

            var line = (ok ? "ok    " : "FAIL  ") + what + (ok || detail is null ? "" : " - " + detail);

            _results.Add(TextBlock(line).Small().Class(ok ? "tssm-lifecycle-ok" : "tssm-lifecycle-fail").Foreground(ok ? Theme.Secondary.Foreground : Theme.Danger.Background));

            if (!ok) console.error("lifecycle: " + line);
        }

        private void Section(string title) => _results.Add(TextBlock(title).SemiBold().PT(8));

        public HTMLElement Render() => _content.Render();
    }
}
