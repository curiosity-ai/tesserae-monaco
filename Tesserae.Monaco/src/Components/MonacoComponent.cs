using System;
using System.Collections.Generic;
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
    ///
    /// A component is <b>remountable</b>. Leaving the DOM tears the editor down - the alternative leaks
    /// an editor per detach - but the component re-arms itself, so being added back builds a new editor
    /// and replays everything that was configured. That is what a component moved between containers, or
    /// inside a parent that detaches rather than hides, needs. <see cref="Dispose"/> is the one-way door:
    /// it opts out of that and releases the component for good.
    /// </summary>
    public abstract class MonacoComponent : IComponent, ISpecialCaseStyling
    {
        private readonly HTMLElement    _container;
        private          ResizeObserver _resizeObserver;
        private          bool           _mountRequested;
        private          bool           _disposed;

        /// <summary>The live Monaco instance, or null until the component has been mounted.</summary>
        protected IEditor Instance { get; private set; }

        /// <summary>
        /// Monaco disposables owned by this component - event subscriptions, provider registrations,
        /// actions - released together on teardown. Monaco hands one back from nearly every <c>on...</c>
        /// and <c>register...</c> call, and disposing the editor does not release the ones that live on a
        /// global registry.
        /// </summary>
        protected DisposableBag Disposables { get; } = new DisposableBag();

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

        /// <summary>
        /// Sizing helpers stay on the container and are not tagged for a wrapper-building container
        /// (Masonry, SectionStack, KeyedObservableStack) to hoist: Monaco measures the element it was
        /// created in, and hoisting the height onto a wrapper clears it here, leaving the editor with
        /// nothing to size against.
        /// </summary>
        public bool PropagateStylesToWrapper => false;

        public HTMLElement Render()
        {
            if (!_mountRequested)
            {
                _mountRequested = true;
                ArmMountObserver();
            }

            return _container;
        }

        private void ArmMountObserver()
        {
            DomObserver.WhenMounted(_container, () => MountAsync().FireAndForget());
        }

        private async Task MountAsync()
        {
            await MonacoEditor.LoadAsync();

            // The component can be discarded again, or torn down and remounted, while Monaco loads.
            if (_disposed || !_container.IsMounted()) return;

            // A second mount signal for an editor that already exists would create a duplicate.
            if (Instance != null) return;

            await WaitForAncestorAnimationsAsync();

            // Waiting yielded to the browser, so re-check both of the above.
            if (_disposed || !_container.IsMounted()) return;

            if (Instance != null) return;

            Instance = Create(_container);

            if (Instance is null) return;

            // Monaco measures character widths eagerly; if a web font lands after that, every
            // column is off until it re-measures.
            document.fonts.ready.then(_ => MonacoApi.editor.remeasureFonts());

            _resizeObserver = new ResizeObserver((_, __) => Layout());
            _resizeObserver.observe(_container);

            DomObserver.WhenRemoved(_container, HandleRemoved);

            AfterCreate();
        }

        /// <summary>
        /// Holds the editor back until no ancestor is mid-animation.
        ///
        /// Monaco sizes its scroll layer (<c>.lines-content</c>) to 16,777,216 x 16,777,216 px, and
        /// Chromium rasters an animating layer's whole subtree rather than the part in view. A layer
        /// that big inside an ancestor running a transform animation that starts near zero scale -
        /// Tesserae's <c>tss-modal-animation</c> starts at <c>scale(0)</c> - makes the raster work
        /// unbounded, and the renderer stops producing frames <b>for the whole page</b>: rAF never
        /// fires again, <c>document.timeline</c> stops, and every screenshot, keystroke and click
        /// hangs waiting for a frame that never comes. The main thread stays responsive throughout,
        /// which is what makes it look like a crash rather than a stall. Chromium eventually kills
        /// the tab.
        ///
        /// Creating the editor one frame later is enough - measured: the stall only happens while the
        /// ancestor's scale is under ~0.01, and the animation has climbed out of that range by its
        /// second frame. Waiting for the animation to finish outright also gets Monaco a container
        /// whose <c>getBoundingClientRect</c> is not scaled, which is what its font measurement reads.
        ///
        /// Bounded on both sides: only an animation that ends within <see cref="MAX_ANIMATION_WAIT_MS"/>
        /// is waited for - an infinite one (a spinner, a shimmer) reports <c>Infinity</c> and is
        /// ignored - and the loop itself gives up at the same limit, so an ancestor that keeps
        /// restarting its animation delays the editor rather than withholding it.
        /// </summary>
        private async Task WaitForAncestorAnimationsAsync()
        {
            for (var waited = 0; waited < MAX_ANIMATION_WAIT_MS && HasAnimatingAncestor(); waited += ANIMATION_POLL_MS)
            {
                await Task.Delay(ANIMATION_POLL_MS);
            }
        }

        private bool HasAnimatingAncestor()
        {
            var animations = document.getAnimations();

            if (animations is null) return false;

            foreach (var animation in animations)
            {
                if (animation.playState != "running") continue;

                // AnimationEffectReadOnly does not carry a target; a CSS animation, a transition and a
                // WAAPI animation all have a KeyframeEffect, which does. A direct cast to it emits
                // nothing - `as`/`is` would emit a runtime type test against metadata a dom type has
                // none of.
                var effect = (KeyframeEffect)animation.effect;

                if (effect is null) continue;

                // A direct cast, not `as`: a type test against an [External] type has no runtime
                // metadata to test against. The cast itself is erased.
                var target = effect.target;

                // contains() answers true for the element itself, which is what we want: the container
                // is as much of a problem animating itself as an ancestor animating it.
                if (target is null || !target.contains(_container)) continue;

                // An animation that never ends would hold the editor back for good; one that ends
                // beyond the limit would be cut short by the loop anyway.
                if (!(effect.getComputedTiming().endTime < MAX_ANIMATION_WAIT_MS)) continue;

                return true;
            }

            return false;
        }

        private const int ANIMATION_POLL_MS      = 16;
        private const int MAX_ANIMATION_WAIT_MS  = 1000;

        // Leaving the DOM tears the editor down but keeps the component usable: the mount observer is
        // re-armed, so being added back rebuilds the editor and replays the configuration. Without the
        // teardown a detached editor leaks; without the re-arm the component silently renders an empty
        // container ever after.
        private void HandleRemoved()
        {
            if (_disposed) return;

            Teardown();
            ArmMountObserver();
        }

        private void Teardown()
        {
            if (Instance is null) return;

            BeforeDispose();

            Disposables.DisposeAll();

            if (_resizeObserver != null)
            {
                _resizeObserver.disconnect();
                _resizeObserver = null;
            }

            Instance.dispose();
            Instance = null;
        }

        /// <summary>Creates the underlying Monaco instance for <paramref name="container"/>.</summary>
        protected abstract IEditor Create(HTMLElement container);

        /// <summary>Called once the instance exists, for per-component wiring.</summary>
        protected virtual void AfterCreate() { }

        /// <summary>
        /// Called before the Monaco instance is torn down - on leaving the DOM as well as on
        /// <see cref="Dispose"/>. Capture anything that should survive a remount here.
        /// </summary>
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
        /// Releases the component for good: tears the editor down and stops it being rebuilt if the
        /// container is mounted again. Leaving the DOM does <b>not</b> call this - it tears down and
        /// re-arms - so call it explicitly when a component is genuinely finished with.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            Teardown();
        }

        /// <summary>Whether <see cref="Dispose"/> has been called.</summary>
        public bool IsDisposed => _disposed;

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

        /// <summary>
        /// Layers the three sources of options in the one order that works: the typed setters over the
        /// defaults, the raw <c>Options(...)</c> callback over those - so a caller can always win - and the
        /// shared overflow host last, since it has to see the final value of <c>fixedOverflowWidgets</c>.
        /// </summary>
        protected EditorOptions FinishOptions(EditorOptions options, IEnumerable<Action<EditorOptions>> typedSetters, Action<EditorOptions> configureOptions)
        {
            if (typedSetters != null)
            {
                foreach (var set in typedSetters)
                {
                    set(options);
                }
            }

            configureOptions?.Invoke(options);

            ApplyOverflowWidgetsHost(options);

            return options;
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
        /// Grows the container to fit the content, so the editor never scrolls vertically.
        ///
        /// Driven by Monaco's own <c>onDidContentSizeChange</c>: it fires for anything that changes the
        /// content's height - typing, wrapping, folding, a view zone opening - which is both cheaper and
        /// more complete than watching decorations and deriving a height from the line count.
        /// </summary>
        protected void EnableAutoHeight()
        {
            if (Instance is null) return;

            // A direct cast rather than `as`: a type test against an [External] interface has no metadata
            // to test against, and throws instead of answering false.
            var editor = (IStandaloneCodeEditor)Instance;

            var previousHeight = 0d;

            void Apply()
            {
                var node = editor.getDomNode();

                if (node is null) return;

                var height = editor.getContentHeight();

                if (previousHeight == height) return;

                previousHeight    = height;
                node.style.height = height + "px";

                editor.layout();
            }

            Disposables.Add(editor.onDidContentSizeChange(_ => Apply()));

            Apply();
        }

    }
}
