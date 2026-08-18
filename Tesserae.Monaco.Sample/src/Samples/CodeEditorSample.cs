using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Editors", Order = 0, Icon = UIcons.FileCode)]
    public class CodeEditorSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public CodeEditorSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SampleCode.CSharp)
               .WordWrap();

            var status = TextBlock("unchanged").Small().Secondary();

            editor.OnChanged(() => status.Text = $"{editor.Text.Length} characters");

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(CodeEditorSample), UIcons.FileCode, "An editable Monaco editor, sized and themed by Tesserae")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("MonacoEditor.Editor() returns a CodeEditor: a full Monaco instance that follows the active Tesserae theme, re-lays itself out when its container resizes, and disposes its model and its language providers when it is unmounted."),
                        TextBlock("It is an IComponent like any other, so the usual sizing helpers apply - .WS().H(220.px()) below. Monaco needs a height it can measure, so give it one, or use Auto Height to let it grow to fit its content instead.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Read and write the document through .Text / .SetText(...), and react to edits with .OnChanged(...). Move the caret with .SetPosition(...) and bring a line into view with .RevealLine(...); both are no-ops until the editor has mounted, so they are safe to call while the page is still being built."),
                        TextBlock("Editing can be switched off at any time with .ReadOnly(). For a document that is never editable prefer the Code Viewer, which starts without the editing affordances rather than hiding them afterwards.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Type in the editor and the character count follows. Word wrap is on - Alt+Z, or the editor's own context menu, toggles it."),
                        editor.WS().H(220.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            Button("Go to line 5").OnClick(() => editor.RevealLine(5).SetPosition(new Position { lineNumber = 5, column = 1 }).Focus()),
                            Button("Read-only").OnClick(() => editor.ReadOnly()),
                            Button("Editable").OnClick(() => editor.ReadOnly(false)),
                            status.PL(8.px()))
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CodeViewerSample), typeof(AutoHeightSample), typeof(EditorOptionsSample), typeof(DiagnosticsSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
