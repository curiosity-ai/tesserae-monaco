using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 2, Icon = UIcons.Ghost)]
    public class InlineCompletionSample : IComponent, ISample
    {
        private const string START = "int Twice(int value)\n{\n    return\n}\n";

        private readonly IComponent _content;

        public InlineCompletionSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(START);

            editor.OnInlineCompletion(context =>
            {
                // Ghost text only where it makes sense - right after `return `. A suggestion on every
                // keystroke is what makes this feature feel noisy.
                if (context.TextUntilPosition.EndsWith("return "))
                {
                    return Task.FromResult(new[] { new InlineCompletion { insertText = "value * 2;" } });
                }

                return Task.FromResult<InlineCompletion[]>(null);
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(InlineCompletionSample), UIcons.Ghost, "Ghost text, accepted with Tab")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Give the editor .OnInlineCompletion(context => ...) and what it returns is rendered as dimmed ghost text after the caret, which Tab accepts and Escape dismisses. This is the mechanism a completion model plugs into - the package supplies the wiring and nothing else."),
                        TextBlock("An InlineCompletion is text plus, optionally, the range it replaces. Return null - not an empty array - where there is nothing to suggest, and Monaco leaves the line alone.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Decide from the text before the caret whether a suggestion is wanted at all. Monaco asks on every edit, so a provider that always answers puts ghost text in the user's way; the interesting positions are usually after an assignment, an opening brace or a return."),
                        TextBlock("Anything slow belongs behind a cancellation check: the context's token is cancelled as soon as the caret moves, and resolving late paints a suggestion for a position the user has already left. Ghost text and the suggest widget are independent - both can be live at once.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Type a space after the return on line 3 and the ghost text appears. Tab accepts it, Escape dismisses it. The button does the same edit from code and then asks Monaco for the suggestion, because an edit the user did not type does not trigger one on its own."),
                        editor.WS().H(160.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            Button("Type a space after 'return'").SetIcon(UIcons.Ghost).OnClick(() =>
                            {
                                editor.SetText(START);
                                editor.Focus();
                                editor.SetPosition(new Position { lineNumber = 3, column = 11 });

                                editor.ApplyEdits(new[] { new TextEdit { range = Ranges.Of(3, 11, 3, 11), text = " " } });

                                // Monaco asks for inline completions when the *user* types, and an edit
                                // applied from code is not that - so the request has to be made explicitly.
                                editor.Trigger("editor.action.inlineSuggest.trigger");
                            }),
                            Button("Reset").OnClick(() => editor.SetText(START))),
                        SampleHint("Type anywhere else and nothing is offered - the delegate only answers after 'return '.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CompletionAndHoverSample), typeof(SignatureHelpSample), typeof(CodeActionsSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
