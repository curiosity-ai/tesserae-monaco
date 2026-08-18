using System;
using System.Threading.Tasks;
using Tesserae;
using Transpose;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The Monaco objects the package calls methods on, beyond the editor and the model themselves -
    /// and the read-only event payloads it is handed.
    ///
    /// Same rules as <see cref="MonacoApi"/>: every member is an <c>[External]</c> declaration, nothing
    /// is emitted, and <c>[Convention(Notation.None)]</c> keeps the C# names identical to the
    /// JavaScript ones.
    ///
    /// Event payloads are declared as interfaces with getters rather than as <c>[ObjectLiteral]</c>
    /// classes, because the package only ever reads them - an <c>[ObjectLiteral]</c> is for a value
    /// being <i>constructed</i> to hand to Monaco.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IUri
    {
        string scheme { get; }
        string path { get; }

        [Name("toString")]
        string asString();
    }

    /// <summary><c>monaco.Uri</c> - building the URIs a model is addressed by.</summary>
    [External]
    [Convention(Notation.None)]
    [Name("monaco.Uri")]
    public static class MonacoUri
    {
        /// <summary>Parses a full URI, e.g. <c>"inmemory://model/1"</c> or <c>"file:///src/main.ts"</c>.</summary>
        public static extern IUri parse(string value);

        /// <summary>Builds a <c>file:</c> URI from a path.</summary>
        public static extern IUri file(string path);
    }

    /// <summary>An editor action, as returned by <c>editor.getAction</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditorAction
    {
        string id { get; }
        string label { get; }

        /// <summary>Whether the action is currently enabled - a disabled one runs to no effect.</summary>
        bool isSupported();

        IPromise run();
    }

    /// <summary>
    /// A decoration set Monaco keeps tracked across edits, from
    /// <c>editor.createDecorationsCollection</c>. Updating one beats replacing it: the ranges move with
    /// the text, and only the difference is re-rendered.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditorDecorationsCollection
    {
        int length { get; }

        void set(ReadOnlyArray<TextDecoration> newDecorations);
        void append(ReadOnlyArray<TextDecoration> newDecorations);
        void clear();

        /// <summary>Where a decoration is <b>now</b>, after any edits since it was set.</summary>
        TextRange getRange(int index);

        TextRange[] getRanges();
    }

    /// <summary>A named boolean in one editor's context, from <c>editor.createContextKey</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IContextKey
    {
        void set(bool value);
        void reset();
        bool get();
    }

    /// <summary>
    /// What <c>editor.changeViewZones</c> hands its callback. Zones may only be added or removed
    /// through it, inside that callback.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IViewZoneAccessor
    {
        string addZone(ViewZoneDescriptor zone);
        void   removeZone(string id);
        void   layoutZone(string id);
    }

    /// <summary>
    /// An editor's saved caret, selection, scroll offset and folding state. Opaque on purpose - it is
    /// only ever handed back to <c>restoreViewState</c>.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IViewState
    {
    }

    /// <summary>One hit from <c>model.findMatches</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IFindMatch
    {
        TextRange range { get; }
    }

    // ---------------------------------------------------------------------------------------------
    // Event payloads
    // ---------------------------------------------------------------------------------------------

    /// <summary>One replacement within a content change, matching Monaco's <c>IModelContentChange</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IModelContentChange
    {
        /// <summary>The range that was replaced, in the document as it was before the change.</summary>
        TextRange range { get; }

        int    rangeOffset { get; }
        int    rangeLength { get; }

        /// <summary>The text that replaced it - empty for a deletion.</summary>
        string text { get; }
    }

    /// <summary>
    /// What changed in a document, matching Monaco's <c>IModelContentChangedEvent</c> - the detail an
    /// argument-less change callback throws away. Without <see cref="versionId"/> there is no cheap way
    /// to discard a stale async result.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IContentChangedEvent
    {
        IModelContentChange[] changes { get; }

        /// <summary>The model's version after the change; increases on every edit.</summary>
        int  versionId { get; }

        /// <summary>True when the whole document was replaced, e.g. by <c>setValue</c>.</summary>
        bool isFlush { get; }

        bool isUndoing { get; }
        bool isRedoing { get; }
        bool isEolChange { get; }
    }

    /// <summary>The caret moved, matching Monaco's <c>ICursorPositionChangedEvent</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ICursorPositionChangedEvent
    {
        Position   position { get; }

        /// <summary>The other carets, when multi-cursor is in play.</summary>
        Position[] secondaryPositions { get; }

        /// <summary>Monaco's <c>CursorChangeReason</c> - see <see cref="CursorChangeReason"/>.</summary>
        int        reason { get; }

        /// <summary>Who moved it - <c>"mouse"</c>, <c>"keyboard"</c>, or an action id.</summary>
        string     source { get; }
    }

    /// <summary>The selection changed, matching Monaco's <c>ICursorSelectionChangedEvent</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ICursorSelectionChangedEvent
    {
        TextSelection   selection { get; }
        TextSelection[] secondarySelections { get; }
        int             reason { get; }
        string          source { get; }
    }

    /// <summary>
    /// The content's own size changed, matching Monaco's <c>IContentSizeChangedEvent</c> - the right
    /// signal for growing a container to fit.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IContentSizeChangedEvent
    {
        double contentWidth { get; }
        double contentHeight { get; }
        bool   contentWidthChanged { get; }
        bool   contentHeightChanged { get; }
    }

    /// <summary>The editor scrolled, matching Monaco's <c>IScrollEvent</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IScrollEvent
    {
        double scrollTop { get; }
        double scrollLeft { get; }
        double scrollWidth { get; }
        double scrollHeight { get; }
        bool   scrollTopChanged { get; }
        bool   scrollLeftChanged { get; }
    }

    /// <summary>
    /// A key event as Monaco sees it, matching <c>IKeyboardEvent</c>. <see cref="keyCode"/> is Monaco's
    /// own <see cref="KeyCode"/>, not the browser's <c>keyCode</c> and not ASCII.
    /// </summary>
    [External]
    [Convention(Notation.None)]
    public interface IKeyboardEvent
    {
        int    keyCode { get; }
        bool   ctrlKey { get; }
        bool   shiftKey { get; }
        bool   altKey { get; }
        bool   metaKey { get; }

        /// <summary>The physical key, e.g. <c>"KeyS"</c>.</summary>
        string code { get; }

        void preventDefault();
        void stopPropagation();
    }

    /// <summary>Where a mouse event landed, matching Monaco's <c>IMouseTarget</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IMouseTarget
    {
        /// <summary>Monaco's <c>MouseTargetType</c> - see <see cref="MouseTargetType"/>.</summary>
        int         type { get; }

        /// <summary>The document position under the pointer, or null when it isn't over text.</summary>
        Position    position { get; }

        TextRange   range { get; }
        HTMLElement element { get; }
    }

    /// <summary>A mouse event on the editor, matching Monaco's <c>IEditorMouseEvent</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IEditorMouseEvent
    {
        IMouseTarget target { get; }
    }

    /// <summary>Text was pasted, matching Monaco's <c>IPasteEvent</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IPasteEvent
    {
        /// <summary>The range the pasted text now occupies.</summary>
        TextRange range { get; }

        string    languageId { get; }
    }

    /// <summary>A model's language changed, matching Monaco's <c>IModelLanguageChangedEvent</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ILanguageChangedEvent
    {
        string oldLanguage { get; }
        string newLanguage { get; }
    }

    // ---------------------------------------------------------------------------------------------
    // Bundled language services
    // ---------------------------------------------------------------------------------------------

    /// <summary><c>monaco.languages.json</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IJsonApi
    {
        IJsonDefaults jsonDefaults { get; }
    }

    /// <summary>The JSON service's settings - validation, and the schemas to validate against.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IJsonDefaults
    {
        void setDiagnosticsOptions(JsonDiagnosticsOptions options);
    }

    /// <summary><c>monaco.languages.typescript</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ITypeScriptApi
    {
        ITypeScriptDefaults typescriptDefaults { get; }
        ITypeScriptDefaults javascriptDefaults { get; }
    }

    /// <summary>The TypeScript service's settings - what it type-checks against, and what it reports.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ITypeScriptDefaults
    {
        void setCompilerOptions(TypeScriptCompilerOptions options);
        void setDiagnosticsOptions(TypeScriptDiagnosticsOptions options);

        /// <summary>Declarations the service should know about. Returns a handle that removes them again.</summary>
        IJsDisposable addExtraLib(string content, string filePath);

        /// <summary>
        /// Whether the worker sees every model or only ones handed to it explicitly. With this off, a
        /// plain editor gets syntax errors but no type errors - which reads as the service not working.
        /// </summary>
        void setEagerModelSync(bool value);
    }

    /// <summary><c>monaco.languages.css</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ICssApi
    {
        ICssDefaults cssDefaults { get; }
        ICssDefaults scssDefaults { get; }
        ICssDefaults lessDefaults { get; }
    }

    [External]
    [Convention(Notation.None)]
    public interface ICssDefaults
    {
        void setOptions(CssOptions options);
    }

    /// <summary><c>monaco.languages.html</c>.</summary>
    [External]
    [Convention(Notation.None)]
    public interface IHtmlApi
    {
        IHtmlDefaults htmlDefaults { get; }
        IHtmlDefaults handlebarDefaults { get; }
        IHtmlDefaults razorDefaults { get; }
    }

    [External]
    [Convention(Notation.None)]
    public interface IHtmlDefaults
    {
        void setOptions(HtmlOptions options);
    }
}
