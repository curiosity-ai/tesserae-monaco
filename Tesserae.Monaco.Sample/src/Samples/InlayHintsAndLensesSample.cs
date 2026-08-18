using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 7, Icon = UIcons.Notes)]
    public class InlayHintsAndLensesSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public InlayHintsAndLensesSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("ini")
               .SetText(SampleCode.Annotated);

            var clicked = TextBlock("no lens clicked yet").Small().Secondary();

            // Monaco asks for hints over the visible range, which is why the range is a parameter - a
            // real provider would only annotate what it was asked about.
            editor.OnInlayHints((text, range) =>
            {
                var hints = new List<InlayHint>();
                var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var equals = lines[i].IndexOf('=');

                    if (equals < 0) continue;

                    var value = lines[i].Substring(equals + 1).Trim();

                    hints.Add(new InlayHint
                    {
                        position     = new Position { lineNumber = i + 1, column = equals + 1 },
                        label        = value.StartsWith("#") ? ": color " : ": number ",
                        kind         = InlayHintKind.Type,
                        paddingRight = true
                    });
                }

                return Task.FromResult(hints.ToArray());
            });

            editor.OnCodeLenses(
                text =>
                {
                    var lenses = new List<CodeLensItem>();
                    var lines  = (text ?? "").Replace("\r\n", "\n").Split('\n');

                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (!lines[i].StartsWith("region ")) continue;

                        lenses.Add(new CodeLensItem
                        {
                            range   = Ranges.Line(i + 1),
                            title   = "collapse this region",
                            tooltip = "A code lens supplied from C#"
                        });
                    }

                    return Task.FromResult(lenses.ToArray());
                },

                // The second delegate is the command behind the lens - the component registers it with
                // Monaco and hands back the lens that was clicked.
                lens =>
                {
                    clicked.Text = "lens clicked on line " + lens.range.startLineNumber;
                    editor.SetPosition(new Position { lineNumber = lens.range.startLineNumber, column = 1 });
                    editor.RunAction("editor.fold");
                });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(InlayHintsAndLensesSample), UIcons.Notes, "Annotations Monaco asks you for, over the range in view")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("An inlay hint is read-only text Monaco paints between the characters - an inferred type, a parameter name - and comes from .OnInlayHints((text, range) => ...). A code lens is a clickable line above the code, and .OnCodeLenses(provider, onClick) takes both the lenses and the command that runs when one is clicked."),
                        TextBlock("Neither is part of the document: no edit is involved, nothing is added to the text, and both disappear with the provider.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Both providers are asked for the visible range, repeatedly, as the user scrolls - so answer from something already computed rather than parsing the document each time. The range argument is there to be used."),
                        TextBlock("Neither renders for an editor that is not actually on screen, and inlay hints often need the editor focused before Monaco asks at all. That is worth knowing before concluding a provider is dead: scroll the editor into view and click into it first.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Every key gets a \": color\" or \": number\" hint before its value, and each region line carries a lens that folds it. Click one."),
                        editor.WS().H(240.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            Button("Toggle inlay hints").SetIcon(UIcons.Notes).OnClick(() => editor.RunAction("editor.action.toggleInlayHints")),
                            clicked.PL(8.px())),
                        SampleHint("The lens runs Monaco's own editor.fold action, so the code lens and the Folding page are two halves of the same thing.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(FoldingSample), typeof(LinksAndColorsSample), typeof(DecorationsSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
