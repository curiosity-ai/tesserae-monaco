using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 7, Icon = UIcons.ClockFuturePast)]
    public class HistoryPersistenceSample : IComponent, ISample
    {
        private const string SCOPE    = "gallery:demo-user";
        private const string DOCUMENT = "samples/history.cs";

        private const string SEED =
            "// Type in here, then reload the page - the text and the caret come back.\n" +
            "// Every pause in typing writes a revision to IndexedDB, stamped with the time.\n" +
            "\n" +
            "int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);\n";

        private readonly IComponent _content;

        public HistoryPersistenceSample()
        {
            // Stands in for a server-hosted history service. A real one would post to an endpoint;
            // what matters here is the shape - a store built from lambdas, so hooking an external
            // system in is an object initialiser rather than a class.
            var sent   = new List<string>();
            var log    = VStack().WS();
            var server = new DelegateHistoryStore
            {
                Save = entry =>
                {
                    sent.Insert(0, Clock(entry.Timestamp) + "  " + Pad(entry.Label) + "  " + entry.Text.Length + " chars");

                    while (sent.Count > 6) sent.RemoveAt(sent.Count - 1);

                    RenderLog(log, sent);

                    return Task.FromResult(true);
                }
            };

            var revisions = VStack().WS();
            var status    = TextBlock("").Small().Secondary();

            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SEED);

            editor.PersistHistory(new EditorHistoryOptions
            {
                Scope      = SCOPE,
                DocumentId = DOCUMENT,

                // IndexedDB in front, the stand-in server behind it. Writes go to both; reads come
                // from IndexedDB and fall through to the server when it is empty, which is what a
                // second device needs.
                Store = MirroredHistoryStore.LocalFirst(server),

                // Short so the page is worth watching. The default is 1500ms.
                SnapshotDebounceMs = 600,
                MaxEntries         = 20,

                OnSaved    = _ => ShowRevisions(editor, revisions, status).FireAndForget(),
                OnRestored = entry => status.Text = "restored the revision from " + Clock(entry.Timestamp),
                OnError    = exception => status.Text = "store error: " + exception.Message
            });

            editor.OnRendered(_ => ShowRevisions(editor, revisions, status).FireAndForget());

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(HistoryPersistenceSample), UIcons.ClockFuturePast, "Keeping an editor's history across reloads")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("PersistHistory(...) records the document as it is edited and puts it back the next time the same document is opened. It keeps two things, because they are what Monaco actually hands out serialisably: the text, and the view state - caret, selections, scroll offset and folding."),
                        TextBlock("Monaco's undo stack is not one of them. It lives in the editor's undo service as objects holding closures over the model, with no accessor and nothing to serialise, so no wrapper can round-trip it through storage. A restored revision is therefore applied as an ordinary edit, which puts it on the live undo stack - so undo reaches back past a restore.").MT(8),
                        TextBlock("It goes into IndexedDB. sessionStorage is emptied when the tab closes; localStorage survives but is synchronous - every write blocks the thread Monaco lays out on - caps out around 5 MB, stores strings only, and has no index to prune by. IndexedDB is asynchronous, sized against available disk, stores the view state as an object, and its cursors make \"newest first\" and \"older than a month\" bounded rather than full scans.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Scope every entry. Scope is the partition - a user id, a workspace id, or a composite - and DocumentId addresses the file inside it, so one origin holds several users' or projects' histories without them seeing each other. This page uses " + "\"" + SCOPE + "\" and \"" + DOCUMENT + "\"."),
                        TextBlock("Every entry is stamped with a UTC epoch-millisecond timestamp, and Clock is where that comes from. Replace it when a server is the authority on time: a device with a wrong clock otherwise stamps revisions that sort ahead of or behind everything the server knows, and a mirrored history then orders wrongly.").MT(8),
                        TextBlock("Three ways to reach an external system, in increasing order of involvement. OnSaved is told about every revision and leaves the browser as the store. DelegateHistoryStore builds a whole store out of lambdas. MirroredHistoryStore runs both at once - writes to each, reads from the browser and falling back to the server. ShouldRestore is the veto for when the server is also an authority on the document.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Type something, leave the caret in the middle of it, then reload the page. The text and the caret come back, and the revision list below fills up as you pause."),
                        editor.WS().H(200.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Save a revision now").SetIcon(UIcons.ClockFuturePast).OnClick(() => SaveNow(editor, revisions, status).FireAndForget()),
                            Button("Refresh list").OnClick(() => ShowRevisions(editor, revisions, status).FireAndForget()),
                            Button("Forget this document").SetIcon(UIcons.Bug).OnClick(() => Forget(editor, revisions, status).FireAndForget()),
                            status.PL(8.px())),
                        SampleSubTitle("Stored revisions, newest first"),
                        revisions,
                        SampleSubTitle("What the stand-in server was sent"),
                        log,
                        SampleHint("The list survives a reload, and \"Forget this document\" clears both the browser's copy and this page's. Everything is filed under the scope above, so nothing else on this origin sees it.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(SeveralDocumentsSample), typeof(RemountSample), typeof(EventsSample));

            RenderLog(log, sent);
        }

        public HTMLElement Render() => _content.Render();

        private static async Task SaveNow(CodeEditor editor, Stack revisions, TextBlock status)
        {
            var history = editor.History;

            if (history is null) return;

            var entry = await history.SaveNowAsync("manual save");

            status.Text = entry is null ? "nothing changed since the last revision" : "saved at " + Clock(entry.Timestamp);

            await ShowRevisions(editor, revisions, status);
        }

        private static async Task Forget(CodeEditor editor, Stack revisions, TextBlock status)
        {
            var history = editor.History;

            if (history is null) return;

            await history.ClearAsync();

            status.Text = "history cleared";

            await ShowRevisions(editor, revisions, status);
        }

        private static async Task ShowRevisions(CodeEditor editor, Stack revisions, TextBlock status)
        {
            var history = editor.History;

            if (history is null) return;

            var entries = await history.ListAsync(10);
            var rows    = new List<IComponent>();

            foreach (var entry in entries)
            {
                var captured = entry;

                rows.Add(HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(2).Children(
                    TextBlock(Clock(captured.Timestamp)).Small().Secondary().W(90.px()),
                    TextBlock(Pad(captured.Label)).Small().Secondary().W(110.px()),
                    TextBlock(captured.Text.Length + " chars").Small().Secondary().W(80.px()),
                    Button("Restore").Small().OnClick(() =>
                    {
                        if (editor.History.Restore(captured)) status.Text = "restored - undo reaches back past it";
                    })));
            }

            if (rows.Count == 0) rows.Add(TextBlock("nothing stored yet - type in the editor").Small().Secondary());

            revisions.Clear();

            foreach (var row in rows)
            {
                revisions.Add(row);
            }
        }

        private static void RenderLog(Stack log, List<string> lines)
        {
            log.Clear();

            if (lines.Count == 0)
            {
                log.Add(TextBlock("nothing sent yet").Small().Secondary());

                return;
            }

            foreach (var line in lines)
            {
                log.Add(TextBlock(line).Small().Secondary());
            }
        }

        /// <summary>The entry's timestamp as a local wall clock, which is all a demo list needs of it.</summary>
        private static string Clock(double epochMilliseconds)
        {
            var moment = EditorHistory.ToDateTime(epochMilliseconds).ToLocalTime();

            return Two(moment.Hour) + ":" + Two(moment.Minute) + ":" + Two(moment.Second);
        }

        private static string Two(int value) => value < 10 ? "0" + value : value.ToString();

        private static string Pad(string label) => string.IsNullOrEmpty(label) ? "-" : label;
    }
}
