using Transpose;
using Transpose.Core;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The two pieces of the Web Animations API the mount path needs that the pinned
    /// <c>Transpose.Core</c> does not declare yet. Everything else comes from there:
    /// <see cref="dom.Animation"/>, <see cref="dom.AnimationEffectReadOnly"/> and
    /// <see cref="dom.ComputedTimingProperties"/> (which is where <c>endTime</c> lives) are all already
    /// bound, so only the discovery call and the effect's target are declared here.
    ///
    /// Both have since been added to <c>Transpose.Core</c> as <c>document.getAnimations()</c> and
    /// <c>dom.KeyframeEffect</c>; delete this file and use those once the pin moves past
    /// <c>26.7.3304</c>.
    ///
    /// Only the read side is needed. An editor never starts an animation; it only needs to know whether
    /// one is running on an ancestor, because Monaco's 16.7-million-pixel scroll layer inside an
    /// ancestor that is animating a transform stalls the compositor - see
    /// <see cref="MonacoComponent"/>.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    [Name("document")]
    internal static class JsDocumentAnimations
    {
        /// <summary>Every animation currently associated with the document, running or not.</summary>
        public static extern Animation[] getAnimations();
    }

    /// <summary>
    /// The concrete effect a CSS animation, a CSS transition or <c>Element.animate()</c> produces - the
    /// one that names the element being animated. <see cref="dom.Animation.effect"/> is typed as the
    /// base, which carries only the timing, so the walk casts to this; the cast is erased, since an
    /// external type from a binding library is not runtime-checked.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    internal abstract class JsKeyframeEffect : AnimationEffectReadOnly
    {
        /// <summary>The animated element, or null for an effect that targets none.</summary>
        public abstract Element target { get; }
    }
}
