using System;
using System.Threading.Tasks;
using Transpose;
using Tesserae;
using Transpose.Core;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco
{
    public static partial class MonacoEditor
    {
        /// <summary>
        /// The marker owner the components write under. Markers are scoped per owner, so the host's
        /// squiggles and the ones Monaco's own workers produce coexist rather than overwrite each other.
        /// </summary>
        public const string DEFAULT_MARKER_OWNER = "tss-monaco";

        #region Models

        /// <summary>
        /// Creates a document, optionally with a URI. See <see cref="CodeModel"/> for why a host would
        /// want one - several files in one editor, edits that keep the undo stack, and addressing a
        /// document from a language service.
        /// </summary>
        public static CodeModel CreateModel(string text, string language = null, string uri = null)
        {
            return CodeModel.Create(text, language, uri);
        }

        /// <summary>
        /// The model already registered for a URI, or null. Check this before <see cref="CreateModel"/>
        /// when the same file might be opened twice: Monaco throws when a URI is claimed a second time.
        /// </summary>
        public static CodeModel GetModel(string uri)
        {
            if (!IsLoaded || string.IsNullOrWhiteSpace(uri)) return null;

            return CodeModel.Wrap(MonacoApi.editor.getModel(MonacoUri.parse(uri)));
        }

        /// <summary>Every model Monaco currently holds, including the ones editors created for themselves.</summary>
        public static CodeModel[] GetModels()
        {
            if (!IsLoaded) return new CodeModel[0];

            var natives = MonacoApi.editor.getModels();
            var models  = new CodeModel[natives.Length];

            for (var i = 0; i < natives.Length; i++)
            {
                models[i] = CodeModel.Wrap(natives[i]);
            }

            return models;
        }

        /// <summary>
        /// Every editor on the page, in creation order - which includes a diff editor's two inner
        /// editors, so it is not a list of components.
        /// </summary>
        public static IStandaloneCodeEditor[] GetEditors()
        {
            return IsLoaded ? MonacoApi.editor.getEditors() : new IStandaloneCodeEditor[0];
        }

        #endregion

        #region Markers

        /// <summary>
        /// Every marker on the page, or just one document's when <paramref name="uri"/> is given - the
        /// host's own and the ones the bundled JSON, TypeScript, CSS and HTML workers produce.
        /// </summary>
        public static CodeMarker[] GetMarkers(string uri = null)
        {
            if (!IsLoaded) return new CodeMarker[0];

            var filter = string.IsNullOrWhiteSpace(uri)
                ? new MarkerFilter()
                : new MarkerFilter { resource = MonacoUri.parse(uri) };

            return MonacoApi.editor.getModelMarkers(filter);
        }

        /// <summary>
        /// Runs <paramref name="handler"/> whenever markers change on any document.
        ///
        /// This is the only way to react to a worker's diagnostics: they arrive asynchronously, well
        /// after the edit that caused them, so reading markers straight after typing sees the previous
        /// state. Pass a <paramref name="bag"/> to have the subscription released with a component.
        /// </summary>
        public static IJsDisposable OnMarkersChanged(Action handler, DisposableBag bag = null)
        {
            if (!IsLoaded || handler is null) return null;

            var registration = MonacoApi.editor.onDidChangeMarkers(handler);

            bag?.Add(registration);

            return registration;
        }

        #endregion

        #region Static colorization

        /// <summary>
        /// Syntax-highlights a string to HTML without creating an editor.
        ///
        /// Much cheaper than a <see cref="CodeViewer"/> for a snippet nobody will interact with - no
        /// editor instance, no model, no view. The result is a fragment to drop into an element; it has
        /// no selection, no scrolling and no line numbers.
        /// </summary>
        public static Task<string> ColorizeAsync(string code, string language)
        {
            if (!IsLoaded || code is null) return Task.FromResult("");

            // Task.FromPromise, not await: awaiting an IPromise is typed as handing back the resolved
            // values as an array, while the runtime passes the native promise straight through - the same
            // trap the hover provider documents. FromPromise is the BCL's own adapter for this direction,
            // the mirror of PromiseExtensions.ToPromise; the handler picks the single resolved value out,
            // and a rejection faults the task.
            return Task.FromPromise<string>(
                MonacoApi.editor.colorize(code, language ?? "plaintext", new ColorizeOptions()),
                new Func<object, string>(html => html as string ?? ""));
        }

        /// <summary>
        /// Highlights code into a new element, ready to append. The element carries Monaco's
        /// <c>monaco-editor</c> class so the theme's colours apply to it, and fills in when Monaco's
        /// colorizer resolves - so it can be appended without awaiting.
        /// </summary>
        public static HTMLElement Colorize(string code, string language, string className = null)
        {
            var host = DIV();
            host.className = "monaco-editor" + (string.IsNullOrWhiteSpace(className) ? "" : " " + className);

            if (!IsLoaded || code is null) return host;

            FillAsync().FireAndForget();

            return host;

            async Task FillAsync()
            {
                host.innerHTML = await ColorizeAsync(code, language);
            }
        }

        /// <summary>
        /// Highlights the code already inside an element, in place. Monaco reads the language from the
        /// element's <c>data-lang</c> attribute unless <paramref name="mimeTypeOrLanguage"/> says otherwise.
        /// </summary>
        public static void ColorizeElement(HTMLElement element, string mimeTypeOrLanguage = null)
        {
            if (!IsLoaded || element is null) return;

            var options = new ColorizeElementOptions();

            if (!string.IsNullOrWhiteSpace(mimeTypeOrLanguage)) options.mimeType = mimeTypeOrLanguage;

            MonacoApi.editor.colorizeElement(element, options);
        }

        #endregion

        #region Web workers

        /// <summary>
        /// Creates a Monaco-managed web worker running the host's own module, for analysis too expensive
        /// for the main thread. The returned proxy is Monaco's <c>MonacoWebWorker</c>.
        ///
        /// The worker module has to be reachable as a plain script from the page - the bundle's own worker
        /// wiring does not cover a host's module. <paramref name="createData"/> crosses to the worker, so
        /// it goes through <see cref="ToPlainObject"/>.
        /// </summary>
        public static object CreateWebWorker(string moduleId, object createData = null, string label = null)
        {
            if (!IsLoaded || string.IsNullOrWhiteSpace(moduleId)) return null;

            return MonacoApi.editor.createWebWorker(new WebWorkerOptions
            {
                moduleId   = moduleId,
                createData = ToPlainObject(createData),
                label      = label
            });
        }

        #endregion

        #region Worker-safe values

        /// <summary>
        /// A structured-clone-safe copy of <paramref name="value"/> - plain objects and arrays only, no
        /// prototypes and no functions.
        ///
        /// Anything Monaco forwards to a web worker has to survive <c>postMessage</c>, and values built
        /// from C# do not. Two separate reasons, both measured:
        ///
        /// <list type="bullet">
        /// <item>A typed array carries a <c>$type</c> property holding a <b>function</b> - Transpose's
        /// element-type bookkeeping. <c>Array.isArray</c> is still true and the elements are fine, but
        /// <c>structuredClone</c> refuses the whole thing. <see cref="Script.ToArray"/> is the cheaper fix
        /// where the value is just an array.</item>
        /// <item>An anonymous type is emitted as a real class unless the host sets Transpose's
        /// <c>anonymousType: "Plain"</c> rule, and a class instance is not cloneable either.</item>
        /// </list>
        ///
        /// The failure is a <c>DataCloneError</c> thrown from inside Monaco, naming a function body and
        /// nothing else - so the wrapper normalises the values it forwards rather than leaving the trap.
        /// </summary>
        public static object ToPlainObject(object value)
        {
            if (value is null) return null;

            try
            {
                return es5.JSON.parse(es5.JSON.stringify(value));
            }
            catch (Exception exception)
            {
                console.error("Tesserae.Monaco: a value could not be normalised for a worker", exception);

                return value;
            }
        }

        #endregion
    }
}
