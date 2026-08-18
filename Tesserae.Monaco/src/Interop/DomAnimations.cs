using Transpose;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The Web Animations surface the mount path needs, declared the same way as the Monaco API:
    /// <c>[External]</c> so nothing is emitted, <c>[Convention(Notation.None)]</c> so the C# names stay
    /// byte-for-byte the JavaScript ones.
    ///
    /// Only the read side is here. An editor never starts an animation; it only needs to know whether
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
        public static extern IWebAnimation[] getAnimations();
    }

    /// <summary>One entry of <c>document.getAnimations()</c> - a CSS animation, transition or WAAPI animation.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IWebAnimation
    {
        /// <summary><c>"running"</c>, <c>"paused"</c>, <c>"finished"</c> or <c>"idle"</c>.</summary>
        string playState { get; }

        /// <summary>What the animation animates - null for an animation with no effect.</summary>
        IAnimationEffect effect { get; }
    }

    /// <summary>The effect of an animation: which element it targets, and its resolved timing.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IAnimationEffect
    {
        /// <summary>The animated element, or null for an effect that targets none.</summary>
        HTMLElement target { get; }

        IComputedEffectTiming getComputedTiming();
    }

    /// <summary>The timing an effect resolved to, after CSS and the timeline have had their say.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IComputedEffectTiming
    {
        /// <summary>
        /// When the effect ends, in milliseconds from its own start - delay plus every iteration.
        /// <c>Infinity</c> for an animation set to repeat forever.
        /// </summary>
        double endTime { get; }
    }
}
