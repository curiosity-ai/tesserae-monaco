using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Editors", Order = 2, Icon = UIcons.CodeCompare)]
    public class DiffViewerSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public DiffViewerSample()
        {
            var diff = MonacoEditor.Diff()
               .SetLanguage("csharp")
               .SetContent(SampleCode.CSharp, SampleCode.CSharpChanged);

            var sideBySide = true;

            var toggle = Button("Show inline");

            toggle.OnClick(() =>
            {
                sideBySide = !sideBySide;
                diff.SideBySide(sideBySide);
                toggle.SetText(sideBySide ? "Show inline" : "Show side by side");
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(DiffViewerSample), UIcons.CodeCompare, "Two documents compared, side by side or inline")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("MonacoEditor.Diff() returns a DiffViewer wrapping Monaco's diff editor. Hand it the two documents with .SetContent(original, modified) - or set .Original / .Modified individually - and Monaco computes and renders the difference."),
                        TextBlock("The diff itself is computed by Monaco's editor worker, so the decorations appear a moment after the panes do. That worker is loaded on demand by the bundle; nothing has to be configured for it.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Side by side reads best on a wide surface; inline is friendlier in a narrow panel or on a phone. .SideBySide(bool) and .Inline() switch between them at runtime, so a single viewer can follow the available width."),
                        TextBlock("The two models belong to the viewer, which disposes them when it unmounts - Monaco does not dispose models handed to setModel, so hand-rolled diff editors leak a pair per render. .IgnoreTrimWhitespace(false) makes whitespace-only edits visible, which matters when reviewing generated or reformatted code.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Switch layouts, and step through the changes with the navigation buttons."),
                        diff.WS().H(300.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            toggle,
                            Button("Next change").OnClick(() => diff.GoToNextDifference()),
                            Button("Previous change").OnClick(() => diff.GoToPreviousDifference()),
                            Button("Show whitespace changes").OnClick(() => diff.IgnoreTrimWhitespace(false)),
                            Button("Ignore whitespace").OnClick(() => diff.IgnoreTrimWhitespace()))
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CodeViewerSample), typeof(CodeEditorSample), typeof(EditorOptionsSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
