using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 1, Icon = UIcons.BellRing)]
    public class EventsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public EventsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// click in, type, move the caret, then click away\nvar x = 1;\n");

            var focus   = TextBlock("focus: -").Small().Secondary();
            var caret   = TextBlock("caret: -").Small().Secondary();
            var changes = TextBlock("changes: -").Small().Secondary();

            editor
               .OnFocused(() => focus.Text = "focus: in")
               .OnBlurred(() => focus.Text = "focus: out (a host would save here)")
               .OnCursorPositionChanged(e => caret.Text = "caret: " + e.position.lineNumber + ":" + e.position.column)
               .OnContentChanged(e =>
                {
                    var first = e.changes is object && e.changes.Length > 0 ? e.changes[0].text : "";

                    changes.Text = "changes: v" + e.versionId + ", " + e.changes.Length + " edit(s), last inserted " + Quote(first);
                });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(EventsSample), UIcons.BellRing, "What the editor tells the host")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Every Monaco event is a fluent .On...(...) on the component: focus and blur, caret and selection moves, content changes, scrolling, key and mouse events, layout and configuration changes, and the model being swapped. Each returns the component, so they chain with the rest of the setup."),
                        TextBlock("The subscriptions are made when the editor is created and disposed with it, so there is nothing to unhook when the component is torn down - which is what makes them safe to attach while building a page.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("The content-changed event carries a version id, and that is the useful part: a host that sends the document somewhere can stamp the request with it and drop the answer if the version has moved on. That is exactly how .ValidateAsYouType(...) discards stale diagnostics."),
                        TextBlock("Blur is the right moment to save, not every keystroke. And nothing here is the place for heavy work - these fire on the main thread, several times per keystroke; hand anything slow to a debounce or a worker.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Click into the editor, type, move the caret with the arrow keys, then click outside it. The three lines below follow along."),
                        editor.WS().H(160.px()).MT(8),
                        VStack().WS().PT(8).Children(focus, caret, changes),
                        SampleHint("The version id only ever increases - it counts edits to the model, not characters.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(ActionsAndCommandsSample), typeof(SeveralDocumentsSample), typeof(DiagnosticsSample));
        }

        private static string Quote(string text)
        {
            if (string.IsNullOrEmpty(text)) return "nothing";

            return "\"" + text.Replace("\n", "\\n") + "\"";
        }

        public HTMLElement Render() => _content.Render();
    }
}
