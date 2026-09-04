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
        /// prototypes, no functions and no Transpose bookkeeping.
        ///
        /// Anything Monaco forwards to a web worker has to survive <c>postMessage</c>, and values built
        /// from C# do not. Two separate reasons, both measured:
        ///
        /// <list type="bullet">
        /// <item>A typed array carries a <c>$type</c> property holding a <b>function</b> - Transpose's
        /// element-type bookkeeping. <c>Array.isArray</c> is still true and the elements are fine, but
        /// <c>structuredClone</c> refuses the whole thing. <see cref="Script.ToArray"/> is the cheaper fix
        /// where the value is just an array.</item>
        /// <item>A class instance - a host's own type handed over as worker data, or a boxed value - is
        /// a prototype and a set of functions around the data, and what reaches the worker has to be
        /// the data alone.</item>
        /// </list>
        ///
        /// The failure is a <c>DataCloneError</c> thrown from inside Monaco, naming a function body and
        /// nothing else - so the wrapper normalises the values it forwards rather than leaving the trap.
        ///
        /// This walks the graph once rather than going through <c>JSON.stringify</c> and back, and
        /// keeps to what that round trip produced where the two can agree: a member whose value is a
        /// function or <c>undefined</c> is left out, a type's own <c>toJSON</c> is honoured (which is how
        /// the runtime serialises a class instance and a <c>List&lt;T&gt;</c>, and how Monaco's
        /// <c>Uri</c> serialises itself), and every object and array in the result is a fresh one. Where
        /// they differ, the copy keeps what the text form lost: a <c>Date</c> stays a <c>Date</c>, a
        /// typed array (<c>Uint32Array</c> and the like) and an <c>ArrayBuffer</c> pass through untouched
        /// since they are already clone-safe, a boxed value is unboxed, <c>NaN</c> and infinities are kept,
        /// and a graph that shares an object or refers back to itself is copied with the same shape
        /// instead of throwing.
        /// </summary>
        public static object ToPlainObject(object value)
        {
            if (value is null) return null;

            try
            {
                return Plain(value, new es5.Map<object, object>());
            }
            catch (Exception exception)
            {
                console.error("Tesserae.Monaco: a value could not be normalised for a worker", exception);

                return value;
            }
        }

        /// <summary>
        /// The recursive half of <see cref="ToPlainObject"/>. <paramref name="seen"/> maps every source
        /// object already copied to its copy, which is what makes a shared reference come out shared
        /// and a cycle come out as a cycle rather than as a stack overflow.
        /// </summary>
        private static object Plain(object value, es5.Map<object, object> seen, bool honourToJson = true)
        {
            // `is null` is emitted as == null, so this covers undefined too - which, as an array
            // element, is what JSON turned into null as well.
            if (value is null) return null;

            var kind = Script.TypeOf(value);

            // Strings, numbers, booleans (and a bigint or a symbol, which are theirs to have passed)
            // are their own copy. A bare function has no plain form; a property holding one is skipped
            // by the caller, an array slot holding one becomes null, as with JSON.
            if (kind != "object") return kind == "function" ? null : value;

            // A value type in an object-typed slot may be boxed - { $boxed: true, v: 5, ... } with a
            // constructor and formatting functions around the value. The value is the data.
            if (Script.Get<bool>(value, "$boxed")) return Plain(Script.Get(value, "v"), seen);

            if (seen.has(value)) return seen.get(value);

            // Already clone-safe, and copying them would be pointless or lossy: a Date's toJSON is an
            // ISO string, and a typed array carries no Transpose bookkeeping (it is not a JS array, so
            // System.Array.init never stamps it).
            if (Script.InstanceOf(value, typeof(es5.Date)))
            {
                var date = new es5.Date(((es5.Date)value).getTime());

                seen.set(value, date);

                return date;
            }

            if (es5.ArrayBuffer.isView(value) || Script.InstanceOf(value, typeof(es5.ArrayBuffer))) return value;

            if (es5.Array<object>.isArray(value))
            {
                var source = (es5.Array<object>)value;

                // A fresh JavaScript array, not a C# one - `new object[n]` would come back stamped with
                // the very $type this exists to drop.
                var array = new es5.Array<object>();

                seen.set(value, array);

                for (var i = 0; i < source.length; i++)
                {
                    array.push(Plain(source[i], seen));
                }

                return array;
            }

            // A type that says how it serialises: the runtime's own class instances (their base toJSON
            // collects the fields and properties and leaves the $-bookkeeping behind), List<T> (its
            // items), System.Uri, System.Guid, Monaco's Uri. The result is whatever it chose to hand out,
            // so it is walked in turn - a List's toJSON is a typed array - but, as with JSON.stringify,
            // its own toJSON is not asked again; only its members' are.
            if (honourToJson && Script.TypeOf(Script.Get(value, "toJSON")) == "function")
            {
                var serialised = Script.InvokeMethod(value, "toJSON");

                if (!Script.StrictEquals(serialised, value))
                {
                    var copy = Plain(serialised, seen, false);

                    seen.set(value, copy);

                    return copy;
                }
            }

            return PlainProperties(value, seen);
        }

        /// <summary>
        /// Copies an object's own enumerable properties onto a fresh plain object, skipping members
        /// whose value is a function or undefined - the two JSON leaves out, and the two a worker
        /// cannot take. Keys are copied as they are: a JSON schema's <c>$schema</c> and <c>$ref</c> are
        /// data, so nothing is filtered by name.
        /// </summary>
        private static object PlainProperties(object value, es5.Map<object, object> seen)
        {
            var copy = new PlainObject();

            seen.set(value, copy);

            var keys = Transpose.Core.Object.keys(value);

            for (var i = 0; i < keys.Length; i++)
            {
                var key  = keys[i];
                var item = Script.Get(value, key);
                var kind = Script.TypeOf(item);

                if (kind == "undefined" || kind == "function") continue;

                Script.Set(copy, key, Plain(item, seen));
            }

            return copy;
        }

        /// <summary>
        /// An empty object literal. <c>new PlainObject()</c> is emitted as <c>{}</c> - a fresh object
        /// with no prototype beyond <c>Object.prototype</c>, which is exactly what a copy is built on.
        /// </summary>
        [ObjectLiteral]
        private sealed class PlainObject
        {
        }

        #endregion
    }
}
