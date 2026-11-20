using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The one stylesheet the AI variant needs, injected the first time it is used. It is not a palette of its own:
    /// every colour is one of Tesserae's <c>--tss-ai-*</c> tokens (<c>tss.ai.css</c>), the same ones
    /// <c>TextArea.AI()</c> and <c>Card.AI()</c> paint with, so an editor marked as the model's reads as the same
    /// thing as every other AI surface in the app and follows the light/dark theme and a custom theme's AI hue with
    /// no code here. It has to be CSS: a Monaco decoration can only name a class, never carry a style.
    /// </summary>
    internal static class AIStyles
    {
        private const string STYLE_ID = "tssm-ai-styles";

        internal const string Editor       = "tssm-ai-editor";
        internal const string BadgeHost    = "tssm-ai-badge";
        internal const string Line         = "tssm-ai-line";
        internal const string LineEdited   = "tssm-ai-line-edited";
        internal const string Gutter       = "tssm-ai-gutter";
        internal const string GutterEdited = "tssm-ai-gutter-edited";
        internal const string Range        = "tssm-ai-range";
        internal const string RangeEdited  = "tssm-ai-range-edited";

        internal static void Ensure()
        {
            if (document.getElementById(STYLE_ID) != null) return;

            var style = document.createElement("style");

            style.id          = STYLE_ID;
            style.textContent =
                // The whole editor: Tesserae's AI field edge (a hairline in the AI border colour, the accent down the
                // left) and its tint, drawn over Monaco - which paints its own opaque background, so nothing under it
                // would show. Translucent and click-through, so the text stays readable and the editor usable.
                "." + Editor + "::after { content: ''; position: absolute; inset: 0; pointer-events: none; z-index: 6;" +
                    " border: 1px solid var(--tss-ai-border-color); border-left: 3px solid var(--tss-ai-accent); background: var(--tss-ai-surface); }" +
                "." + Editor + ".tss-ai-strong::after { background: var(--tss-ai-surface-strong); border-left-width: 4px; }" +
                // Above the tint, in the overlay-widget corner Monaco keeps clear of the minimap and the scrollbar.
                "." + BadgeHost + " { position: relative; z-index: 7; margin: 6px 6px 0 0; display: flex; pointer-events: auto; }" +

                // A line the model wrote: the tint behind the text, and the accent in the strip beside the numbers.
                "." + Line + " { background: var(--tss-ai-surface); }" +
                "." + Gutter + " { background: linear-gradient(180deg, var(--tss-ai-from), var(--tss-ai-to)); width: 3px !important; margin-left: 4px; }" +
                // Edited since: no tint, and the strip goes dashed - the badge's filled-to-outline step, for a line.
                "." + GutterEdited + " { background: repeating-linear-gradient(180deg, var(--tss-ai-accent) 0 3px, transparent 3px 6px); width: 3px !important; margin-left: 4px; }" +

                // A sentence or a span the model wrote: tinted, with an accent underline.
                "." + Range + " { background: var(--tss-ai-surface-strong); box-shadow: inset 0 -2px 0 var(--tss-ai-border-color); }" +
                "." + RangeEdited + " { text-decoration: underline dashed var(--tss-ai-accent); text-underline-offset: 4px; }";

            document.head.appendChild(style);
        }

        /// <summary>
        /// The AI accent as a concrete colour, for the overview ruler and the minimap: those take a colour string, not
        /// a class, and a <c>var(...)</c> means nothing to the canvas Monaco draws them on.
        /// </summary>
        internal static string AccentColor()
        {
            var value = window.getComputedStyle(document.body).getPropertyValue("--tss-ai-accent");
            value = value is null ? "" : value.Trim();
            return string.IsNullOrEmpty(value) ? "#7c3aed" : value;
        }
    }
}
