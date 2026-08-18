using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 2, Icon = UIcons.Bug)]
    public class DiagnosticsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public DiagnosticsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SampleCode.CSharp);

            // Stands in for a server-side compile; the debounce and the staleness handling are the
            // component's job, so a real validator looks exactly like this.
            editor.ValidateAsYouType(code => Task.FromResult<ReadOnlyArray<CodeDiagnostic>>(FindTodos(code)));

            var json = MonacoEditor.Editor()
               .SetLanguage("json")
               .SetText(SampleCode.Json);

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(DiagnosticsSample), UIcons.Bug, "Squiggles from a validator that runs as you type")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A CodeDiagnostic is a range, a message and a severity. Push a set with .SetDiagnostics(...) whenever you have one, or hand .ValidateAsYouType(...) a delegate and the component runs it for you - debounced while typing, and with stale answers dropped when a newer edit has already landed."),
                        TextBlock("Diagnostics are Monaco markers, so they squiggle in the text, mark the overview ruler, and appear in the editor's own problem hover. Nothing else has to be drawn.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Line and character offsets on CodeDiagnostic are zero-based, which is what a compiler usually reports; the component converts them to Monaco's one-based positions. Severities are Error, Warning, Info and Hint - reserve Error for something that will actually fail."),
                        TextBlock("Monaco's own workers already validate the languages they know: json, typescript, css and html produce markers with no help from you. Your validator adds to those rather than replacing them.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("A validator of your own"),
                        TextBlock("This one flags every TODO. Type one on any line - the squiggle appears about a second after you stop typing."),
                        editor.WS().H(220.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            Button("Validate now").OnClick(() => editor.Validate()),
                            Button("Clear markers").OnClick(() => editor.ClearMarkers())),
                        SampleSubTitle("Monaco's own worker"),
                        TextBlock("No validator is attached here - the json language worker produces the markers. The document below is valid, so it starts clean; break it and the errors appear."),
                        json.WS().H(160.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            Button("Break the JSON").OnClick(() => json.SetText("{ \"name\": , }")),
                            Button("Restore").OnClick(() => json.SetText(SampleCode.Json)))
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(FormattingSample), typeof(CompletionAndHoverSample), typeof(CodeEditorSample));
        }

        private static CodeDiagnostic[] FindTodos(string code)
        {
            var diagnostics = new List<CodeDiagnostic>();
            var lines       = (code ?? "").Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var index = lines[i].IndexOf("TODO", StringComparison.Ordinal);

                if (index < 0) continue;

                diagnostics.Add(new CodeDiagnostic(
                    startLine:      i,
                    startCharacter: index,
                    endLine:        i,
                    endCharacter:   index + "TODO".Length,
                    message:        "Unresolved TODO.",
                    severity:       MarkerSeverity.Warning));
            }

            return diagnostics.ToArray();
        }

        public HTMLElement Render() => _content.Render();
    }
}
