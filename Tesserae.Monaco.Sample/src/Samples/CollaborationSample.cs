using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tesserae;
using static Transpose.Core.dom;
using static Transpose.Core.es5;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 14, Icon = UIcons.UsersAlt)]
    public class CollaborationSample : IComponent, ISample
    {
        private const string Seed =
            "// Every editor on this page edits its own Y.Doc. The documents are kept in sync\n" +
            "// through YjsSyncProvider over an in-memory network, like a server would relay them.\n" +
            "public static class Greeter\n" +
            "{\n" +
            "    public static string Greet(string name)\n" +
            "    {\n" +
            "        return \"Hello, \" + name + \"!\";\n" +
            "    }\n" +
            "}\n";

        private readonly IComponent _content;

        public CollaborationSample()
        {
            _content = SectionStack().Secondary()
               .SampleTitle(typeof(CollaborationSample), UIcons.UsersAlt, "Several people in one document, with Yjs")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("editor.BindYjs(text, awareness) binds a code editor to a shared Yjs text: the editor shows the shared content, what the user types is written into it, and what collaborators write appears in place with the caret kept where it was. With an Awareness, each collaborator's caret and selection is drawn in their colour with their name, and this user's selection is published in the same shape y-monaco uses, so JavaScript y-monaco clients and these editors see each other."),
                        TextBlock("Ctrl+Z goes through a Y.UndoManager that only tracks this editor's edits, so undo reverts what this user did and never what a collaborator did. The binding follows the editor through a remount, and Dispose removes every listener, decoration and style it added.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Await Yjs.LoadAsync() before creating a Y.Doc - Yjs is fetched on demand, not part of the page's bundle. Give each user's awareness a user field from YjsUserColors.UserFor(name) so every peer draws them in the same colour."),
                        TextBlock("The binding sets the model to LF line endings and maps offsets when the shared text holds \\r\\n (from a client whose model is CRLF), so mixed line endings stay consistent. Remote edits never land on Monaco's own undo stack; leave undo on unless the host provides its own.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(Defer(async () =>
                    {
                        await Yjs.LoadAsync();
                        return new Playground().Content;
                    }, TextBlock("Loading Yjs..."))).SetTitle("Usage")))
               .SeeAlso(typeof(MultipleEditorsSample), typeof(RemountSample), typeof(HistoryPersistenceSample));
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>The editors, their network and the page's own test.</summary>
        private sealed class Playground
        {
            private static readonly string[] Names = { "Ada", "Grace", "Linus", "Barbara", "Edsger" };

            private readonly Network    _network = new Network();
            private readonly List<Peer> _peers   = new List<Peer>();
            private readonly Stack      _row     = HStack().WS().Wrap().Gap(12.px());
            private readonly TextBlock  _convergence = TextBlock().Small();
            private readonly Toggle     _offline;
            private readonly Button     _add;

            public IComponent Content { get; }

            public Playground()
            {
                var latencyLabel = TextBlock("Latency: " + _network.Latency + " ms").Small();
                var latency      = Slider(val: _network.Latency, min: 0, max: 1500, step: 50).W(180).OnInput((s, e) =>
                {
                    _network.Latency  = s.Value;
                    latencyLabel.Text = "Latency: " + s.Value + " ms";
                });

                _offline = Toggle("Offline").OnChange((t, e) => SetOfflineAsync(t.IsChecked).FireAndForget());
                _add     = Button("Add an editor").SetIcon(UIcons.UserAdd).OnClick(() => AddPeer());

                var test = new SampleTest("Run collaboration test", RunTestAsync, cleanup: () =>
                {
                    _offline.IsChecked = false;
                    if (_network.Offline) SetOfflineAsync(false).FireAndForget();
                });

                AddPeer(Seed);
                AddPeer();

                Content = VStack().WS().Children(
                        HStack().WS().Wrap().AlignItemsCenter().Gap(16.px()).Children(latencyLabel, latency, _offline, _add),
                        _row.PT(8),
                        _convergence.PT(8),
                        SampleSubTitle("Self-test"),
                        SampleHint("Edits both editors at once at random, with latency and an offline stretch, inserts CRLF text the way a CRLF y-monaco client would, checks that undo only reverts the local editor, remounts an editor, and disposes a binding - then checks that every document and every model holds the same text and nothing was left behind."),
                        test.PT(8))
                   .WhenRemoved(() =>
                    {
                        foreach (var peer in _peers.ToArray()) peer.Dispose();
                        _peers.Clear();
                    });

                ConnectAllAsync().FireAndForget();
            }

            private Peer AddPeer(string seed = null)
            {
                var name = Names[_peers.Count % Names.Length];
                var peer = new Peer(name, _network, seed, UpdateConvergence);

                _peers.Add(peer);
                _network.Join(peer);
                _row.Add(peer.Content.Grow().W(10).MinWidth(260.px()));

                if (_peers.Count >= Names.Length) _add.Disabled();

                if (_peers.Count > 2 && !_network.Offline) peer.Provider.ConnectAsync().FireAndForget();

                UpdateConvergence();
                return peer;
            }

            private async Task ConnectAllAsync()
            {
                foreach (var peer in _peers) await peer.Provider.ConnectAsync();
            }

            private async Task SetOfflineAsync(bool offline)
            {
                _network.Offline = offline;

                if (offline)
                {
                    foreach (var peer in _peers) peer.Provider.Disconnect();
                }
                else
                {
                    await ConnectAllAsync();
                }

                UpdateConvergence();
            }

            private bool Converged()
            {
                var first = _peers[0].Text.asString();
                return _peers.All(p => p.Text.asString() == first && p.ModelText() == Normalize(first));
            }

            private void UpdateConvergence()
            {
                if (_peers.Count == 0) return;

                var same = _peers.All(p => p.Text.asString() == _peers[0].Text.asString());

                _convergence.Text = same
                    ? "Every document holds the same text."
                    : _network.Offline ? "The documents differ: the network is offline." : "The documents differ: updates are in flight.";

                _convergence.Foreground(same ? Theme.Success.Background : Theme.Secondary.Foreground);
            }

            private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

            // -------------------------------------------------------------------------------------
            // The self-test.

            private async Task RunTestAsync(SampleTest t)
            {
                var random = new Random(4242);
                var ada    = _peers[0];
                var grace  = _peers[1];

                t.Section("Convergence");
                await t.WaitUntil(Converged, "the page starts converged");

                _network.Latency = 120;

                for (var round = 0; round < 3; round++)
                {
                    for (var i = 0; i < 12; i++)
                    {
                        ada.RandomEdit(random);
                        grace.RandomEdit(random);
                        await Task.Delay(random.Next(5, 40));
                    }

                    await t.WaitUntil(Converged, "concurrent random edits converge (round " + (round + 1) + ")");
                }

                t.Section("Offline and back");
                await SetOfflineAsync(true);

                for (var i = 0; i < 8; i++)
                {
                    ada.RandomEdit(random);
                    grace.RandomEdit(random);
                }

                t.Check(ada.Text.asString() != grace.Text.asString(), "the documents differ while offline");
                await SetOfflineAsync(false);
                await t.WaitUntil(Converged, "they converge once back online");

                t.Section("Line endings");
                // A y-monaco client whose model is CRLF writes \r\n into the shared text.
                grace.Doc.transact(_ => grace.Text.insert(0, "// written with CRLF\r\n// line two\r\n", null), "crlf-client");
                await t.WaitUntil(Converged, "\\r\\n text converges, and every LF model shows it line for line");
                t.Check(ada.Editor.Editor.getModel().getLineCount() == Normalize(ada.Text.asString()).Split('\n').Length, "the model has one line per shared line break");

                ada.EditAtEnd("\n// after the CRLF lines");
                await t.WaitUntil(Converged, "an edit after \\r\\n lines lands in the right place on every side");
                t.Check(ada.Text.asString().IndexOf("// line two\r\n") >= 0, "the shared text keeps the \\r\\n it was given");

                t.Section("Caret preserved");
                grace.Editor.Editor.focus();
                grace.Editor.Editor.setPosition(new Position { lineNumber = 3, column = 1 });
                var before = grace.Editor.Editor.getModel().getLineContent(3);
                ada.InsertAtStart("// a line Ada inserted above\n");
                await t.WaitUntil(Converged, "a remote insert above the caret arrives");
                var after = grace.Editor.Editor.getPosition();
                t.Check(after.lineNumber == 4 && after.column == 1, "Grace's caret moved down with its line", after.lineNumber + ":" + after.column);
                t.Check(grace.Editor.Editor.getModel().getLineContent(4) == before, "and is still on the same text");

                t.Section("Remote cursors");
                await t.WaitUntil(() => ada.CaretCount() > 0, "Ada's editor draws Grace's caret");
                t.Check(document.querySelectorAll("style[data-tssm-yjs-client]").length > 0, "a style element carries the collaborators' colours");

                t.Section("Undo is local");
                ada.Binding.UndoManager.stopCapturing();
                ada.EditAtEnd("\n// ada-undo-marker");
                await t.WaitUntil(Converged, "Ada's edit arrives");
                grace.EditAtEnd("\n// grace-keeps-this");
                await t.WaitUntil(Converged, "Grace's edit arrives");
                ada.Binding.Undo();
                await t.WaitUntil(Converged, "Ada's undo propagates");
                t.Check(ada.Text.asString().IndexOf("ada-undo-marker") < 0, "Ada's undo removed Ada's edit");
                t.Check(ada.Text.asString().IndexOf("grace-keeps-this") >= 0, "and left Grace's edit alone");
                ada.Binding.Redo();
                await t.WaitUntil(() => Converged() && ada.Text.asString().IndexOf("ada-undo-marker") >= 0, "redo puts it back everywhere");

                t.Section("Remount");
                ada.Detach();
                grace.EditAtEnd("\n// written while Ada's editor was detached");
                await t.WaitUntil(() => ada.Text.asString().IndexOf("while Ada's editor was detached") >= 0, "Ada's document still syncs while its editor is detached");
                ada.Reattach();
                await t.WaitUntil(() => ada.ModelText().IndexOf("while Ada's editor was detached") >= 0, "the re-created editor shows it");
                ada.EditAtEnd("\n// typed after the remount");
                await t.WaitUntil(() => Converged() && grace.ModelText().IndexOf("typed after the remount") >= 0, "edits after the remount reach Grace");

                t.Section("Dispose");
                var third = AddPeer();
                await t.WaitUntil(() => Converged() && third.ModelText().Length > 0, "a third editor joins and catches up");
                third.Editor.Editor.focus();
                third.Editor.Editor.setPosition(new Position { lineNumber = 2, column = 3 });
                await t.WaitUntil(() => ada.CaretCount() >= 2, "Ada sees the third editor's caret");
                var thirdNode = third.Editor.Editor.getDomNode();
                await t.WaitUntil(() => third.CaretCount() > 0, "the third editor draws the others");
                third.Binding.Dispose();
                await t.WaitUntil(() => third.CaretCount() == 0, "disposing its binding removes the carets it drew");
                t.Check(third.Awareness.getLocalState().field(MonacoYjsBinding.AwarenessSelectionField) == null, "and withdraws its selection from the awareness");
                await t.WaitUntil(() => ada.CaretCount() == 1, "so the others stop drawing its caret");
                third.Editor.Editor.executeEdits("test", new[] { new TextEdit { range = new TextRange { startLineNumber = 1, startColumn = 1, endLineNumber = 1, endColumn = 1 }, text = "unbound " } });
                await Task.Delay(400);
                t.Check(ada.Text.asString().IndexOf("unbound ") < 0, "edits in an unbound editor stay local");

                RemovePeer(third);
                await t.WaitUntil(Converged, "everyone left is still converged");
                t.Check(thirdNode.isConnected == false, "the third editor left the page");
                t.CheckNoErrors("the collaboration test");
            }

            private void RemovePeer(Peer peer)
            {
                peer.Dispose();
                _peers.Remove(peer);
                _row.Remove(peer.Content);
                _add.Disabled(false);
            }
        }

        /// <summary>An in-memory relay: every message a peer sends reaches every other peer after the latency, unless the network is offline by then.</summary>
        private sealed class Network
        {
            private readonly List<Peer> _members = new List<Peer>();

            public int  Latency { get; set; } = 150;
            public bool Offline { get; set; }

            public void Join(Peer peer) => _members.Add(peer);

            public void Leave(Peer peer) => _members.Remove(peer);

            public Task Send(Peer from, Uint8Array message)
            {
                window.setTimeout(_ =>
                {
                    if (Offline) return;

                    foreach (var member in _members.ToArray())
                    {
                        if (member != from) member.Provider.Receive(message);
                    }
                }, Latency);

                return Task.FromResult(0);
            }
        }

        /// <summary>One collaborator: a document, presence, a provider, and an editor bound to the document.</summary>
        private sealed class Peer : IDisposable
        {
            private readonly Network     _network;
            private readonly HTMLElement _slot;
            private readonly HTMLElement _rendered;
            private readonly Badge       _synced;

            public string           Name      { get; }
            public Y.Doc            Doc       { get; }
            public Y.Text           Text      { get; }
            public Awareness        Awareness { get; }
            public YjsSyncProvider  Provider  { get; }
            public CodeEditor       Editor    { get; }
            public MonacoYjsBinding Binding   { get; }
            public IComponent       Content   { get; }

            public Peer(string name, Network network, string seed, Action changed)
            {
                Name     = name;
                _network = network;
                Doc      = new Y.Doc();
                Text     = Doc.getText("content");

                if (seed != null) Text.insertText(0, seed);

                Awareness = new Awareness(Doc);
                Awareness.setLocalStateField("user", YjsUserColors.UserFor(name));

                Provider = YjsSyncProvider.WithUint8ArraySender(Doc, Awareness, message => _network.Send(this, message));

                Editor  = MonacoEditor.Editor().SetLanguage("csharp");
                Binding = Editor.BindYjs(Text, Awareness, undo: true);

                Text.observe((e, tr) => changed());

                _synced = Badge("Connecting");
                Provider.Synced.Observe(synced => _synced.SetText(synced ? "Synced" : (_network.Offline ? "Offline" : "Connecting")));

                // The editor lives in a slot the page owns, so the self-test can take it out of the DOM and put it back.
                _slot = DIV();
                _slot.style.width  = "100%";
                _slot.style.height = "260px";
                _rendered = Editor.WS().HS().Render();
                _slot.appendChild(_rendered);

                Content = VStack().Gap(6.px()).Children(
                    HStack().AlignItemsCenter().Gap(8.px()).Children(
                        Raw(Swatch(YjsUserColors.ColorFor(name))),
                        TextBlock(name).SemiBold(),
                        _synced),
                    Raw(_slot));
            }

            public string ModelText()
            {
                var model = Editor.Editor?.getModel();
                return model == null ? "" : model.getValue();
            }

            /// <summary>How many remote carets are drawn in this editor.</summary>
            public int CaretCount()
            {
                var node = Editor.Editor?.getDomNode();
                return node == null ? 0 : (int)node.querySelectorAll(".tssm-yjs-caret").length;
            }

            /// <summary>An edit as a user makes it: through the editor, so the binding sees a model change.</summary>
            public void RandomEdit(Random random)
            {
                var model  = Editor.Editor.getModel();
                var length = model.getValue().Length;
                var at     = model.getPositionAt(random.Next(length + 1));
                var words  = new[] { "x", " y", "\n", "zz", "(q)" };

                if (length > 20 && random.Next(3) == 0)
                {
                    var end = model.getPositionAt(System.Math.Min(length, model.getOffsetAt(at) + random.Next(1, 4)));
                    Apply(at, end, "");
                }
                else
                {
                    Apply(at, at, words[random.Next(words.Length)]);
                }
            }

            public void EditAtEnd(string text)
            {
                var model = Editor.Editor.getModel();
                var end   = model.getPositionAt(model.getValue().Length);
                Apply(end, end, text);
            }

            public void InsertAtStart(string text)
            {
                var start = new Position { lineNumber = 1, column = 1 };
                Apply(start, start, text);
            }

            private void Apply(Position from, Position to, string text)
            {
                Editor.Editor.executeEdits("collaboration-sample", new[]
                {
                    new TextEdit
                    {
                        range = new TextRange { startLineNumber = from.lineNumber, startColumn = from.column, endLineNumber = to.lineNumber, endColumn = to.column },
                        text  = text
                    }
                });
            }

            public void Detach() => _rendered.remove();

            public void Reattach()
            {
                if (_rendered.parentElement == null) _slot.appendChild(_rendered);
            }

            private static HTMLElement Swatch(string color)
            {
                var swatch = DIV();
                swatch.style.width        = "10px";
                swatch.style.height       = "10px";
                swatch.style.borderRadius = "50%";
                swatch.style.background   = color;
                return swatch;
            }

            public void Dispose()
            {
                _network.Leave(this);
                Binding.Dispose();
                Provider.Dispose();
                Awareness.destroy();
                Doc.destroy();
            }
        }
    }
}
