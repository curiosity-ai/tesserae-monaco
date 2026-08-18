using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A selection with a direction, matching Monaco's <c>ISelection</c>: the anchor is
    /// <c>selectionStart</c> and the moving end is <c>position</c>, so a selection dragged upwards has
    /// its anchor after its caret.
    ///
    /// Use <see cref="Selections.ToRange"/> for the normalised range when direction doesn't matter.
    /// </summary>
    [ObjectLiteral]
    public class TextSelection
    {
        public int selectionStartLineNumber;
        public int selectionStartColumn;
        public int positionLineNumber;
        public int positionColumn;
    }

    /// <summary>Ordering a <see cref="TextSelection"/> into a plain range.</summary>
    public static class Selections
    {
        /// <summary>
        /// The selection as a start-before-end range, whichever way round the user dragged it.
        /// </summary>
        public static TextRange ToRange(TextSelection selection)
        {
            if (selection is null) return null;

            var anchorFirst =
                selection.selectionStartLineNumber < selection.positionLineNumber ||
                (selection.selectionStartLineNumber == selection.positionLineNumber &&
                 selection.selectionStartColumn <= selection.positionColumn);

            if (anchorFirst)
            {
                return Ranges.Of(
                    selection.selectionStartLineNumber,
                    selection.selectionStartColumn,
                    selection.positionLineNumber,
                    selection.positionColumn
                );
            }

            return Ranges.Of(
                selection.positionLineNumber,
                selection.positionColumn,
                selection.selectionStartLineNumber,
                selection.selectionStartColumn
            );
        }

        /// <summary>A selection covering <paramref name="range"/>, anchored at its start.</summary>
        public static TextSelection FromRange(TextRange range)
        {
            if (range is null) return null;

            return new TextSelection
            {
                selectionStartLineNumber = range.startLineNumber,
                selectionStartColumn     = range.startColumn,
                positionLineNumber       = range.endLineNumber,
                positionColumn           = range.endColumn
            };
        }

        /// <summary>True when the selection covers no characters.</summary>
        public static bool IsEmpty(TextSelection selection)
        {
            return selection is null ||
                   (selection.selectionStartLineNumber == selection.positionLineNumber &&
                    selection.selectionStartColumn == selection.positionColumn);
        }
    }

    /// <summary>
    /// Why the caret moved, matching Monaco's <c>CursorChangeReason</c>. Compare
    /// <see cref="ICursorPositionChangedEvent.reason"/> against these rather than against raw numbers.
    /// </summary>
    [Enum(Emit.Value)]
    public enum CursorChangeReason
    {
        NotSet      = 0,
        ContentFlush = 1,
        RecoverFromMarkers = 2,
        Explicit    = 3,
        Paste       = 4,
        Undo        = 5,
        Redo        = 6
    }

    /// <summary>
    /// What part of the editor the pointer is over, matching Monaco's <c>MouseTargetType</c>. Compare
    /// <see cref="IMouseTarget.type"/> against these.
    /// </summary>
    [Enum(Emit.Value)]
    public enum MouseTargetType
    {
        Unknown             = 0,
        Textarea            = 1,
        GutterGlyphMargin   = 2,
        GutterLineNumbers   = 3,
        GutterLineDecorations = 4,
        GutterViewZone      = 5,
        Content             = 6,
        ContentEmpty        = 7,
        ContentViewZone     = 8,
        ContentWidget       = 9,
        OverviewRuler       = 10,
        Scrollbar           = 11,
        OverlayWidget       = 12,
        OutsideEditor       = 13
    }

    /// <summary>
    /// One changed block in a diff, matching Monaco's <c>ILineChange</c>. Lines are one-based and
    /// inclusive.
    ///
    /// An end line of 0 on a side means the block does not exist there at all: it is an insertion when
    /// <see cref="originalEndLineNumber"/> is 0, and a deletion when
    /// <see cref="modifiedEndLineNumber"/> is 0.
    /// </summary>
    [ObjectLiteral]
    public class LineChange
    {
        public int originalStartLineNumber;
        public int originalEndLineNumber;
        public int modifiedStartLineNumber;
        public int modifiedEndLineNumber;
    }
}
