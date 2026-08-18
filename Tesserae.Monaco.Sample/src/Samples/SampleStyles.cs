using static Transpose.Core.dom;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// The CSS the decoration and widget pages name. The package ships no stylesheet of its own - the
    /// same way it ships no language intelligence - so a decoration carries a class name and the host
    /// decides what it looks like. These are the sample's own, injected once.
    /// </summary>
    internal static class SampleStyles
    {
        private const string STYLE_ID = "tssm-sample-styles";

        internal const string Line    = "tssm-sample-line";
        internal const string Glyph   = "tssm-sample-glyph";
        internal const string Inline  = "tssm-sample-inline";
        internal const string Note    = "tssm-sample-note";
        internal const string Widget  = "tssm-sample-widget";
        internal const string Overlay = "tssm-sample-overlay";
        internal const string Zone    = "tssm-sample-zone";

        internal static void Ensure()
        {
            if (document.getElementById(STYLE_ID) != null) return;

            var style = document.createElement("style");

            style.id          = STYLE_ID;
            style.textContent =
                "." + Line    + " { background: rgba(226,192,141,0.25); }" +
                "." + Glyph   + " { background: #4ec9b0; border-radius: 50%; width: 10px !important; height: 10px !important; margin-left: 4px; }" +
                "." + Inline  + " { text-decoration: underline wavy #4fc1ff; }" +
                "." + Note    + " { color: #6a9955; font-style: italic; }" +
                "." + Widget  + " { background: #4ec9b0; color: #1e1e1e; padding: 1px 6px; border-radius: 3px; font: 11px sans-serif; white-space: nowrap; }" +
                "." + Overlay + " { background: rgba(0,0,0,0.6); color: #fff; padding: 2px 8px; border-radius: 3px; font: 11px sans-serif; }" +
                "." + Zone    + " { background: rgba(79,193,255,0.12); color: #4fc1ff; font: 11px sans-serif; padding: 4px 12px; }";

            document.head.appendChild(style);
        }
    }
}
