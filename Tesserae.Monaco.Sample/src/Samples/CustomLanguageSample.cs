using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 11, Icon = UIcons.Language)]
    public class CustomLanguageSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public CustomLanguageSample()
        {
            var greetLanguage = new LanguageDefinition
            {
                Id         = "greet",
                Aliases    = new[] { "Greet" },
                Extensions = new[] { ".greet" },

                // Monaco's Monarch tokenizer, written as an anonymous object mirroring the JS shape.
                Tokenizer = new
                {
                    tokenizer = new
                    {
                        root = new object[]
                        {
                            new object[] { "#.*$",                  "comment" },
                            new object[] { "\\b(greet|from|to)\\b", "keyword" },
                            new object[] { "\"[^\"]*\"",            "string"  },
                            new object[] { "\\b\\d+\\b",            "number"  }
                        }
                    }
                },

                Configuration = new
                {
                    comments = new { lineComment = "#" },
                    brackets = new object[] { new[] { "(", ")" } }
                },

                TokenColors = new[]
                {
                    new TokenColor("keyword", "c586c0", "bold"),
                    new TokenColor("string",  "ce9178"),
                    new TokenColor("comment", "6a9955", "italic"),
                    new TokenColor("number",  "b5cea8")
                },

                // Without these, Monaco never triggers completion on ':' - it is not a word character.
                CompletionTriggerCharacters = new[] { ":" }
            };

            var editor = MonacoEditor.Editor()
               .SetLanguage(greetLanguage)
               .SetText("# a tiny made-up language\ngreet \"world\" from 1 to 10\n");

            editor.OnCompletion(context => Task.FromResult(new[]
            {
                new CompletionItem { label = "greet", kind = CompletionItemKind.Keyword },
                new CompletionItem { label = "from",  kind = CompletionItemKind.Keyword },
                new CompletionItem { label = "to",    kind = CompletionItemKind.Keyword }
            }));

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(CustomLanguageSample), UIcons.Language, "A language of your own, described from C#")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A LanguageDefinition carries an id, its aliases and file extensions, a Monarch tokenizer, a language configuration, and the colours its token types take. Pass one to .SetLanguage(...) - or register it up front with MonacoEditor.RegisterLanguage(...) - and every editor asking for that id gets it."),
                        TextBlock("This is how a query language, a template dialect or a configuration format gets first-class highlighting without a Monaco plugin. The tokenizer is Monaco's own Monarch, written here as an anonymous object mirroring the JavaScript shape it expects.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Registration is safe to call before Monaco has loaded: definitions are queued and applied once it is ready, which is what lets a page describe its languages while it is still being built."),
                        TextBlock("TokenColors are folded into the light and dark themes the package derives from Tesserae's, so a custom language re-colours with everything else when the theme changes. Set CompletionTriggerCharacters for any punctuation that should open the suggest list - Monaco only triggers on word characters by default.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("The greet, from and to keywords, the string and the numbers are all coloured by the Monarch rules above. Press Ctrl+Space for the language's three keywords."),
                        editor.WS().H(160.px()).MT(8),
                        SampleHint("The same definition also registers the .greet extension, so .SetLanguageByExtension(\".greet\") finds it.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CompletionAndHoverSample), typeof(LanguagesAndThemesSample), typeof(CodeEditorSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
