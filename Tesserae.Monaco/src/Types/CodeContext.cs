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

        internal CodeContext(ITextModel model, Position position)
        {
            Text = model.getValue();

            TextUntilPosition = model.getValueInRange(new TextRange
            {
                startLineNumber = 1,
                startColumn     = 1,
                endLineNumber   = position.lineNumber,
                endColumn       = position.column
            });

            Position = new Position
            {
                lineNumber = position.lineNumber,
                column     = position.column
            };

            var word = model.getWordAtPosition(position);

            if (word != null)
            {
                Word = word.word;

                WordRange = new TextRange
                {
                    startLineNumber = Position.lineNumber,
                    endLineNumber   = Position.lineNumber,
                    startColumn     = word.startColumn,
                    endColumn       = word.endColumn
                };
            }
        }
    }
}
