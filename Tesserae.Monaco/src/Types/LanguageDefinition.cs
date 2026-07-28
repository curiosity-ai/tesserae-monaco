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

        /// <summary>Hex colour <b>without</b> the leading <c>#</c>, as Monaco requires (e.g. <c>"569cd6"</c>).</summary>
        public string Foreground { get; set; }

        /// <summary>Any of <c>"italic"</c>, <c>"bold"</c>, <c>"underline"</c>, or a space-separated combination.</summary>
        public string FontStyle { get; set; }

        public TokenColor() { }

        public TokenColor(string token, string foreground, string fontStyle = null)
        {
            Token      = token;
            Foreground = foreground;
            FontStyle  = fontStyle;
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
        /// Monaco's <c>LanguageConfiguration</c> (comment markers, brackets, auto-closing pairs).
        /// Optional; when null Monaco applies no bracket matching or comment toggling.
        /// </summary>
        public object Configuration { get; set; }

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
