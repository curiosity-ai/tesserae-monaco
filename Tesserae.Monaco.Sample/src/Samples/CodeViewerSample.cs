using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Editors", Order = 1, Icon = UIcons.Eye)]
    public class CodeViewerSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public CodeViewerSample()
        {
            var viewer = MonacoEditor.Viewer()
               .SetLanguage("json")
               .SetText(SampleCode.Json);

            var byExtension = MonacoEditor.Viewer()
               .SetLanguageByExtension(".cs")
               .SetText(SampleCode.CSharp);

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(CodeViewerSample), UIcons.Eye, "Read-only code, with highlighting and selection")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("MonacoEditor.Viewer() returns a CodeViewer: the same Monaco instance as the editor, configured for displaying code rather than writing it. The line numbers, highlighting, folding and selection are all there; the cursor, the editing affordances and the overview ruler are not."),
                        TextBlock("Use it wherever code is output rather than input - a snippet in documentation, a log excerpt, a request or response body, the definition of the thing the user just clicked.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A viewer is still a real editor, so it is not a security boundary - it is a presentation choice. Call .Editable() to hand editing back at runtime, which is the cheap way to build a \"view, then edit\" affordance without swapping components."),
                        TextBlock("Set the language with .SetLanguage(\"json\") when you know it, or .SetLanguageByExtension(\".cs\") when all you have is a file name - the extension is resolved against Monaco's own language registry, so it covers every language the bundle ships.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("A JSON document"),
                        TextBlock("Read-only: the text can be selected and copied, but not typed into."),
                        viewer.WS().H(140.px()).MT(8),
                        SampleSubTitle("Language picked from a file extension"),
                        TextBlock("The same viewer, with .SetLanguageByExtension(\".cs\") instead of a language id."),
                        byExtension.WS().H(200.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(
                            Button("Make editable").OnClick(() => byExtension.Editable()),
                            Button("Back to read-only").OnClick(() => byExtension.Editable(false)))
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CodeEditorSample), typeof(AutoHeightSample), typeof(LanguagesAndThemesSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
