using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The state a completion or hover request is made against, read off Monaco's model so callers
    /// don't have to touch <c>dynamic</c>.
    ///
    /// <see cref="TextUntilPosition"/> is what a language backend usually wants alongside
    /// <see cref="Text"/>: its length is the caret's offset into the document, which is how most
    /// compiler APIs address a position.
    /// </summary>
    public sealed class CodeContext
    {
        /// <summary>The whole document.</summary>
        public string Text { get; }

        /// <summary>The document from the start up to the caret.</summary>
        public string TextUntilPosition { get; }

        /// <summary>The caret's offset into <see cref="Text"/>.</summary>
        public int Offset => TextUntilPosition.Length;

        /// <summary>The one-based caret position.</summary>
        public Position Position { get; }

        /// <summary>The word under the caret, or null when the caret isn't on a word.</summary>
        public string Word { get; }

        /// <summary>The range of <see cref="Word"/>, or null when the caret isn't on a word.</summary>
        public TextRange WordRange { get; }

        internal CodeContext(dynamic model, dynamic position)
        {
            Text = Script.Write<string>("{0}.getValue()", model);

            TextUntilPosition = Script.Write<string>(
                "{0}.getValueInRange({ startLineNumber: 1, startColumn: 1, endLineNumber: {1}.lineNumber, endColumn: {1}.column })",
                model,
                position
            );

            Position = new Position
            {
                lineNumber = Script.Write<int>("{0}.lineNumber", position),
                column     = Script.Write<int>("{0}.column",     position)
            };

            dynamic word = Script.Write<dynamic>("{0}.getWordAtPosition({1})", model, position);

            if (word is object)
            {
                Word = Script.Write<string>("{0}.word", word);

                WordRange = new TextRange
                {
                    startLineNumber = Position.lineNumber,
                    endLineNumber   = Position.lineNumber,
                    startColumn     = Script.Write<int>("{0}.startColumn", word),
                    endColumn       = Script.Write<int>("{0}.endColumn",   word)
                };
            }
        }
    }
}
