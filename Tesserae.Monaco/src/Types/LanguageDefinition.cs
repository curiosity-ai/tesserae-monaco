using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A syntax-colour rule for one Monarch token type, matching an entry in Monaco's
    /// <c>defineTheme</c> <c>rules</c> array.
    /// </summary>
    public sealed class TokenColor
    {
        /// <summary>The Monarch token name produced by the tokenizer (e.g. <c>"keyword"</c>).</summary>
        public string Token { get; set; }

        /// <summary>
        /// Hex colour, with or without the leading <c>#</c> - it is stripped for you, since Monaco requires
        /// it absent (e.g. <c>"569cd6"</c>).
        /// </summary>
        public string Foreground { get; set; }

        /// <summary>
        /// Background hex colour for the token, with or without the leading <c>#</c>. Rarely wanted for
        /// syntax, but it is how a token type gets highlighted rather than just recoloured.
        /// </summary>
        public string Background { get; set; }

        /// <summary>Any of <c>"italic"</c>, <c>"bold"</c>, <c>"underline"</c>, or a space-separated combination.</summary>
        public string FontStyle { get; set; }

        public TokenColor() { }

        public TokenColor(string token, string foreground, string fontStyle = null, string background = null)
        {
            Token      = token;
            Foreground = foreground;
            FontStyle  = fontStyle;
            Background = background;
        }
    }

    /// <summary>
    /// A custom language to register with Monaco, via
    /// <see cref="MonacoEditor.RegisterLanguage(LanguageDefinition)"/>.
    ///
    /// Monaco's language registry is global (per language id), so registering the same id twice is
    /// ignored - <see cref="MonacoEditor.RegisterLanguage(LanguageDefinition)"/> is safe to call
    /// from every component that uses the language.
    /// </summary>
    public sealed class LanguageDefinition
    {
        /// <summary>The language id used by <c>SetLanguage(...)</c> - e.g. <c>"mylang"</c>.</summary>
        public string Id { get; set; }

        /// <summary>Human-readable names shown in Monaco's language picker.</summary>
        public string[] Aliases { get; set; }

        /// <summary>File extensions, each including the dot (e.g. <c>".mylang"</c>).</summary>
        public string[] Extensions { get; set; }

        /// <summary>
        /// A Monarch tokenizer - Monaco's <c>IMonarchLanguage</c>. Build it as an anonymous object
        /// whose shape mirrors the JS one, e.g.
        /// <code>
        /// Tokenizer = new
        /// {
        ///     keywords  = new[] { "if", "else" },
        ///     tokenizer = new
        ///     {
        ///         root = new object[]
        ///         {
        ///             new object[] { "[a-z_$][\\w$]*", new { cases = new Dictionary&lt;string, string&gt; { ... } } },
        ///             new object[] { "\".*?\"", "string" }
        ///         }
        ///     }
        /// };
        /// </code>
        /// </summary>
        public object Tokenizer { get; set; }

        /// <summary>
        /// The same thing, fetched only once a document actually uses the language - for a grammar
        /// that lives in its own script file rather than in C#. Monaco registers all ~90 of its own
        /// grammars this way, and this is the hook it does it through
        /// (<c>registerTokensProviderFactory</c>).
        ///
        /// Set this instead of <see cref="Tokenizer"/>; when both are given the eager one wins, since
        /// there is nothing to wait for. The task is awaited once and its result cached by Monaco.
        /// </summary>
        public Func<Task<object>> TokenizerFactory { get; set; }

        /// <summary>
        /// Monaco's <c>LanguageConfiguration</c> as a raw object (comment markers, brackets, auto-closing
        /// pairs). Optional; when null Monaco applies no bracket matching or comment toggling.
        ///
        /// Prefer <see cref="Config"/>, which is the same thing typed. This stays as the escape hatch for
        /// the corners of the shape it doesn't cover - indentation rules, folding markers by regex - and
        /// wins over <see cref="Config"/> when both are set.
        /// </summary>
        public object Configuration { get; set; }

        /// <summary>
        /// Comment markers, brackets and auto-closing pairs, typed. Set this instead of
        /// <see cref="Configuration"/> unless you need something it doesn't express.
        /// </summary>
        public LanguageConfiguration Config { get; set; }

        /// <summary>
        /// The language configuration fetched on demand, applied the first time a document uses the
        /// language. The companion to <see cref="TokenizerFactory"/> for a grammar file that carries
        /// its configuration alongside its tokenizer; use it when both come from the same script, so
        /// neither is fetched until the language is actually shown.
        /// </summary>
        public Func<Task<object>> ConfigurationFactory { get; set; }

        /// <summary>
        /// Syntax colours for the tokens the <see cref="Tokenizer"/> emits. Applied on top of the
        /// package's light and dark base themes, so a single set of rules covers both.
        /// </summary>
        public TokenColor[] TokenColors { get; set; }

        /// <summary>
        /// Characters that should pop the suggest widget in addition to normal word characters -
        /// e.g. <c>":"</c> or <c>"|"</c>. Monaco only auto-triggers completion on word characters,
        /// so a language whose syntax hinges on punctuation needs these declared or the editor's
        /// own completion handler will never be asked.
        /// </summary>
        public string[] CompletionTriggerCharacters { get; set; }
    }
}

namespace Tesserae.Monaco
{
    /// <summary>
    /// A pair of characters Monaco should treat as brackets, matching an entry in Monaco's
    /// <c>brackets</c> array. Drives bracket matching, the colouring, and the indentation that follows an
    /// opening bracket.
    /// </summary>
    public sealed class BracketPair
    {
        public string Open  { get; set; }
        public string Close { get; set; }

