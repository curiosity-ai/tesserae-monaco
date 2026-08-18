using System;
using System.Collections.Generic;
using Transpose;
using Tesserae;

namespace Tesserae.Monaco
{
    /// <summary>Which ECMAScript version the TypeScript service targets, matching Monaco's <c>ScriptTarget</c>.</summary>
    [Enum(Emit.Value)]
    public enum ScriptTarget
    {
        ES3    = 0,
        ES5    = 1,
        ES2015 = 2,
        ES2016 = 3,
        ES2017 = 4,
        ES2018 = 5,
        ES2019 = 6,
        ES2020 = 7,
        ESNext = 99,
        JSON   = 100
    }

    /// <summary>The module system the TypeScript service assumes, matching Monaco's <c>ModuleKind</c>.</summary>
    [Enum(Emit.Value)]
    public enum ModuleKind
    {
        None     = 0,
        CommonJS = 1,
        AMD      = 2,
        UMD      = 3,
        System   = 4,
        ES2015   = 5,
        ESNext   = 99
    }

    /// <summary>How JSX is emitted, matching Monaco's <c>JsxEmit</c>.</summary>
    [Enum(Emit.Value)]
    public enum JsxEmit
    {
        None        = 0,
        Preserve    = 1,
        React       = 2,
        ReactNative = 3,
        ReactJSX    = 4,
        ReactJSXDev = 5
    }

    /// <summary>A JSON Schema to validate documents against, for <see cref="MonacoEditor.ConfigureJson"/>.</summary>
    public sealed class JsonSchema
    {
        /// <summary>
        /// The schema's own id. Also what a document's <c>$schema</c> has to name to opt in, and what
        /// Monaco fetches if <see cref="Schema"/> is left null and requests are enabled.
        /// </summary>
        public string Uri { get; set; }

        /// <summary>
        /// Which documents this applies to, as globs against the model URI - e.g.
        /// <c>new[] { "*.config.json" }</c>, or <c>new[] { "*" }</c> for every JSON document. Without
        /// this the schema is only used when a document names it in <c>$schema</c>.
        /// </summary>
        public string[] FileMatch { get; set; }

        /// <summary>
        /// The schema itself, as an anonymous object mirroring the JSON. Supply it inline rather than
        /// letting Monaco fetch <see cref="Uri"/> when the schema is known to the app.
        /// </summary>
        public object Schema { get; set; }

        public JsonSchema() { }

        public JsonSchema(string uri, string[] fileMatch, object schema)
        {
            Uri       = uri;
            FileMatch = fileMatch;
            Schema    = schema;
        }
    }

    public static partial class MonacoEditor
    {
        /// <summary>
        /// Configures the bundled <b>JSON</b> language service: validation, and the schemas to validate
        /// against.
        ///
        /// The workers ship with the package but nothing configures them by default, so a JSON document is
        /// only checked for syntax. Attaching a schema is what turns that into real validation, with
        /// completion and hover documentation for the properties it describes.
        ///
        /// Schemas are matched by model URI, so a document that should be validated needs a model with one
        /// - see <see cref="CreateModel"/> - unless the schema's <c>FileMatch</c> is <c>"*"</c>. Safe to
        /// call before Monaco has loaded.
        /// </summary>
        /// <param name="schemas">The schemas to register. Replaces any previously registered.</param>
        /// <param name="validate">Whether to validate at all.</param>
        /// <param name="allowComments">Accept comments (JSONC) instead of flagging them.</param>
        /// <param name="allowTrailingCommas">Accept trailing commas instead of flagging them.</param>
        /// <param name="enableSchemaRequest">Let Monaco fetch a schema over HTTP when only its URI is known.</param>
        public static void ConfigureJson(
            JsonSchema[] schemas             = null,
            bool         validate            = true,
            bool         allowComments       = false,
            bool         allowTrailingCommas = false,
            bool         enableSchemaRequest = false)
        {
            WhenLoaded(() =>
            {
                var entries = new List<JsonSchemaEntry>();

                if (schemas is object)
                {
                    foreach (var schema in schemas)
                    {
                        if (schema is null || string.IsNullOrWhiteSpace(schema.Uri)) continue;

                        // Both the schema body and the glob list are forwarded to the worker, so both have
                        // to survive postMessage - see ToPlainObject.
                        entries.Add(new JsonSchemaEntry
                        {
                            uri       = schema.Uri,
                            fileMatch = (string[])ToPlainObject(schema.FileMatch),
                            schema    = ToPlainObject(schema.Schema)
                        });
                    }
                }

                MonacoApi.languages.json.jsonDefaults.setDiagnosticsOptions(new JsonDiagnosticsOptions
                {
                    validate            = validate,
                    allowComments       = allowComments,
                    comments            = allowComments       ? "ignore" : "error",
                    trailingCommas      = allowTrailingCommas ? "ignore" : "error",
                    schemaValidation    = "error",
                    enableSchemaRequest = enableSchemaRequest,
                    schemas             = Script.ToArray(entries.ToArray())
                });
            });
        }

