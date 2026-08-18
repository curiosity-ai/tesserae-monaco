using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Editors", Order = 4, Icon = UIcons.Expand)]
    public class AutoHeightSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public AutoHeightSample()
        {
            var viewer = MonacoEditor.Viewer(autoHeight: true)
               .SetLanguage("json")
               .SetText(SampleCode.Json);

            var editor = MonacoEditor.Editor(autoHeight: true)
               .SetLanguage("csharp")
               .SetText("// Add and remove lines - the editor's height follows.\nvar x = 1;\n");

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(AutoHeightSample), UIcons.Expand, "An editor that grows to fit its content instead of scrolling")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Both factories take an autoHeight flag: MonacoEditor.Editor(autoHeight: true) and MonacoEditor.Viewer(autoHeight: true). The component then sizes itself to the content it holds, so no vertical scrollbar of its own ever appears."),
                        TextBlock("This is what makes a snippet sit in a page of prose the way a paragraph does, and it is the right choice inside an already-scrolling column: two nested scrollbars are worse than one long page.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("The parent has to be able to grow too, otherwise the editor is simply clipped at whatever height the parent allows - so do not combine autoHeight with a fixed .H(...) on the editor or on a container around it."),
                        TextBlock("Prefer a fixed height for a long or unbounded document: a thousand-line file rendered at full height defeats Monaco's virtualised rendering, which is the thing that makes it fast on large files in the first place.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("A viewer that fits its document"),
                        TextBlock("No height is set on this viewer; it is exactly as tall as the five lines it holds."),
                        viewer.WS().MT(8),
                        SampleSubTitle("An editor that grows as you type"),
                        TextBlock("Press Enter a few times: the editor gets taller, and the page - not the editor - scrolls."),
                        editor.WS().MT(8)
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CodeViewerSample), typeof(CodeEditorSample), typeof(ModalSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
