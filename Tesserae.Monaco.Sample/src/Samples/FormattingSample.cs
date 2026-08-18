using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 1, Icon = UIcons.TextCheck)]
    public class FormattingSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public FormattingSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SampleCode.Messy);

            // A real host would call its server's formatter here; this only proves the pathway.
            editor.OnFormat(code => Task.FromResult(Tidy(code)));

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(FormattingSample), UIcons.TextCheck, "Format Document, answered by a delegate you supply")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Give the editor .OnFormat(code => ...) and it registers both of Monaco's formatting providers, document and range. The delegate receives the text and returns the formatted text; the component turns the difference into the edits Monaco applies, so the user's undo stack and cursor survive."),
                        TextBlock("Anything can produce that text - a server round-trip, a JavaScript formatter, or the whitespace tidier below. The component does not care, which is what keeps the package free of a bundled formatter.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("The keybinding for Format Document is VS Code's, and it is not the same everywhere: Shift+Alt+F on Windows, Shift+Option+F on macOS, and Ctrl+Shift+I on Linux. A formatter that looks broken on Linux is usually the wrong key, not a wrong delegate."),
                        TextBlock("Format Selection is Ctrl+K Ctrl+F on every platform. Both actions also sit in the editor's context menu, which is the discoverable route and the one worth pointing users at.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("This formatter trims trailing whitespace and collapses runs of blank lines. Right-click and pick Format Document, or press the platform's keybinding."),
                        editor.WS().H(200.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            Button("Format document").SetIcon(UIcons.TextCheck).OnClick(() => RunFormatDocument(editor)),
                            Button("Mess it up again").OnClick(() => editor.SetText(SampleCode.Messy))),
                        SampleHint("Format Document: Ctrl+Shift+I on Linux, Shift+Alt+F on Windows, Shift+Option+F on macOS. Format Selection: Ctrl+K Ctrl+F everywhere.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CompletionAndHoverSample), typeof(DiagnosticsSample), typeof(CodeEditorSample));
        }

        /// <summary>
        /// The same code path the keybinding takes, without any keyboard involved - handy for
        /// checking a formatter on a platform whose Format Document key you do not know.
        /// </summary>
        private static void RunFormatDocument(CodeEditor editor)
        {
            var action = editor.Editor is object ? editor.Editor.getAction("editor.action.formatDocument") : null;

            if (action is object) action.run();
        }

        /// <summary>Trims trailing whitespace and collapses runs of blank lines - a stand-in for a real formatter.</summary>
        private static string Tidy(string code)
        {
            var lines  = (code ?? "").Replace("\r\n", "\n").Split('\n');
            var result = new List<string>();
            var blanks = 0;

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();

                if (trimmed.Length == 0)
                {
                    blanks++;
                    if (blanks > 1) continue;
                }
                else
                {
                    blanks = 0;
                }

                result.Add(trimmed);
            }

            return string.Join("\n", result.ToArray());
        }

        public HTMLElement Render() => _content.Render();
    }
}
