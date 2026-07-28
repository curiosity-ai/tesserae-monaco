using System;
using System.Threading.Tasks;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Static entry point for the Monaco-backed components, mirroring Tesserae's <c>UI</c> class.
    ///
    /// Also owns everything that is global to Monaco rather than per-editor: loading the AMD
    /// bundle, the light/dark themes derived from the active Tesserae theme, custom language
    /// registration, and the shared host element that lets suggest/hover popups escape a clipping
    /// ancestor.
    /// </summary>
    public static partial class MonacoEditor
    {
        /// <summary>A full-featured, editable code editor.</summary>
        /// <param name="autoHeight">
        /// Grow the editor's height to fit its content instead of scrolling vertically. The parent
        /// must be able to grow too, otherwise the editor is simply clipped.
        /// </param>
        public static CodeEditor Editor(bool autoHeight = false) => new CodeEditor(autoHeight);

        /// <summary>A lighter, read-only-by-default viewer for displaying code.</summary>
        /// <param name="autoHeight">Grow to fit content instead of scrolling vertically.</param>
        public static CodeViewer Viewer(bool autoHeight = false) => new CodeViewer(autoHeight);

        /// <summary>A side-by-side (or inline) diff of two documents.</summary>
        public static DiffViewer Diff() => new DiffViewer();
    }
}
