using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 2, Icon = UIcons.WindowMaximize)]
    public class ModalSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public ModalSample()
        {
            _content = SectionStack().Secondary()
               .SampleTitle(typeof(ModalSample), UIcons.WindowMaximize, "An editor inside a clipping ancestor")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Monaco renders its suggest list, its hover card and its parameter hints as widgets inside the editor's own DOM. Put that editor inside anything with overflow: hidden - a modal, a panel, a split view - and those popups are clipped at the container's edge."),
                        TextBlock("Every component in this package sets fixedOverflowWidgets and points overflowWidgetsDomNode at a single body-mounted host, so the popups escape. This page is the regression check for that: if a suggest list is ever cut off by a surface, it shows up here first.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Give the editor a height the surface can satisfy. Inside a modal that usually means sizing the modal (.W(70.vw()).H(60.vh()) below) and stretching the editor into it with .WS().HS(), rather than pinning the editor to a pixel height the surface may not have."),
                        TextBlock("The same applies to a Panel, a Dialog or either split view - nothing here is modal-specific. The one thing to avoid is a second overflow host of your own: one shared host is what keeps the popups above every layer.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Open the modal and press Ctrl+Space on the last line. The suggest list must be fully visible, including the part that falls outside the modal's box."),
                        HStack().WS().PT(8).Children(
                            Button("Open editor in a modal").SetIcon(UIcons.WindowMaximize).OnClick(OpenModal)),
                        Banner("Known issue: on this build the page stops producing frames while a modal containing an editor is open - Tesserae's 0.3s modal animation and Monaco's compositing layers wedge each other. The editor itself is healthy; suppressing the animation removes the stall entirely. See the repository's CLAUDE.md for the full measurement.")
                           .Warning()
                           .MT(16)
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CompletionAndHoverSample), typeof(AutoHeightSample), typeof(EditorOptionsSample));
        }

        private static void OpenModal()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// Ctrl+Space - the suggest popup must not be clipped by the modal\nvar x = Gr\n");

            editor.OnCompletion(context => Task.FromResult(new[]
            {
                new CompletionItem { label = "Greeter", kind = CompletionItemKind.Class },
                new CompletionItem { label = "Greet",   kind = CompletionItemKind.Method }
            }));

            Modal("Editor in a modal")
               .W(70.vw())
               .H(60.vh())
               .Content(editor.WS().HS())
               .Show();
        }

        public HTMLElement Render() => _content.Render();
    }
}
