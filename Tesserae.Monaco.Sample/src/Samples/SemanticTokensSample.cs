using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 10, Icon = UIcons.PaintRoller)]
    public class SemanticTokensSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public SemanticTokensSample()
        {
            var legend = new SemanticTokensLegend
            {
                tokenTypes     = new[] { "macro", "variable" },
                tokenModifiers = new string[0]
            };

            // The legend names token types; the colour for one comes from a theme rule, exactly as for a
            // Monarch token. Without this the provider still runs and nothing looks different.
            MonacoEditor.AddTokenColors(new TokenColor("macro", "4fc1ff", "bold"));

            // The themes are derived when Monaco loads, so a colour added after that - which is the case
            // whenever this page is not the first one opened - is only picked up by redefining them.
            // RegisterLanguage does this for a language's own TokenColors; AddTokenColors leaves it to
            // the host, which here is this page.
            MonacoEditor.WhenLoaded(() =>
            {
                MonacoEditor.DefineThemes();
                MonacoEditor.ApplyTheme();
            });

            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// ALLCAPS words are coloured by the semantic provider, not the tokenizer\nvar limit = MAX_ITEMS;\nvar name  = DEFAULT_NAME;\n");

            editor.OnSemanticTokens(legend, text =>
            {
                var builder = new SemanticTokenBuilder();
                var lines   = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var line  = lines[i];
                    var start = -1;

                    for (var c = 0; c <= line.Length; c++)
                    {
                        var isUpper = c < line.Length && (char.IsUpper(line[c]) || line[c] == '_');

                        if (isUpper && start < 0) start = c;

                        if (!isUpper && start >= 0)
                        {
                            // Token type 0 is "macro" - the first entry in the legend above.
                            if (c - start >= 3) builder.Add(i + 1, start + 1, c - start, 0);

                            start = -1;
                        }
                    }
                }

                return Task.FromResult(builder.Build());
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(SemanticTokensSample), UIcons.PaintRoller, "Highlighting a tokenizer cannot express")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A Monarch tokenizer sees one line at a time and matches on regular expressions, so anything that depends on meaning - which identifiers are types, which are constants, which are unused - is beyond it. .OnSemanticTokens(legend, text => ...) is the second pass: a provider that colours by what a symbol is rather than what it looks like."),
                        TextBlock("The legend names the token types you will emit; SemanticTokenBuilder takes a line, a column, a length and an index into that legend, and encodes the deltas Monaco's wire format wants.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A token type only has a colour if something defines one - MonacoEditor.AddTokenColors(...) folds a rule into both derived themes, exactly as a custom language's TokenColors are. Without it the provider runs, the tokens arrive, and nothing looks different, which is a confusing way to spend an afternoon."),
                        TextBlock("Timing matters as much as the rule: the themes are built when Monaco loads, so a colour added afterwards needs MonacoEditor.DefineThemes() and ApplyTheme() to become visible. This page calls both from WhenLoaded, which is why the colour is there whether or not it is the first page opened.").MT(8),
                        TextBlock("Semantic tokens layer over the tokenizer's, they do not replace them - so keep the Monarch rules for the syntax and use this for the meaning. Emit tokens in document order: the format is delta-encoded and out-of-order tokens are silently dropped.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Every run of three or more capitals or underscores is reported as a \"macro\" and comes out bold blue - MAX_ITEMS and DEFAULT_NAME below. Type another ALLCAPS word and it colours too."),
                        editor.WS().H(160.px()).MT(8),
                        SampleHint("Nothing in the csharp tokenizer knows about ALLCAPS: turn the provider off and these are plain identifiers.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CustomLanguageSample), typeof(LanguagesAndThemesSample), typeof(DecorationsSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
