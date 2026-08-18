using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The global <c>monaco</c> object, declared to the compiler instead of reached through
    /// <c>Script.Write</c>. Every member below is an <c>[External]</c> declaration: nothing is
    /// emitted for it, a call site compiles straight to the JavaScript it names, and a typo or a
    /// wrong argument becomes a build error rather than a runtime one.
    ///
    /// <c>[Convention(Notation.None)]</c> is what keeps the C# names identical to the JavaScript
    /// ones - without it the compiler camel-cases members, and <c>monaco.KeyMod.Alt</c> would be
    /// emitted as <c>monaco.KeyMod.alt</c>.
    ///
    /// Only what this package needs is declared. Monaco's surface is far larger; add to these
    /// interfaces as needed rather than reaching back for a raw script string.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    [Name("monaco")]
    public static class MonacoApi
    {
        /// <summary><c>monaco.editor</c>.</summary>
        public static extern IEditorApi editor { get; }

        /// <summary><c>monaco.languages</c>.</summary>
        public static extern ILanguagesApi languages { get; }

        /// <summary><c>monaco.KeyMod</c> - the modifier bits of a keybinding.</summary>
        public static extern IKeyMod KeyMod { get; }

        /// <summary><c>monaco.KeyCode</c> - the key part of a keybinding.</summary>
        public static extern IKeyCode KeyCode { get; }
    }

    /// <summary>
    /// <c>window.monaco</c>, for asking whether Monaco has loaded yet.
    ///
    /// Deliberately reached through <c>window</c>: a bare <c>monaco</c> reference throws a
    /// <c>ReferenceError</c> before the bundle's script has run, while a missing property on
    /// <c>window</c> is simply <c>undefined</c>.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    [Name("window")]
    internal static class JsWindow
    {
        public static extern IMonacoNamespace monaco { get; }
    }

    /// <summary>Just enough of the <c>monaco</c> namespace to tell a loaded bundle from an absent one.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IMonacoNamespace
    {
        IEditorApi editor { get; }
    }
}
