using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 3, Icon = UIcons.ObjectsColumn)]
    public class SeveralDocumentsSample : IComponent, ISample
    {
        private const string FIRST_URI  = "inmemory://sample/first.cs";
        private const string SECOND_URI = "inmemory://sample/second.json";

        private const string SECOND_TEXT = "{\n  \"file\": \"second.json\",\n  \"note\": \"a different document, with its own undo history\"\n}\n";

        private readonly IComponent _content;

        public SeveralDocumentsSample()
        {
            var editor = MonacoEditor.Editor();

            CodeModel first  = null;
            CodeModel second = null;

            EditorViewState firstState  = null;
            EditorViewState secondState = null;

            var showingFirst = true;
            var status       = TextBlock("").Small().Secondary();

            // The models cannot exist before Monaco has loaded, so they are created on first render.
            editor.OnRendered(e =>
            {
                first  = EnsureModel(FIRST_URI, SampleCode.Navigable + "\n\n// first.cs - scroll me, then switch and come back\n" + SampleCode.Order, "csharp");
                second = EnsureModel(SECOND_URI, SECOND_TEXT, "json");

                e.SetModel(first);

                showingFirst = true;
                status.Text  = "showing first.cs";
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(SeveralDocumentsSample), UIcons.ObjectsColumn, "One editor, several documents, each keeping its place")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A model is the document; the editor is only a view onto one. MonacoEditor.CreateModel(text, language, uri) makes one, .SetModel(...) shows it, and each model carries its own text, language and undo history - so an app with tabs wants one editor and one model per open file, not one editor per file."),
                        TextBlock("Creating a second editor to show a second file costs a full Monaco instance: its own DOM, its own view, its own listeners. Switching models costs a repaint.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Monaco does not remember where the caret was in the document you switched away from - saving .SaveViewState() before the switch and passing it to .RestoreViewState(...) after is what keeps the caret, the scroll offset and the folding state per document."),
                        TextBlock("Models handed to an editor are yours to dispose: the editor does not dispose them when it is torn down, which is what lets a host switch back to one. Monaco also throws when a URI is claimed twice, so a page that is rebuilt on every visit - like this one - has to reuse the model it already made.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Scroll down in first.cs and put the caret somewhere, switch to second.json, then switch back - the caret and the scroll offset are where you left them."),
                        editor.WS().H(220.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Switch document").SetIcon(UIcons.ObjectsColumn).OnClick(() =>
                            {
                                if (first is null || second is null) return;

                                // Saving before the switch and restoring after is what keeps each
                                // document's caret, scroll offset and folding - Monaco does not do it.
                                if (showingFirst)
                                {
                                    firstState = editor.SaveViewState();
                                    editor.SetModel(second).RestoreViewState(secondState);
                                }
                                else
                                {
                                    secondState = editor.SaveViewState();
                                    editor.SetModel(first).RestoreViewState(firstState);
                                }

                                showingFirst = !showingFirst;
                                status.Text  = "showing " + (showingFirst ? "first.cs" : "second.json") + " - " + editor.LineCount + " lines";
                            }),
                            Button("Insert a line (undoable)").OnClick(() =>
                            {
                                // ApplyEdits rather than the Text setter: the undo stack and caret survive.
                                editor.ApplyEdits(new[]
                                {
                                    new TextEdit { range = Ranges.Of(1, 1, 1, 1), text = "// inserted, and Ctrl+Z still works\n" }
                                });
                            }),
                            Button("Undo").OnClick(() => editor.Undo()),
                            status.PL(8.px())),
                        SampleHint("The language follows the model, so the syntax highlighting switches from C# to JSON with it.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(RemountSample), typeof(EventsSample), typeof(CodeEditorSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
