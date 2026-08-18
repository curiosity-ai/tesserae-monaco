using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 8, Icon = UIcons.Compress)]
    public class FoldingSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public FoldingSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("ini")
               .SetText(SampleCode.Annotated)
               .Folding();

            // `region ... endregion` is not something the ini tokenizer knows about, so the ranges have
            // to come from a provider.
            editor.OnFoldingRanges(text =>
            {
                var ranges = new List<FoldingRange>();
                var lines  = (text ?? "").Replace("\r\n", "\n").Split('\n');
                var open   = -1;

                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("region ")) open = i + 1;

                    if (lines[i].StartsWith("endregion") && open > 0)
                    {
                        ranges.Add(new FoldingRange { start = open, end = i + 1, kind = FoldingRangeKind.Region });
                        open = -1;
                    }
                }

                return Task.FromResult(ranges.ToArray());
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(FoldingSample), UIcons.Compress, "Foldable regions your own syntax defines")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Monaco folds by indentation on its own, which is wrong for any syntax whose blocks are not indented. .OnFoldingRanges(text => ...) replaces that guess: return a start line, an end line and a kind, and the chevrons appear in the margin where you say they should."),
                        TextBlock("FoldingRangeKind.Region, Comment and Imports are the three Monaco knows - naming the right one is what makes \"fold all regions\" and \"fold all comments\" behave.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Folding has to be enabled on the editor for a provider to be visible at all - .Folding() does that, and it is on by default in Monaco. Ranges are one-based line numbers and may nest, but must not partly overlap; Monaco drops a range that crosses another."),
                        TextBlock("The provider is asked again after every edit, so keep it cheap and tolerant of a half-typed document - an unterminated region is normal while the user is typing, and returning nothing for it is the right answer.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Both region blocks fold, even though nothing in them is indented. The chevrons are in the margin next to the region lines."),
                        editor.WS().H(240.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).PT(8).Children(
                            Button("Fold all").SetIcon(UIcons.Compress).OnClick(() => { editor.Focus(); editor.RunAction("editor.foldAll"); }),
                            Button("Unfold all").OnClick(() => { editor.Focus(); editor.RunAction("editor.unfoldAll"); })),
                        SampleHint("Ctrl+K Ctrl+0 folds everything and Ctrl+K Ctrl+J unfolds it, the same as in VS Code.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(InlayHintsAndLensesSample), typeof(LinksAndColorsSample), typeof(CustomLanguageSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
