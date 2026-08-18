using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Editors", Order = 5, Icon = UIcons.PaintBrush)]
    public class ColorizeSample : IComponent, ISample
    {
        private const string SNIPPET = "var greeting = \"hello\";\nvar count = 42; // no editor was created for this";

        private readonly IComponent _content;

        public ColorizeSample()
        {
            var host = DIV();

            // Nothing on this page creates an editor, and it is a component mounting that normally starts
            // the loader - so this page has to ask for Monaco itself, or WhenLoaded waits forever.
            MonacoEditor.LoadAsync();

            // No editor, no model, no view - just highlighted markup. The element fills in when
            // Monaco's colorizer resolves, which is why this is a callback rather than a return value.
            MonacoEditor.WhenLoaded(() =>
            {
                var colorized = MonacoEditor.Colorize(SNIPPET, "csharp");

                colorized.style.fontFamily = "'Cascadia Code', Consolas, monospace";
                colorized.style.fontSize   = "12px";

                host.appendChild(colorized);
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(ColorizeSample), UIcons.PaintBrush, "Highlighted code with no editor at all")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("MonacoEditor.Colorize(code, language) runs Monaco's tokenizer over a string and hands back an element of highlighted markup - no editor, no model, no view, and none of the memory or listeners one of those brings. ColorizeAsync(...) returns the HTML instead, and ColorizeElement(...) highlights the code already inside an element in place."),
                        TextBlock("This is the right tool for code nobody will interact with: a snippet in documentation, a log line, a value in a table, a preview in a list. A read-only editor would work too, and would cost several hundred times as much.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Colorizing needs Monaco loaded, and a page usually reaches this code before that has happened - MonacoEditor.WhenLoaded(...) runs the callback once it has, immediately if it already is. Calling Colorize before then returns nothing useful rather than waiting."),
                        TextBlock("What starts the loader, though, is a component mounting. A page whose only Monaco content is colorized markup has no component to do that, so it has to call MonacoEditor.LoadAsync() itself - otherwise WhenLoaded queues a callback that nothing ever runs. This page is exactly that case.").MT(8),
                        TextBlock("The markup carries Monaco's token classes, so it re-colours with the theme like everything else, but it inherits nothing about the font - set the family and size yourself, or the snippet renders in the page's body font.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Two lines of C#, tokenized and coloured. There is no editor on this page: no caret, no selection, nothing to focus."),
                        Raw(host).MT(8),
                        SampleHint("Switch to dark mode with the button at the bottom of the sidebar - the snippet follows the theme.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CodeViewerSample), typeof(CustomLanguageSample), typeof(LanguagesAndThemesSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
