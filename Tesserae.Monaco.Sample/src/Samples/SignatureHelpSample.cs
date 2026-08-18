using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 1, Icon = UIcons.FunctionSquare)]
    public class SignatureHelpSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public SignatureHelpSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// put the caret between the parens, or type a comma\nvar total = Sum(1, 2);\n");

            // A real host would ask its compiler which overload is being called; this one always answers
            // with the same signature and only works out which parameter the caret is in.
            editor.OnSignatureHelp(context => Task.FromResult(new SignatureHelp
            {
                signatures = new[]
                {
                    new SignatureInformation
                    {
                        label         = "Sum(int first, int second)",
                        documentation = new MarkdownString { value = "Adds two numbers." },
                        parameters    = new[]
                        {
                            new ParameterInformation { label = "int first",  documentation = new MarkdownString { value = "The left operand." } },
                            new ParameterInformation { label = "int second", documentation = new MarkdownString { value = "The right operand." } }
                        }
                    }
                },
                activeSignature = 0,
                activeParameter = CountCommasBefore(context)
            }));

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(SignatureHelpSample), UIcons.FunctionSquare, "Parameter hints inside the parens")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Give the editor .OnSignatureHelp(context => ...) and Monaco shows the parameter-hints widget while the caret is inside a call. The delegate returns a SignatureHelp: the overloads on offer, which one is active, and which parameter the caret is in - Monaco bolds that one and shows its documentation."),
                        TextBlock("The context is the same CodeContext every provider gets, so the text up to the caret is what tells you which call and which argument you are in.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Monaco opens the widget on '(' and ',' by default and closes it on ')'. Recompute activeParameter on every request rather than caching it: the widget is asked again on each keystroke inside the call, which is exactly what makes the bolding follow the caret."),
                        TextBlock("The widget is one of the popups hosted in the shared body-mounted overflow node, so it is not clipped by a modal or a panel. Checking it in a browser means looking for a non-zero bounding rect, not offsetParent - the host element has no size of its own.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Click between the parens of Sum(1, 2) and press Ctrl+Shift+Space, or use the button. Type a comma and the bolded parameter moves to the second one."),
                        editor.WS().H(160.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            Button("Show parameter hints").SetIcon(UIcons.FunctionSquare).OnClick(() =>
                            {
                                editor.SetPosition(new Position { lineNumber = 2, column = 18 });
                                editor.Focus();
                                editor.ShowParameterHints();
                            })),
                        SampleHint("Ctrl+Shift+Space is Monaco's own keybinding for this on every platform.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CompletionAndHoverSample), typeof(InlineCompletionSample), typeof(CodeActionsSample));
        }

        /// <summary>Which argument the caret is in, as far as counting commas can tell.</summary>
        private static int CountCommasBefore(CodeContext context)
        {
            var text = context.TextUntilPosition ?? "";
            var open = text.LastIndexOf('(');

            if (open < 0) return 0;

            var commas = 0;

            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == ',') commas++;
            }

            return commas;
        }

        public HTMLElement Render() => _content.Render();
    }
}
