using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 6, Icon = UIcons.ArrowsRepeat)]
    public class RemountSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public RemountSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// edit me, put the caret somewhere, then detach and re-attach\nvar survives = true;\n");

            // Rendered into a slot this page owns, so the element can be pulled out of the DOM and put
            // back - which is what a router, a tab strip or a collapsing panel does to a component.
            var slot = DIV();
            slot.style.width  = "100%";
            slot.style.height = "160px";

            var rendered = editor.Render();
            slot.appendChild(rendered);

            var status = TextBlock("attached").Small().Secondary();

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(RemountSample), UIcons.ArrowsRepeat, "Leaving the DOM and coming back")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Monaco cannot survive being detached from the document - its measurements, its listeners and its layout all assume it is on screen. So the component tears the editor down when its element leaves the DOM and builds a new one when it comes back, restoring the text, the caret and the scroll offset in the process."),
                        TextBlock("That is what makes these components safe inside anything that mounts and unmounts: a route, a tab, a panel that collapses, a Tesserae Defer. Every page in this gallery relies on it - each one is built when it is opened and torn down when it is left.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("The state that survives is what the component tracks: text, language, options, the delegates, the caret and the scroll offset. Anything held on the raw editor - a decoration collection, a widget added through .Editor directly - goes with the editor, so add those through the component instead."),
                        TextBlock(".Editor is null while detached, which is why every accessor on the component tolerates it and why work that needs a live editor belongs in .OnRendered(...). A provider registration is disposed on teardown too: without that, each remount would leave a provider bound to a dead model behind.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Type something and leave the caret in the middle of it, then detach and re-attach. The text and the caret come back; the editor behind them is a new one."),
                        Raw(slot).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Detach").SetIcon(UIcons.ArrowsRepeat).OnClick(() =>
                            {
                                if (rendered.parentElement is object)
                                {
                                    rendered.remove();
                                    status.Text = "detached - the editor was torn down";
                                }
                            }),
                            Button("Re-attach").OnClick(() =>
                            {
                                if (rendered.parentElement is null)
                                {
                                    slot.appendChild(rendered);
                                    status.Text = "re-attached - text and caret restored";
                                }
                            }),
                            status.PL(8.px())),
                        SampleHint("Walking the sidebar does the same thing to every page here, which is why a bad teardown shows up as a console error while clicking around.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(ModalSample), typeof(SeveralDocumentsSample), typeof(AutoHeightSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
