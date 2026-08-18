using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 12, Icon = UIcons.PlugConnection)]
    public class BundledServicesSample : IComponent, ISample
    {
        /// <summary>
        /// The document the schema below applies to. A schema is matched against the model's URI, so
        /// giving this page's model a name of its own is what keeps the schema off every other JSON
        /// editor in the gallery - fileMatch "*" would validate them all.
        /// </summary>
        private const string SERVICE_URI = "inmemory://sample/service.json";

        private const string SERVICE_JSON = "{\n  \"name\": \"sample\",\n  \"port\": \"8080\",\n  \"extra\": true\n}\n";

        private readonly IComponent _content;

        public BundledServicesSample()
        {
            ConfigureServices();

            // The document breaks the schema three ways: port is a string and out of range, and
            // `extra` is not allowed.
            var json = MonacoEditor.Editor().SetLanguage("json");

            // Named, not just typed: the schema below is matched against the model's URI. EnsureModel
            // reuses the model on a second visit, because Monaco throws when a URI is claimed twice.
            json.OnRendered(e => e.SetModel(EnsureModel(SERVICE_URI, SERVICE_JSON, "json")));

            var status = TextBlock("waiting for the worker...").Small().Secondary();

            // Markers from a worker arrive asynchronously, long after the edit - so this has to be an
            // event, not a read after typing.
            json.OnMarkersChanged(() =>
            {
                var markers = json.GetMarkers();
                var text    = markers.Length + " marker(s) from the JSON worker";

                if (markers.Length > 0) text += ": " + markers[0].message;

                status.Text = text;
            });

            var ts = MonacoEditor.Editor()
               .SetLanguage("typescript")
               .SetText("// the TypeScript worker, configured with the host's own declarations\nsampleHost.log(42);\nconst n: number = \"not a number\";\n");

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(BundledServicesSample), UIcons.PlugConnection, "Monaco's own workers, configured from C#")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Four languages come with real language services in the Monaco bundle - json, typescript, css and html - and they run on web workers, so they cost nothing on the main thread. MonacoEditor.ConfigureJson(...), ConfigureTypeScript(...), ConfigureCss(...) and ConfigureHtml(...) are how a host sets them up: schemas to validate against, a compiler target, which diagnostics to switch off."),
                        TextBlock("AddTypeScriptLib(...) puts your own .d.ts into that worker, which is what makes a host API complete and type-check inside the editor. None of this is the package's own intelligence - it is Monaco's, wired up.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("All four are safe to call before Monaco has loaded - the calls are queued and applied once it is ready - and they are global rather than per-editor, so scope a schema with fileMatch instead. This page names its model inmemory://sample/service.json and matches on that; a schema registered for \"*\" would validate every JSON document in the app."),
                        TextBlock("A worker's markers arrive well after the edit that caused them, so read them from .OnMarkersChanged(...) rather than after a SetText. .GetMarkers() straight after typing is a race you usually lose.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("JSON, against a schema"),
                        TextBlock("The schema wants a string name and an integer port between 1 and 65535, and allows nothing else. This document gets all three wrong."),
                        json.WS().H(160.px()).MT(8),
                        status.PT(8),
                        SampleSubTitle("TypeScript, with the host's declarations"),
                        TextBlock("sampleHost is declared by a .d.ts handed to the worker, so passing it a number is an error the editor knows about - as is assigning a string to a number."),
                        ts.WS().H(140.px()).MT(8),
                        SampleHint("Fix the port to 8080 without quotes and delete the extra line: the markers clear a moment later, when the worker has run again.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(DiagnosticsSample), typeof(LanguagesAndThemesSample), typeof(CodeEditorSample));
        }

        /// <summary>
        /// Everything the bundled workers need to know. Queued until Monaco loads, so it is safe from a
        /// page constructor - or from Main, if a host would rather configure everything up front.
        /// </summary>
        private static void ConfigureServices()
        {
            MonacoEditor.ConfigureJson(
                schemas: new[]
                {
                    new JsonSchema(
                        "https://tesserae.monaco/sample-schema.json",
                        new[] { "*service.json" },
                        new
                        {
                            type                 = "object",
                            required             = new[] { "name", "port" },
                            additionalProperties = false,
                            properties = new
                            {
                                name = new { type = "string", description = "The service's name." },
                                port = new { type = "integer", minimum = 1, maximum = 65535, description = "The port to listen on." }
                            }
                        })
                },
                allowComments: false);

            MonacoEditor.ConfigureTypeScript(target: ScriptTarget.ES2020, strict: true, lib: new[] { "es2020", "dom" });

            MonacoEditor.AddTypeScriptLib(
                "declare namespace sampleHost { /** Logs a line to the host's console. */ function log(message: string): void; }",
                "file:///sample-host.d.ts");

            MonacoEditor.ConfigureCss();
            MonacoEditor.ConfigureHtml();
        }

        public HTMLElement Render() => _content.Render();
    }
}
