using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 5, Icon = UIcons.LightbulbOn)]
    public class CodeActionsSample : IComponent, ISample
    {
        private const string TODO_MESSAGE = "Unresolved TODO.";

        private readonly IComponent _content;

        public CodeActionsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// TODO: finish this\nvar total = Sum(1, 2);\n\n// TODO: and this one too\nvar name = \"world\";\n");

            // A quick fix answers a marker, so the page needs something producing markers first.
            editor.ValidateAsYouType(code => Task.FromResult<ReadOnlyArray<CodeDiagnostic>>(FindTodos(code)));

            editor.OnCodeActions(context =>
            {
                var actions = new List<CodeAction>();

                // Only the markers Monaco asked about - the ones on the line the caret is on.
                foreach (var marker in context.Markers)
                {
                    if (marker.message != TODO_MESSAGE) continue;

                    actions.Add(new CodeAction
                    {
                        title       = "Remove the TODO comment",
                        isPreferred = true,
                        diagnostics = new[] { marker },
                        edits       = new[]
                        {
                            new TextEdit
                            {
                                range = Ranges.Of(marker.startLineNumber, 1, marker.startLineNumber + 1, 1),
                                text  = ""
                            }
                        }
                    });
                }

                return Task.FromResult(actions.ToArray());
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(CodeActionsSample), UIcons.LightbulbOn, "The lightbulb, and the edits behind it")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Give the editor .OnCodeActions(context => ...) and Monaco offers a lightbulb wherever the delegate returns something. A CodeAction is a title, the markers it resolves, and the edits to apply - the component turns those into a Monaco workspace edit, so accepting one lands as a single undoable step."),
                        TextBlock("The context carries the range Monaco is asking about and the markers inside it, which is what makes this the natural pair to a validator: match on the marker you produced and offer the fix for it.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Gate on the marker rather than re-analysing the text. Monaco asks for actions on every caret move, so a provider that re-parses the document makes the editor feel heavy - the markers have already done that work."),
                        TextBlock("Set isPreferred on the one action that should run under Ctrl+. without a menu, and attach the diagnostics the action resolves so Monaco can group the fix under the problem it belongs to.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Both TODO lines squiggle about a second after load. Put the caret on one and press Ctrl+. - or use the button - and the fix deletes that line."),
                        editor.WS().H(200.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).PT(8).Children(
                            Button("Quick fix on line 1").SetIcon(UIcons.LightbulbOn).OnClick(() =>
                            {
                                editor.SetPosition(new Position { lineNumber = 1, column = 6 });
                                editor.Focus();
                                editor.ShowQuickFixes();
                            }),
                            Button("Reset").OnClick(() => editor.SetText("// TODO: finish this\nvar total = Sum(1, 2);\n\n// TODO: and this one too\nvar name = \"world\";\n"))),
                        SampleHint("Accepting the fix is one undo step: Ctrl+Z puts the line back.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(DiagnosticsSample), typeof(FormattingSample), typeof(SignatureHelpSample));
        }

        private static CodeDiagnostic[] FindTodos(string code)
        {
            var diagnostics = new List<CodeDiagnostic>();
            var lines       = (code ?? "").Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var index = lines[i].IndexOf("TODO", StringComparison.Ordinal);

                if (index < 0) continue;

                diagnostics.Add(new CodeDiagnostic(i, index, i, index + "TODO".Length, TODO_MESSAGE, MarkerSeverity.Warning));
            }

            return diagnostics.ToArray();
        }

        public HTMLElement Render() => _content.Render();
    }
}
