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
        protected IEditor Instance { get; private set; }

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
            // column is off until it re-measures.
            document.fonts.ready.then(_ => MonacoApi.editor.remeasureFonts());

            _resizeObserver = new ResizeObserver((_, __) => Layout());
            _resizeObserver.observe(_container);

            DomObserver.WhenRemoved(_container, Dispose);

            AfterCreate();
        }

        /// <summary>Creates the underlying Monaco instance for <paramref name="container"/>.</summary>
        protected abstract IEditor Create(HTMLElement container);

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
            Instance?.layout();
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

            if (_resizeObserver != null)
            {
                _resizeObserver.disconnect();
                _resizeObserver = null;
            }

            if (Instance != null)
            {
                Instance.dispose();
                Instance = null;
            }
        }

        /// <summary>
        /// The default editor options shared by the editor and viewer - the font stack, the theme
        /// derived from Tesserae, and the popup host that lets suggest/hover widgets escape a
        /// clipping ancestor.
        /// </summary>
        protected EditorOptions BuildBaseOptions(string language, string value, bool readOnly, bool wordWrap, bool autoHeight)
        {
            var options = new EditorOptions
            {
                value                   = value ?? "",
                language                = language ?? "",
                readOnly                = readOnly,
                theme                   = MonacoEditor.ActiveTheme,
                roundedSelection        = false,
                minimap                 = new MinimapOptions { enabled = false },
                scrollBeyondLastLine    = !autoHeight,
                fixedOverflowWidgets    = true,
                bracketPairColorization = new BracketPairColorizationOptions { enabled = true },
                fontFamily              = MONOSPACE_FONT_FAMILY,
                fontSize                = 12,
                fontLigatures           = true,
                wordWrap                = wordWrap ? "on" : "off",
                wrappingIndent          = "same"
            };

            // With auto-height there is nothing to scroll to, so let the wheel keep scrolling the page.
            if (autoHeight)
            {
                options.scrollbar = new ScrollbarOptions { alwaysConsumeMouseWheel = false };
            }

            return options;
        }

        private const string MONOSPACE_FONT_FAMILY = "'Monaspace Neon', 'Monaspace Argon', 'Cascadia Code', 'Fira Code', Consolas, 'Courier New', monospace";

        /// <summary>
        /// Points Monaco's overflow widgets at the shared, body-mounted host when the caller left
        /// <c>fixedOverflowWidgets</c> on. Kept separate from <see cref="BuildBaseOptions"/> so it
        /// runs after any caller-supplied option overrides.
        /// </summary>
        protected static void ApplyOverflowWidgetsHost(EditorOptions options)
        {
            if (options.fixedOverflowWidgets)
            {
                options.overflowWidgetsDomNode = MonacoEditor.GetOverflowWidgetsHost();
            }
        }

        /// <summary>The diff editor's equivalent of <see cref="ApplyOverflowWidgetsHost(EditorOptions)"/>.</summary>
        protected static void ApplyOverflowWidgetsHost(DiffEditorOptions options)
        {
            if (options.fixedOverflowWidgets)
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
            var editor = (IStandaloneCodeEditor)Instance;

            if (editor is null) return;

            var previousHeight = 0d;

            void UpdateHeight()
            {
                var editorElement = editor.getDomNode();

                if (editorElement is null) return;

                var lineHeight = editor.getNumberOption(MonacoApi.editor.EditorOption.lineHeight);
                var model      = editor.getModel();
                var lineCount  = model is null ? 1 : model.getLineCount();
                var height     = editor.getTopForLineNumber(lineCount + 1) + lineHeight;

                if (previousHeight == height) return;

                previousHeight             = height;
                editorElement.style.height = height + "px";

                editor.layout();
            }

            editor.onDidChangeModelDecorations(() =>
            {
                UpdateHeight();
                window.requestAnimationFrame(_ => UpdateHeight());
            });

            UpdateHeight();
        }
    }
}
