using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 9, Icon = UIcons.Swatchbook)]
    public class LinksAndColorsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public LinksAndColorsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("ini")
               .SetText(SampleCode.Annotated)
               .Links();

            editor.OnDocumentLinks(text =>
            {
                var links = new List<DocumentLink>();
                var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var at = lines[i].IndexOf("https://", StringComparison.Ordinal);

                    if (at < 0) continue;

                    var end = lines[i].IndexOf(' ', at);

                    if (end < 0) end = lines[i].Length;

                    links.Add(new DocumentLink
                    {
                        range   = Ranges.Of(i + 1, at + 1, i + 1, end + 1),
                        url     = lines[i].Substring(at, end - at),
                        tooltip = "Open the Monaco docs"
                    });
                }

                return Task.FromResult(links.ToArray());
            });

            editor.OnColors(text =>
            {
                var colors = new List<ColorInformation>();
                var lines  = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var at = lines[i].IndexOf('#');

                    // Only a `#rrggbb` literal, and not the comment marker at the start of a line.
                    if (at < 0 || at + 7 > lines[i].Length || i == 0) continue;

                    var hex = lines[i].Substring(at + 1, 6);

                    colors.Add(new ColorInformation
                    {
                        range = Ranges.Of(i + 1, at + 1, i + 1, at + 8),
                        color = new ColorValue
                        {
                            red   = HexPair(hex, 0),
                            green = HexPair(hex, 2),
                            blue  = HexPair(hex, 4),
                            alpha = 1
                        }
                    });
                }

                return Task.FromResult(colors.ToArray());
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(LinksAndColorsSample), UIcons.Swatchbook, "Clickable URLs and a colour picker in the text")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock(".OnDocumentLinks(text => ...) turns ranges into links: Monaco underlines them on Ctrl-hover and opens the url on click, with the tooltip you supply. Any syntax can have links this way - an issue id, a file path, a package name - because the provider decides what a link is, not the tokenizer."),
                        TextBlock(".OnColors(text => ...) reports where the colour literals are and what colour each one is. Monaco draws a swatch in front of each and opens its own colour picker on click - editing through it replaces the literal in the text.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Links need .Links() on the editor, which is Monaco's default. A url that is not absolute is left alone rather than guessed at, so resolve relative paths in the provider - it is the only place that knows what they are relative to."),
                        TextBlock("A ColorValue's channels are 0..1 doubles, not bytes - dividing by 255 is the conversion, and getting it wrong shows up as a swatch that is always white. Monaco decides the picker's format from the range you reported, so the range must cover the literal exactly.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("The URL in the comment on line 1 is a link - Ctrl-hover it. The two #rrggbb values each carry a swatch; click one to open Monaco's colour picker."),
                        editor.WS().H(240.px()).MT(8),
                        SampleHint("Picking a new colour writes it back into the document as an edit, so Ctrl+Z reverts it.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(InlayHintsAndLensesSample), typeof(FoldingSample), typeof(DecorationsSample));
        }

        private static double HexPair(string hex, int offset)
        {
            var value = Convert.ToInt32(hex.Substring(offset, 2), 16);

            return value / 255.0;
        }

        public HTMLElement Render() => _content.Render();
    }
}
