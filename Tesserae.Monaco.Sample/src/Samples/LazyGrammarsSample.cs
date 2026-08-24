using System.Threading.Tasks;
using Transpose;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// The two halves of "a grammar that is not in the initial payload": a language of your own whose
    /// tokenizer arrives on demand, and replacing the grammar of a language Monaco already ships.
    /// </summary>
    [SampleDetails(Group = "Language services", Order = 13, Icon = UIcons.Clock)]
    public class LazyGrammarsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        // Proof that the factories are not called at start-up: each flips its line when Monaco first
        // asks, which only happens once a document in that language exists. Static because the 'bat'
        // override is registered globally and is not re-registered on a second visit to this page.
        private static TextBlock _dialectState;
        private static TextBlock _batState;

        public LazyGrammarsSample()
        {
            _dialectState = TextBlock("Tokenizer factory: not requested yet").Small().Secondary();
            _batState     = TextBlock("Tokenizer factory: not requested yet").Small().Secondary();

            // A language of our own, with nothing but its identity described up front. Monaco knows it
            // exists - it is in getLanguages(), the extension resolves, an editor can select it - and
            // calls the factory the first time a document actually uses it.
            var dialect = new LanguageDefinition
            {
                Id         = "lazydialect",
                Aliases    = new[] { "Lazy Dialect" },
                Extensions = new[] { ".lazy" },

                TokenizerFactory     = LoadDialectTokenizerAsync,
                ConfigurationFactory = LoadDialectConfigurationAsync,

                TokenColors = new[]
                {
                    new TokenColor("keyword", "c586c0", "bold"),
                    new TokenColor("string",  "ce9178"),
                    new TokenColor("comment", "6a9955", "italic")
                }
            };

            var dialectEditor = MonacoEditor.Editor()
               .SetLanguage(dialect)
               .SetText("# arrives only when this page is opened\nwhen ready say \"hello\"\n");

            // The other direction: 'bat' is one of Monaco's own languages, and this replaces its
            // grammar. Monaco treats tokenizers as exclusive per language, so the last one registered
            // wins - including over the one Monaco registered for itself.
            MonacoEditor.SetTokenizer("bat", LoadBatTokenizerAsync);

            var batEditor = MonacoEditor.Editor()
               .SetLanguage("bat")
               .SetText("@echo off\nREM only ECHO and SET are keywords in our grammar\nSET greeting=hello\nECHO %greeting%\n");

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(LazyGrammarsSample), UIcons.Clock, "Grammars that are fetched when a language is first used")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A Monarch grammar is a sizeable object, and an app usually shows one or two languages out of the dozens it supports. LanguageDefinition.TokenizerFactory is the deferred form of Tokenizer: a Func<Task<object>> Monaco calls the first time a document uses the language, and never if no document does."),
                        TextBlock("ConfigurationFactory is its companion for the comment markers and bracket pairs, applied on the same first encounter. Both map onto how Monaco loads its own ~90 grammars - registerTokensProviderFactory plus onLanguageEncountered - so a grammar of yours is deferred on exactly the terms Monaco's are.").MT(8),
                        TextBlock("MonacoEditor.SetTokenizer(languageId, ...) is the other half: it replaces the grammar of a language that already exists, whether one of Monaco's own or one registered earlier. It takes the same two shapes, eager or deferred.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Put the fetch inside the delegate, not around it: Transpose.Require.RequireAsync in there means the grammar's script is requested when the language is first shown, while awaiting it before building the definition puts it right back in the initial payload."),
                        TextBlock("TokenColors still belong on the definition rather than in the deferred grammar - they are folded into the themes when those are defined, which happens as Monaco loads, well before any factory runs.").MT(8),
                        TextBlock("Replacing a built-in grammar is global, like everything on monaco.languages: every editor showing that language gets the new one. Register it once, from a place that runs whether or not the editor is on screen.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("A language of your own"),
                        TextBlock("Its tokenizer is a Func<Task<object>> that resolves after a short delay, standing in for a fetch."),
                        _dialectState.MT(4),
                        dialectEditor.WS().H(120.px()).MT(8),

                        SampleSubTitle("Replacing a built-in grammar"),
                        TextBlock("Monaco ships a bat grammar; this one colours only ECHO and SET, so the difference is visible."),
                        _batState.MT(4),
                        batEditor.WS().H(140.px()).MT(8),

                        SampleHint("Open the network panel and reload: the language chunks Monaco fetches are the ones these pages use, not all 90 of them.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CustomLanguageSample), typeof(LanguagesAndThemesSample), typeof(BundledServicesSample));
        }

        // Stands in for what a real host does here - Transpose.Require.RequireAsync of the script
        // holding the grammar, then reading it off the global it defined.
        private static async Task<object> LoadDialectTokenizerAsync()
        {
            _dialectState.Text = "Tokenizer factory: requested";

            await Task.Delay(250);

            _dialectState.Text = "Tokenizer factory: delivered";

            return new
            {
                tokenizer = new
                {
                    root = new object[]
                    {
                        new object[] { "#.*$",                     "comment" },
                        new object[] { "\\b(when|ready|say)\\b",   "keyword" },
                        new object[] { "\"[^\"]*\"",               "string"  }
                    }
                }
            };
        }

        private static async Task<object> LoadDialectConfigurationAsync()
        {
            await Task.Delay(250);

            return new { comments = new { lineComment = "#" } };
        }

        private static async Task<object> LoadBatTokenizerAsync()
        {
            _batState.Text = "Tokenizer factory: requested";

            await Task.Delay(250);

            _batState.Text = "Tokenizer factory: delivered";

            return new
            {
                ignoreCase = true,
                tokenizer  = new
                {
                    root = new object[]
                    {
                        // [@] rather than @: Monarch reads a bare @word in a rule as one of its own
                        // attribute references, and rejects the whole grammar when there is no such
                        // attribute - "language definition does not contain attribute 'echo'".
                        new object[] { "^\\s*(REM|[@]echo)\\b.*$", "comment" },
                        new object[] { "\\b(ECHO|SET)\\b",         "keyword" },
                        new object[] { "%[^%]+%",                  "string"  }
                    }
                }
            };
        }

        public HTMLElement Render() => _content.Render();
    }
}
