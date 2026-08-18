using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Decorations and widgets", Order = 0, Icon = UIcons.Highlighter)]
    public class DecorationsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public DecorationsSample()
        {
            // The classes the decorations below name. The package ships no CSS, so this is the host's job.
            SampleStyles.Ensure();

            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SampleCode.Order)
               .GlyphMargin();

            var status  = TextBlock("no decorations").Small().Secondary();
            var applied = false;
            var toggle  = Button("Decorate");

            toggle.OnClick(() =>
            {
                applied = !applied;

                if (applied)
                {
                    editor.Decorate(new[]
                    {
                        Decoration.Line(3, SampleStyles.Line),
                        Decoration.Glyph(6, SampleStyles.Glyph, "This method is covered by tests"),
                        Decoration.Range(Ranges.Of(8, 16, 8, 21), SampleStyles.Inline),
                        Decoration.RulerMark(Ranges.Line(3), "#e2c08d"),
                        Decoration.InlineNote(new Position { lineNumber = 4, column = 39 }, "  // money", SampleStyles.Note)
                    });

                    status.Text = editor.GetDecorationRanges().Length + " decorations - now type above line 3 and they follow the text";
                }
                else
                {
                    editor.ClearDecorations();
                    status.Text = "no decorations";
                }

                toggle.SetText(applied ? "Clear" : "Decorate");
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(DecorationsSample), UIcons.Highlighter, "Marking up the text without editing it")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A decoration paints something over a range of text without changing the document: a highlighted line, a wavy underline, an icon in the glyph margin, a mark on the overview ruler, or a note injected after the text. Build them with the Decoration factories and hand a set to .Decorate(...)."),
                        TextBlock("Monaco tracks a decoration's range across edits, so a highlight on line 3 stays on that line's text when a line is inserted above it. That is why decorations are the right tool for anything derived from the document - coverage, blame, search hits - and why re-applying them after every keystroke is the wrong one.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A decoration names a CSS class and the host styles it: the package ships no stylesheet, the same way it ships no language intelligence. This page injects its own, which is what a host app does too."),
                        TextBlock("Prefer updating one collection over replacing it - .CreateDecorations(...) hands back a DecorationCollection whose .Set(...) only re-renders the difference. Glyph-margin decorations need .GlyphMargin() on the editor, or there is no margin to draw in.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Five decorations at once: the line, the glyph, the underlined range, the ruler mark and the injected note. Then type a line above line 3 and watch them move with the text."),
                        editor.WS().H(220.px()).MT(8),
                        HStack().WS().AlignItemsCenter().PT(8).Children(toggle, status.PL(8.px())),
                        SampleHint("Hover the dot in the glyph margin: a glyph decoration carries its own hover message.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(WidgetsSample), typeof(DiagnosticsSample), typeof(CodeEditorSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
