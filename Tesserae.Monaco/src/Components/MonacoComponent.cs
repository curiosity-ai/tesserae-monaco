using System;
using System.Threading.Tasks;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Shared plumbing for the Monaco-backed components: a sized container element, the
    /// mount/create/dispose lifecycle, and keeping Monaco's internal layout in step with the
    /// container's size.
    ///
    /// Monaco can only measure itself once it is in the document, so the underlying editor is
    /// created lazily on mount rather than in the constructor. Everything configured before that
    /// point is captured in fields and applied when the editor is created; everything configured
    /// afterwards is forwarded to the live instance. Each component's property setters follow that
    /// same "field if not created yet, otherwise forward" shape.
    /// </summary>
    public abstract class MonacoComponent : IComponent, ISpecialCaseStyling
    {
        private readonly HTMLElement    _container;
        private          ResizeObserver _resizeObserver;
        private          bool           _mountRequested;
        private          bool           _disposed;

        /// <summary>The live Monaco instance, or null until the component has been mounted.</summary>
        protected object Instance { get; private set; }

        protected MonacoComponent()
        {
            _container                = DIV();
            _container.style.width     = "100%";
            _container.style.height    = "100%";
            _container.style.overflow  = "hidden";
            _container.style.position  = "relative";
        }

        /// <summary>The container element - styled directly by the Tesserae sizing helpers.</summary>
        public HTMLElement StylingContainer => _container;

        /// <summary>Monaco needs a sized container, so sizing stays on the container itself.</summary>
        public bool PropagateToStackItemParent => false;

        public HTMLElement Render()
        {
            if (!_mountRequested)
            {
                _mountRequested = true;
                DomObserver.WhenMounted(_container, () => MountAsync().FireAndForget());
            }

            return _container;
        }

        private async Task MountAsync()
        {
            await MonacoEditor.LoadAsync();

            // The component can be discarded again while Monaco is still loading.
            if (_disposed || !_container.IsMounted()) return;

            Instance = Create(_container);

            if (Instance is null) return;

            // Monaco measures character widths eagerly; if a web font lands after that, every
            // column is off until it re-measures. Note the block body: an expression-bodied lambda
            // would make Transpose emit `return <script>`, which is a syntax error for a void
            // Script.Write.
            document.fonts.ready.then(_ =>
            {
                Script.Write("monaco.editor.remeasureFonts()");
            });

            _resizeObserver = new ResizeObserver((_, __) => Layout());
            _resizeObserver.observe(_container);

            DomObserver.WhenRemoved(_container, Dispose);

            AfterCreate();
        }

        /// <summary>Creates the underlying Monaco instance for <paramref name="container"/>.</summary>
        protected abstract object Create(HTMLElement container);

        /// <summary>Called once the instance exists, for per-component wiring.</summary>
        protected virtual void AfterCreate() { }

        /// <summary>Called when the component leaves the DOM, for per-component cleanup.</summary>
        protected virtual void BeforeDispose() { }

        /// <summary>
        /// Re-measures the editor against its container. Called automatically when the container
        /// resizes; useful by hand after showing a previously hidden ancestor.
        /// </summary>
        public void Layout()
        {
            if (Instance is null) return;

            Script.Write("{0}.layout()", Instance);
        }

        /// <summary>
        /// Disposes the Monaco instance and stops observing the container. Called automatically when
        /// the component is removed from the DOM.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            BeforeDispose();

            if (_resizeObserver is object)
            {
                _resizeObserver.disconnect();
                _resizeObserver = null;
            }

            if (Instance is object)
            {
                Script.Write("{0}.dispose()", Instance);
                Instance = null;
            }
        }

        /// <summary>
        /// The default editor options shared by the editor and viewer - the font stack, the theme
        /// derived from Tesserae, and the popup host that lets suggest/hover widgets escape a
        /// clipping ancestor.
        /// </summary>
        protected dynamic BuildBaseOptions(string language, string value, bool readOnly, bool wordWrap, bool autoHeight)
        {
            dynamic options = new
            {
                value                   = value ?? "",
                language                = language ?? "",
                readOnly,
                theme                   = MonacoEditor.ActiveTheme,
                roundedSelection        = false,
                minimap                 = new { enabled = false },
                scrollBeyondLastLine    = !autoHeight,
                fixedOverflowWidgets    = true,
                bracketPairColorization = new { enabled = true },
                fontFamily              = MONOSPACE_FONT_FAMILY,
                fontSize                = 12,
                fontLigatures           = true,
                wordWrap                = wordWrap ? "on" : "off",
                wrappingIndent          = "same"
            };

            // With auto-height there is nothing to scroll to, so let the wheel keep scrolling the page.
            if (autoHeight)
            {
                options.scrollbar = new { alwaysConsumeMouseWheel = false };
            }

            return options;
        }

        private const string MONOSPACE_FONT_FAMILY = "'Monaspace Neon', 'Monaspace Argon', 'Cascadia Code', 'Fira Code', Consolas, 'Courier New', monospace";

        /// <summary>
        /// Points Monaco's overflow widgets at the shared, body-mounted host when the caller left
        /// <c>fixedOverflowWidgets</c> on. Kept separate from <see cref="BuildBaseOptions"/> so it
        /// runs after any caller-supplied option overrides.
        /// </summary>
        protected static void ApplyOverflowWidgetsHost(dynamic options)
        {
            if (Script.Write<bool>("!!{0}.fixedOverflowWidgets", options))
            {
                options.overflowWidgetsDomNode = MonacoEditor.GetOverflowWidgetsHost();
            }
        }

        /// <summary>
        /// Grows the container to fit the content, so the editor never scrolls vertically. Monaco has
        /// no built-in option for this; the height is recomputed whenever the rendered line count
        /// changes (typing, and - via the animation frame - folding).
        /// </summary>
        protected void EnableAutoHeight()
        {
            if (Instance is null) return;

            Script.Write(
                @"(function(edt) {
                    var prevHeight = 0;

                    var updateEditorHeight = function () {
                        var editorElement = edt.getDomNode();
                        if (!editorElement) { return; }
                        var lineHeight = edt.getOption(monaco.editor.EditorOption.lineHeight);
                        var lineCount = edt.getModel() == null ? 1 : edt.getModel().getLineCount();
                        var height = edt.getTopForLineNumber(lineCount + 1) + lineHeight;

                        if (prevHeight !== height) {
                            prevHeight = height;
                            editorElement.style.height = height + 'px';
                            edt.layout();
                        }
                    };

                    edt.onDidChangeModelDecorations(function () {
                        updateEditorHeight();
                        requestAnimationFrame(updateEditorHeight);
                    });

                    updateEditorHeight();
                })({0})",
                Instance
            );
        }
    }
}
