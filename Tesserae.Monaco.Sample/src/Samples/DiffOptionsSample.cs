using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Editors", Order = 3, Icon = UIcons.ListTree)]
    public class DiffOptionsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public DiffOptionsSample()
        {
            var padding = "\n// a long run of identical lines, so there is something to collapse\n";

            for (var i = 0; i < 12; i++)
            {
                padding += "// line " + i + "\n";
            }

            // The two methods swap places, which is a move rather than a delete and an insert.
            var left  = "int Twice(int v) { return v * 2; }" + padding + "int Half(int v) { return v / 2; }\n";
            var right = "int Half(int v) { return v / 2; }" + padding + "int Twice(int v) { return v * 3; }\n";

            var diff = MonacoEditor.Diff()
               .SetLanguage("csharp")
               .SetContent(left, right)
               .ShowMoves()
               .RenderMarginRevertIcon()
               .OriginalEditable();

            var status    = TextBlock("computing...").Small().Secondary();
            var collapsed = false;

            // The diff is computed on a worker, so the change count is only meaningful from here.
            diff.OnDiffUpdated(() =>
            {
                status.Text = diff.ChangeCount + " changed block(s)" + (diff.IsIdentical ? " - identical" : "");
            });

            diff.OnRendered(d =>
            {
                // Both sides are surfaces, so the baseline can be watched even though it is the left one.
                d.OriginalSide.OnContentChanged(e => status.Text = "baseline edited - v" + e.versionId);
            });

            var collapse = Button("Collapse unchanged");

            collapse.OnClick(() =>
            {
                collapsed = !collapsed;
                diff.HideUnchangedRegions(collapsed);
                collapse.SetText(collapsed ? "Expand unchanged" : "Collapse unchanged");
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(DiffOptionsSample), UIcons.ListTree, "What a diff can do beyond showing two files")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("The diff editor has an API of its own beyond the two documents. .ShowMoves() marks a block that moved rather than reporting a delete and an insert, .HideUnchangedRegions(...) collapses the runs neither side touched, .RenderMarginRevertIcon() puts an arrow in the margin that takes a change back, and .OriginalEditable() makes the baseline editable too - for a review pane where both sides are live."),
                        TextBlock(".GoToNextDifference() walks the changes, and .ChangeCount and .IsIdentical say what the diff came to - the latter honouring ignoreTrimWhitespace, so it can be true for two texts that are not byte-identical.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("The diff runs on Monaco's editor worker, which is why .ChangeCount is zero right after setting the content and only becomes meaningful inside .OnDiffUpdated(...). Reading it any earlier measures a diff that has not been computed yet - and looks exactly like a broken worker."),
                        TextBlock("Both sides are ordinary editor surfaces, reachable as .OriginalSide and .ModifiedSide, so everything the code editor can do - events, decorations, providers - works on either. The two models are the component's to dispose, which it does: Monaco does not dispose models handed to setModel.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Twice and Half swap places and Twice changes its multiplier, with fourteen identical lines between them. Collapse the unchanged region, walk the changes, and edit the left pane."),
                        diff.WS().H(280.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            collapse,
                            Button("Next change").OnClick(() => diff.GoToNextDifference()),
                            status.PL(8.px())),
                        SampleHint("The change count arrives a moment after the page does - that is the worker answering, not a slow render.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(DiffViewerSample), typeof(CodeViewerSample), typeof(EditorOptionsSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
