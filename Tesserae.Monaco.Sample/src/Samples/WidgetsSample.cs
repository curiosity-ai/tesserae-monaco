using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Decorations and widgets", Order = 1, Icon = UIcons.Sticker)]
    public class WidgetsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public WidgetsSample()
        {
            SampleStyles.Ensure();

            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SampleCode.Order);

            var badge = DIV();
            badge.className   = SampleStyles.Widget;
            badge.textContent = "public API";

            // Anchored to a position in the text, so it scrolls with the line it belongs to.
            var contentWidget = new ContentWidget("sample.badge", badge, new Position { lineNumber = 6, column = 1 })
            {
                AllowEditorOverflow = true
            };

            var corner = DIV();
            corner.className   = SampleStyles.Overlay;
            corner.textContent = "read-only region";

            var zoneBody = DIV();
            zoneBody.className   = SampleStyles.Zone;
            zoneBody.textContent = "3 changes by two authors - a view zone the editor reflows around";

            var zone = new ViewZone(4, zoneBody, heightInLines: 2);

            editor
               .AddContentWidget(contentWidget)
               .AddOverlayWidget(new OverlayWidget("sample.corner", corner))
               .AddViewZone(zone);

            var line = 6;

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(WidgetsSample), UIcons.Sticker, "Your own DOM inside the editor")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Three ways to put an element of your own into an editor. A ContentWidget is anchored to a position in the text and scrolls with it - a blame annotation, an inline error badge. An OverlayWidget is pinned to a corner of the viewport and stays there. A ViewZone opens a band of empty space between two lines and fills it, and the editor reflows around it."),
                        TextBlock("All three take a plain HTMLElement, so anything you can build - including a rendered Tesserae component - can go inside one.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A content widget's id has to be unique within the editor: Monaco keys its bookkeeping on it, and a second widget with the same id replaces the first. Set AllowEditorOverflow when the widget is wider than the editor, or it is clipped at the edge."),
                        TextBlock("Moving a widget is two steps - change .Position, then .LayoutContentWidget(...) - because Monaco only re-reads the position when it is told to. View zones are addressed by the object you added, so keep the reference if you mean to remove one.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("The green badge is a content widget on line 6, the dark label in the corner is an overlay widget, and the blue band above line 4 is a view zone two lines tall."),
                        editor.WS().H(240.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).PT(8).Children(
                            Button("Move the badge down").OnClick(() =>
                            {
                                line = line >= editor.LineCount ? 1 : line + 1;
                                contentWidget.Position = new Position { lineNumber = line, column = 1 };
                                editor.LayoutContentWidget(contentWidget);
                            }),
                            Button("Close the view zone").OnClick(() => editor.RemoveViewZone(zone))),
                        SampleHint("Scroll the editor: the badge moves with its line, the corner label does not.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(DecorationsSample), typeof(CodeEditorSample), typeof(ModalSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
