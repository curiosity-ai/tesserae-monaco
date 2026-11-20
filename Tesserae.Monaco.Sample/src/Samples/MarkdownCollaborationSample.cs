using System;
using System.Threading.Tasks;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 15, Icon = UIcons.Exchange)]
    public class MarkdownCollaborationSample : IComponent, ISample
    {
        private const string Seed =
            "# Meeting notes\n\n" +
            "Edit this document as **Markdown** on the left or as *rich text* on the right: both are views of one shared `Y.Text`.\n\n" +
            "- Decide the release date\n" +
            "- Review the [design](https://github.com/fastmail/Squire)\n\n" +
            "> Formatting applied with the toolbar on the right shows up as Markdown on the left.\n";

        private readonly IComponent _content;

        public MarkdownCollaborationSample()
        {
            _content = SectionStack().Secondary()
               .SampleTitle(typeof(MarkdownCollaborationSample), UIcons.Exchange, "Markdown in a code editor, rich text in Squire, one document")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A code editor and Tesserae's SquireEditor bind to the same kind of shared text: Markdown in a Y.Text. editor.BindYjs(text, awareness) on the CodeEditor edits the Markdown directly; squireEditor.BindYjs(text, awareness) renders it as rich text and writes its edits back as Markdown, changing only the blocks that were edited."),
                        TextBlock("Both publish the selection in the y-monaco awareness shape - relative positions into the Markdown - so each draws the other's caret where it is in the text: in the code editor at its Markdown offset, in the rich text editor at the matching place in the rendered block.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(Defer(async () =>
                    {
                        await Yjs.LoadAsync();
                        return new Playground().Content;
                    }, TextBlock("Loading Yjs..."))).SetTitle("Usage")))
               .SeeAlso(typeof(CollaborationSample));
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>Two documents - one shown as Markdown, one as rich text - kept in sync through an in-memory network.</summary>
        private sealed class Playground
        {
            private readonly Y.Doc           _markdownDoc  = new Y.Doc();
            private readonly Y.Doc           _richDoc      = new Y.Doc();
            private readonly Awareness       _markdownPeer;
            private readonly Awareness       _richPeer;
            private readonly YjsSyncProvider _markdownProvider;
            private readonly YjsSyncProvider _richProvider;
            private readonly TextBlock       _convergence = TextBlock().Small().Class("sample-md-convergence");

            private int  _latency = 150;
            private bool _offline;

            public IComponent Content { get; }

            public Playground()
            {
                var markdownText = _markdownDoc.getText("markdown");
                var richText     = _richDoc.getText("markdown");

                markdownText.insertText(0, Seed);

                _markdownPeer = new Awareness(_markdownDoc);
                _markdownPeer.setLocalStateField("user", YjsUserColors.UserFor("Ada"));

                _richPeer = new Awareness(_richDoc);
                _richPeer.setLocalStateField("user", YjsUserColors.UserFor("Grace"));

                _markdownProvider = YjsSyncProvider.WithUint8ArraySender(_markdownDoc, _markdownPeer, m => Send(() => _richProvider,     m));
                _richProvider     = YjsSyncProvider.WithUint8ArraySender(_richDoc,     _richPeer,     m => Send(() => _markdownProvider, m));

                var code = MonacoEditor.Editor().SetLanguage("markdown").WordWrap().Class("sample-md-monaco");
                var codeBinding = code.BindYjs(markdownText, _markdownPeer, undo: true);

                var rich = SquireEditor().Class("sample-md-squire");
                var richBinding = rich.BindYjs(richText, _richPeer);

                markdownText.observe((e, tr) => UpdateConvergence());
                richText.observe((e, tr) => UpdateConvergence());

                var latencyLabel = TextBlock($"Latency: {_latency} ms").Small();
                var latency = Slider(val: _latency, min: 0, max: 1500, step: 50).W(200).OnInput((s, e) =>
                {
                    _latency          = s.Value;
                    latencyLabel.Text = $"Latency: {_latency} ms";
                });

                var offline = Toggle("Offline").OnChange((t, e) => SetOfflineAsync(t.IsChecked).FireAndForget());

                Content = VStack().WS().Gap(8.px()).Children(
                    HStack().WS().AlignItemsCenter().Wrap().Gap(16.px()).Children(latencyLabel, latency, offline),
                    HStack().WS().Gap(16.px()).Children(
                        VStack().W(10).Grow().Gap(6.px()).Children(Label("Ada - Markdown (Monaco)"), code.WS().H(380)),
                        VStack().W(10).Grow().Gap(6.px()).Children(Label("Grace - rich text (Squire)"), rich.WS().H(380))),
                    _convergence)
                   .WhenRemoved(() =>
                    {
                        codeBinding.Dispose();
                        richBinding.Dispose();
                        _markdownProvider.Dispose();
                        _richProvider.Dispose();
                        _markdownPeer.destroy();
                        _richPeer.destroy();
                        rich.Destroy();
                        _markdownDoc.destroy();
                        _richDoc.destroy();
                    });

                ConnectAsync().FireAndForget();
                UpdateConvergence();
            }

            private static IComponent Label(string text) => TextBlock(text).SemiBold().Small();

            private Task Send(Func<YjsSyncProvider> to, Transpose.Core.es5.Uint8Array message)
            {
                window.setTimeout(_ =>
                {
                    if (!_offline) to().Receive(message);
                }, _latency);

                return Task.FromResult(0);
            }

            private async Task ConnectAsync()
            {
                await _markdownProvider.ConnectAsync();
                await _richProvider.ConnectAsync();
            }

            private async Task SetOfflineAsync(bool offline)
            {
                _offline = offline;

                if (offline)
                {
                    _markdownProvider.Disconnect();
                    _richProvider.Disconnect();
                }
                else
                {
                    await ConnectAsync();
                }

                UpdateConvergence();
            }

            private void UpdateConvergence()
            {
                var same = _markdownDoc.getText("markdown").asString() == _richDoc.getText("markdown").asString();

                _convergence.Text = same
                    ? "Both documents hold the same Markdown."
                    : _offline ? "The documents differ: they are offline." : "The documents differ: updates are in flight.";
            }
        }
    }
}
