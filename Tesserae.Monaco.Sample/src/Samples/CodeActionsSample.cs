using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 6, Icon = UIcons.LightbulbOn)]
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

            var answered  = 0;
            var cancelled = 0;
            var counts    = TextBlock("0 requests answered, 0 cancelled").Small().Secondary();

            editor.OnCodeActions(async context =>
            {
                // No marker under the caret, nothing to fix - and no round trip to find that out.
                if (context.Markers.Length == 0) return null;

                // A stand-in for a server round trip. Monaco cancels the request when the caret moves on,
                // and the token is what lets the "fetch" stop rather than finish for nobody.
                try
                {
                    await Task.Delay(300, context.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled++;
                    counts.Text = answered + " requests answered, " + cancelled + " cancelled";
                    throw;
                }

                answered++;
                counts.Text = answered + " requests answered, " + cancelled + " cancelled";

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

                return actions.ToArray();
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(CodeActionsSample), UIcons.LightbulbOn, "The lightbulb, and the edits behind it")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Give the editor .OnCodeActions(context => ...) and Monaco offers a lightbulb wherever the delegate returns something. A CodeAction is a title, the markers it resolves, and the edits to apply - the component turns those into a Monaco workspace edit, so accepting one lands as a single undoable step."),
                        TextBlock("The context carries the range Monaco is asking about and the markers inside it, which is what makes this the natural pair to a validator: match on the marker you produced and offer the fix for it.").MT(8),
                        TextBlock("It also carries a CancellationToken. Monaco asks again on every caret move and cancels the request before, so a handler that fetches its fixes from a server passes the token to the fetch - otherwise every caret move the user has already left behind is still computed in full.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Return early when Markers is empty, and gate on the marker rather than re-analysing the text. Monaco asks for actions on every caret move, so a provider that re-parses the document makes the editor feel heavy - the markers have already done that work."),
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
                        counts.MT(8),
                        SampleHint("Each answer waits 300ms, like a server would, and Monaco asks about 250ms after the caret stops. Click back and forth between the two TODO words, a little faster than that, and the cancelled count climbs: those requests stopped where they were. Accepting the fix is one undo step: Ctrl+Z puts the line back.")
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