        /// <summary>
        /// Configures the bundled <b>TypeScript</b> and <b>JavaScript</b> language service: the compiler
        /// options it type-checks against, and which classes of diagnostic to report.
        ///
        /// <paramref name="eagerModelSync"/> is the setting most likely to be the difference between this
        /// working and not: with it off, the worker only sees models the service was explicitly told
        /// about, so a plain editor gets syntax errors but no type errors. It defaults to on here.
        /// </summary>
        /// <param name="target">The ECMAScript level to check against.</param>
        /// <param name="module">The module system to assume.</param>
        /// <param name="strict">Turn on the strict family of checks.</param>
        /// <param name="jsx">How to treat JSX syntax.</param>
        /// <param name="lib">Which built-in type libraries to load, e.g. <c>new[] { "es2020", "dom" }</c>.</param>
        /// <param name="noSemanticValidation">Report syntax errors only - useful for a snippet that isn't a whole program.</param>
        /// <param name="noSyntaxValidation">Suppress syntax errors.</param>
        /// <param name="diagnosticCodesToIgnore">TypeScript error codes to swallow, e.g. 2304 for "cannot find name".</param>
        /// <param name="eagerModelSync">Let the worker see every model, not just ones handed to it.</param>
        /// <param name="alsoJavaScript">Apply the same settings to the JavaScript service.</param>
        public static void ConfigureTypeScript(
            ScriptTarget target                  = ScriptTarget.ES2020,
            ModuleKind   module                  = ModuleKind.ESNext,
            bool         strict                  = false,
            JsxEmit      jsx                     = JsxEmit.None,
            string[]     lib                     = null,
            bool         noSemanticValidation    = false,
            bool         noSyntaxValidation      = false,
            int[]        diagnosticCodesToIgnore = null,
            bool         eagerModelSync          = true,
            bool         alsoJavaScript          = true)
        {
            WhenLoaded(() =>
            {
                var compilerOptions = new TypeScriptCompilerOptions
                {
                    target = target,
                    module = module,
                    strict = strict,

                    // On, so a snippet that is not a whole program still type-checks.
                    allowNonTsExtensions = true,

                    // NodeJs, so an import resolves the way a host's own build would resolve it.
                    moduleResolution = MODULE_RESOLUTION_NODE
                };

                if (jsx != JsxEmit.None)        compilerOptions.jsx = jsx;
                if (lib is object && lib.Length > 0) compilerOptions.lib = Script.ToArray(lib);

                var diagnosticsOptions = new TypeScriptDiagnosticsOptions
                {
                    noSemanticValidation    = noSemanticValidation,
                    noSyntaxValidation      = noSyntaxValidation,
                    diagnosticCodesToIgnore = Script.ToArray(diagnosticCodesToIgnore ?? new int[0])
                };

                Apply(MonacoApi.languages.typescript.typescriptDefaults);

                if (alsoJavaScript) Apply(MonacoApi.languages.typescript.javascriptDefaults);

                void Apply(ITypeScriptDefaults defaults)
                {
                    defaults.setCompilerOptions(compilerOptions);
                    defaults.setDiagnosticsOptions(diagnosticsOptions);
                    defaults.setEagerModelSync(eagerModelSync);
                }
            });
        }

        // monaco.languages.typescript.ModuleResolutionKind.NodeJs.
        private const int MODULE_RESOLUTION_NODE = 2;

        /// <summary>
        /// Adds declarations the TypeScript service should know about - the app's own API surface, so a
        /// script the user writes in the editor gets completion and type-checking against it.
        ///
        /// <paramref name="filePath"/> matters: give it a <c>file:///</c> path and the declarations can be
        /// imported by that path, rather than only being ambient.
        /// </summary>
        public static void AddTypeScriptLib(string declarations, string filePath = null, bool alsoJavaScript = true)
        {
            if (string.IsNullOrWhiteSpace(declarations)) return;

            WhenLoaded(() =>
            {
                MonacoApi.languages.typescript.typescriptDefaults.addExtraLib(declarations, filePath);

                if (alsoJavaScript) MonacoApi.languages.typescript.javascriptDefaults.addExtraLib(declarations, filePath);
            });
        }

        /// <summary>
        /// Configures the bundled <b>CSS</b>, SCSS and LESS language service: whether to validate, and
        /// which lint rules to report. Severities are Monaco's own strings - <c>"ignore"</c>,
        /// <c>"warning"</c> or <c>"error"</c> - keyed by rule name, e.g. <c>"emptyRules"</c>.
        /// </summary>
        public static void ConfigureCss(bool validate = true, Dictionary<string, string> lint = null)
        {
            WhenLoaded(() =>
            {
                var lintOptions = new CssLintOptions();

                if (lint is object)
                {
                    foreach (var rule in lint)
                    {
                        if (!string.IsNullOrWhiteSpace(rule.Key)) lintOptions.Set(rule.Key, rule.Value);
                    }
                }

                var options = new CssOptions { validate = validate, lint = lintOptions };

                MonacoApi.languages.css.cssDefaults.setOptions(options);
                MonacoApi.languages.css.scssDefaults.setOptions(options);
                MonacoApi.languages.css.lessDefaults.setOptions(options);
            });
        }

        /// <summary>
        /// Configures the bundled <b>HTML</b> language service: the formatter's settings, and whether to
        /// suggest HTML5 tags.
        /// </summary>
        public static void ConfigureHtml(
            int  tabSize        = 4,
            bool insertSpaces   = true,
            int  wrapLineLength = 120,
            bool suggestHtml5   = true)
        {
            WhenLoaded(() =>
            {
                var options = new HtmlOptions
                {
                    format = new HtmlFormatOptions
                    {
                        tabSize            = tabSize,
                        insertSpaces       = insertSpaces,
                        wrapLineLength     = wrapLineLength,
                        unformatted        = "default, a, abbr, span, code",
                        contentUnformatted = "pre",
                        indentInnerHtml    = false,
                        preserveNewLines   = true,
                        wrapAttributes     = "auto"
                    },
                    suggest = new HtmlSuggestOptions { html5 = suggestHtml5 }
                };

                MonacoApi.languages.html.htmlDefaults.setOptions(options);
                MonacoApi.languages.html.handlebarDefaults.setOptions(options);
                MonacoApi.languages.html.razorDefaults.setOptions(options);
            });
        }
    }
}