        public BracketPair() { }

        public BracketPair(string open, string close)
        {
            Open  = open;
            Close = close;
        }
    }

    /// <summary>
    /// A pair Monaco closes for the user, matching Monaco's <c>IAutoClosingPairConditional</c>.
    ///
    /// <see cref="NotIn"/> is what stops an apostrophe inside a comment from opening a string: name the
    /// scopes - <c>"string"</c>, <c>"comment"</c> - where the pair should be left alone.
    /// </summary>
    public sealed class AutoClosingPair
    {
        public string   Open  { get; set; }
        public string   Close { get; set; }
        public string[] NotIn { get; set; }

        public AutoClosingPair() { }

        public AutoClosingPair(string open, string close, string[] notIn = null)
        {
            Open  = open;
            Close = close;
            NotIn = notIn;
        }
    }

    /// <summary>Monaco's <c>CommentRule</c>.</summary>
    [ObjectLiteral]
    public class CommentRule
    {
        public string   lineComment;

        /// <summary>The open and close markers, in that order.</summary>
        public string[] blockComment;
    }

    /// <summary>An entry in Monaco's <c>autoClosingPairs</c>.</summary>
    [ObjectLiteral]
    public class AutoClosingPairRule
    {
        public string   open;
        public string   close;
        public string[] notIn;
    }

    /// <summary>An entry in Monaco's <c>brackets</c> or <c>surroundingPairs</c>.</summary>
    [ObjectLiteral]
    public class BracketPairRule
    {
        public string open;
        public string close;
    }

    /// <summary>Monaco's <c>LanguageConfiguration</c>, as passed to <c>setLanguageConfiguration</c>.</summary>
    [ObjectLiteral]
    public class LanguageConfigurationData
    {
        public CommentRule           comments;
        public BracketPairRule[]     brackets;
        public AutoClosingPairRule[] autoClosingPairs;
        public BracketPairRule[]     surroundingPairs;
    }

    /// <summary>
    /// A typed <c>LanguageConfiguration</c>: what makes bracket matching, comment toggling (Ctrl+/),
    /// auto-closing and surround-with work for a custom language.
    ///
    /// Registering a Monarch tokenizer only colours the text - none of that behaviour comes with it, which
    /// is why a custom language often feels half-finished until this is supplied.
    /// </summary>
    public sealed class LanguageConfiguration
    {
        /// <summary>The line-comment marker, e.g. <c>"//"</c>. Enables Ctrl+/.</summary>
        public string LineComment { get; set; }

        /// <summary>The block-comment opener, e.g. <c>"/*"</c>. Enables Shift+Alt+A.</summary>
        public string BlockCommentStart { get; set; }

        /// <summary>The block-comment terminator.</summary>
        public string BlockCommentEnd { get; set; }

        /// <summary>Bracket pairs, for matching, colouring and indentation.</summary>
        public BracketPair[] Brackets { get; set; }

        /// <summary>Pairs Monaco closes as the user types the opening character.</summary>
        public AutoClosingPair[] AutoClosingPairs { get; set; }

        /// <summary>
        /// Pairs that wrap a selection when the user types the opening character. Defaults to
        /// <see cref="AutoClosingPairs"/> when left unset, which is nearly always what is wanted.
        /// </summary>
        public BracketPair[] SurroundingPairs { get; set; }

        internal LanguageConfigurationData ToMonaco()
        {
            var data = new LanguageConfigurationData();

            if (!string.IsNullOrWhiteSpace(LineComment) || !string.IsNullOrWhiteSpace(BlockCommentStart))
            {
                data.comments = new CommentRule();

                if (!string.IsNullOrWhiteSpace(LineComment)) data.comments.lineComment = LineComment;

                if (!string.IsNullOrWhiteSpace(BlockCommentStart) && !string.IsNullOrWhiteSpace(BlockCommentEnd))
                {
                    data.comments.blockComment = new[] { BlockCommentStart, BlockCommentEnd };
                }
            }

            var brackets    = ToRules(Brackets);
            var surrounding = ToRules(SurroundingPairs ?? BracketsFrom(AutoClosingPairs));

            if (brackets    != null) data.brackets         = brackets;
            if (surrounding != null) data.surroundingPairs = surrounding;

            if (AutoClosingPairs is object && AutoClosingPairs.Length > 0)
            {
                var pairs = new List<AutoClosingPairRule>();

                foreach (var pair in AutoClosingPairs)
                {
                    if (pair is null || pair.Open is null || pair.Close is null) continue;

                    var rule = new AutoClosingPairRule { open = pair.Open, close = pair.Close };

                    if (pair.NotIn is object && pair.NotIn.Length > 0) rule.notIn = pair.NotIn;

                    pairs.Add(rule);
                }

                if (pairs.Count > 0) data.autoClosingPairs = pairs.ToArray();
            }

            return data;
        }

        private static BracketPairRule[] ToRules(BracketPair[] pairs)
        {
            if (pairs is null || pairs.Length == 0) return null;

            var rules = new List<BracketPairRule>();

            foreach (var pair in pairs)
            {
                if (pair is null || pair.Open is null || pair.Close is null) continue;

                rules.Add(new BracketPairRule { open = pair.Open, close = pair.Close });
            }

            return rules.Count == 0 ? null : rules.ToArray();
        }

        private static BracketPair[] BracketsFrom(AutoClosingPair[] pairs)
        {
            if (pairs is null || pairs.Length == 0) return null;

            var result = new List<BracketPair>();

            foreach (var pair in pairs)
            {
                if (pair is object) result.Add(new BracketPair(pair.Open, pair.Close));
            }

            return result.ToArray();
        }
    }
}
