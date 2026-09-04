using System;
using System.Collections.Generic;
using Transpose;
using Transpose.Core;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// The test page for <see cref="MonacoEditor.ToPlainObject"/>: one row per kind of value the
    /// package hands to that method, each checked in the browser against what a worker or a store
    /// needs from it. The page is self-checking - it marks its result container with
    /// <c>data-status</c>, <c>data-passed</c> and <c>data-failed</c>, and each row with
    /// <c>data-case</c> and <c>data-result</c> - so <c>scripts/check-plain-objects.mjs</c> can drive
    /// it headlessly and fail a build on a red row.
    /// </summary>
    [SampleDetails(Group = "Runtime and hosting", Order = 9, Icon = UIcons.Flask)]
    public class PlainObjectsSample : IComponent, ISample
    {
        private const string RESULTS_ID = "plain-objects-results";

        private readonly IComponent _content;
        private readonly HTMLElement _results;
        private readonly TextBlock   _summary;
        private readonly TextBlock   _timing;

        private int  _passed;
        private int  _failed;
        private bool _editorCasesRan;

        public PlainObjectsSample()
        {
            _results = DIV();
            _results.id = RESULTS_ID;
            _results.style.width = "100%";
            _results.setAttribute("data-status", "running");

            _summary = TextBlock("running...").Small().Secondary();
            _timing  = TextBlock("").Small().Secondary();

            // The editor supplies the one value that cannot be built by hand: a real view state from
            // Monaco's saveViewState(). Everything else runs as soon as the page is constructed.
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// The caret is put on this line before the view state is saved,\n// then restored from the plain copy - the row below checks it came back here.\nvar plain = MonacoEditor.ToPlainObject(value);\n");

            editor.OnRendered(e =>
            {
                if (_editorCasesRan) return;

                _editorCasesRan = true;

                foreach (var testCase in EditorCases(e)) Run(testCase);

                Finish();
            });

            foreach (var testCase in StaticCases()) Run(testCase);

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(PlainObjectsSample), UIcons.Flask, "What a worker or a store gets from ToPlainObject")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Everything Monaco forwards to a web worker has to survive postMessage, and so does everything the history store writes to IndexedDB. Values built from C# do not, as they stand: a C# array is stamped with a $type property holding a function, a class instance is a prototype and a set of methods around its data, and a boxed value is a wrapper. structuredClone refuses all three with a DataCloneError that quotes a function body and names nothing useful."),
                        TextBlock("MonacoEditor.ToPlainObject is the normalisation the package applies before a value crosses: a fresh graph of plain objects and arrays with nothing but the data on it. It used to be a JSON.stringify/JSON.parse round trip; it is now a single walk over the graph, which drops the text intermediate and handles what the round trip could not - a shared reference, a cycle, a Date, a typed array.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Use Script.ToArray for a bare C# array crossing to a worker - it is the cheaper fix when the value is just an array. Reach for ToPlainObject when a whole graph crosses: a JSON schema, a worker's createData, a view state going into storage."),
                        TextBlock("Every row here goes through the same four checks - structuredClone accepts the copy, the copy is a plain graph (Object.prototype or Array, no functions, no undefined members), it stringifies to exactly what the old round trip produced (where the two can agree), and it shares no object with its source. Rows with something particular to prove add a check of their own.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("The cases"),
                        TextBlock("One row per kind of value that reaches ToPlainObject, from the package or from a host. The last three need Monaco and run once the editor below has rendered."),
                        Raw(_results).MT(8),
                        _summary.PT(8),
                        _timing.PT(4),
                        SampleSubTitle("The editor the view-state rows use"),
                        editor.WS().H(120.px()).MT(8),
                        SampleHint("Headless: serve the sample and run node scripts/check-plain-objects.mjs <url> - it opens this page, waits for data-status=\"done\" and exits non-zero on any red row.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(BundledServicesSample), typeof(HistoryPersistenceSample), typeof(SemanticTokensSample));
        }

        public HTMLElement Render() => _content.Render();

        #region Cases

        /// <summary>
        /// One thing to normalise and how to judge the result. The generic checks run unless a flag
        /// turns them off; <see cref="Extra"/> is the case's own assertion, returning a message on
        /// failure and null when satisfied.
        /// </summary>
        private sealed class PlainCase
        {
            public string                       Name;
            public string                       What;
            public Func<object>                 Source;
            public bool                         ExpectJson  = true;
            public bool                         ExpectFresh = true;
            public Func<object, object, string> Extra;
        }

        // A host's own type handed to CreateWebWorker as createData: fields, an auto-property, a nested
        // instance, a typed array, and a delegate that must not reach the worker.
        private sealed class WorkerSettings
        {
            public string          Name;
            public int             Retries;
            public string[]        Include;
            public WorkerLimits    Limits;
            public Func<int, int>  Transform = x => x * 2;
            public bool            Enabled { get; set; }
        }

        private sealed class WorkerLimits
        {
            public int    MaxItems;
            public double Timeout;
        }

        private static IEnumerable<PlainCase> StaticCases()
        {
            yield return new PlainCase
            {
                Name   = "null",
                What   = "ToPlainObject(null) is null",
                Source = () => null,
                ExpectFresh = false,
                Extra  = (source, plain) => plain is null ? null : "expected null"
            };

            yield return new PlainCase
            {
                Name   = "primitives",
                What   = "a string, a number, a boolean and an int in an object slot come back as themselves",
                Source = () => "hello",
                ExpectFresh = false,
                Extra  = (source, plain) =>
                {
                    object boxed = 42;

                    if (!Script.StrictEquals(plain, "hello")) return "string changed";
                    if (!Script.StrictEquals(MonacoEditor.ToPlainObject(42.5), 42.5)) return "number changed";
                    if (!Script.StrictEquals(MonacoEditor.ToPlainObject(true), true)) return "boolean changed";
                    if (!Script.StrictEquals(MonacoEditor.ToPlainObject(boxed), 42)) return "int in an object slot did not come back as 42";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "string[] - a schema's fileMatch globs",
                What   = "a C# string array loses its $type stamp and keeps its elements",
                Source = () => new[] { "*service.json", "*.config.json" },
                Extra  = (source, plain) =>
                {
                    if (Script.TypeOf(Script.Get(source, "$type")) != "function") return "premise failed: the C# array carries no $type function";
                    if (!es5.Array<object>.isArray(plain)) return "not an array";
                    if (Script.IsDefined(Script.Get(plain, "$type"))) return "$type survived";
                    if (((es5.Array<object>)plain).length != 2) return "wrong length";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "nested arrays",
                What   = "a jagged int array and a List<string> (whose toJSON is its items) both become plain arrays",
                Source = () => new[] { new[] { 1, 2 }, new[] { 3 } },
                Extra  = (source, plain) =>
                {
                    var list  = new List<string> { "a", "b" };
                    var items = MonacoEditor.ToPlainObject(list);

                    if (!es5.Array<object>.isArray(items)) return "List<string> did not become an array";
                    if (es5.JSON.stringify(items) != "[\"a\",\"b\"]") return "List<string> items wrong: " + es5.JSON.stringify(items);
                    if (Script.IsDefined(Script.Get(items, "$type"))) return "$type survived on the List's items";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "anonymous JSON schema",
                What   = "the Bundled Services schema shape, with $schema, $ref and $defs keys kept as data",
                Source = () =>
                {
                    var schema = new
                    {
                        type                 = "object",
                        required             = new[] { "name", "port" },
                        additionalProperties = false,
                        properties = new
                        {
                            name = new { type = "string", description = "The service's name.", @default = (string)null },
                            port = new { type = "integer", minimum = 1, maximum = 65535 }
                        }
                    };

                    // JSON Schema's own keywords are not C# identifiers, and they are the reason nothing
                    // may be filtered by a leading dollar sign.
                    Script.Set(schema, "$schema", "https://json-schema.org/draft/2020-12/schema");
                    Script.Set(schema.properties, "owner", new { });
                    Script.Set(Script.Get(schema.properties, "owner"), "$ref", "#/$defs/person");
                    Script.Set(schema, "$defs", new { person = new { type = "string" } });

                    return schema;
                },
                Extra = (source, plain) =>
                {
                    if (!Script.StrictEquals(Script.Get(plain, "$schema"), "https://json-schema.org/draft/2020-12/schema")) return "$schema was dropped";
                    if (!Script.StrictEquals(Script.Get(Script.Get(Script.Get(plain, "properties"), "owner"), "$ref"), "#/$defs/person")) return "$ref was dropped";
                    if (!Script.In(Script.Get(Script.Get(plain, "properties"), "name"), "default")) return "a null member was dropped";
                    if (Script.IsDefined(Script.Get(Script.Get(plain, "required"), "$type"))) return "$type survived on the required array";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "[ObjectLiteral] payload",
                What   = "a JsonSchemaEntry - the literal ConfigureJson builds - holding a C# array and an anonymous schema",
                Source = () => new JsonSchemaEntry
                {
                    uri       = "https://tesserae.monaco/sample-schema.json",
                    fileMatch = new[] { "*service.json" },
                    schema    = new { type = "object", required = new[] { "name" } }
                },
                Extra = (source, plain) =>
                {
                    if (Script.IsDefined(Script.Get(Script.Get(plain, "fileMatch"), "$type"))) return "$type survived on fileMatch";
                    if (Script.IsDefined(Script.Get(Script.Get(Script.Get(plain, "schema"), "required"), "$type"))) return "$type survived inside the schema";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "class instance - a worker's createData",
                What   = "a host's own class: fields and the auto-property cross, the delegate does not, the nested array is plain",
                Source = () => new WorkerSettings
                {
                    Name    = "indexer",
                    Retries = 3,
                    Include = new[] { "*.cs", "*.md" },
                    Limits  = new WorkerLimits { MaxItems = 500, Timeout = 2.5 },
                    Enabled = true
                },
                Extra = (source, plain) =>
                {
                    if (!Script.StrictEquals(Script.Get(plain, "Name"), "indexer")) return "Name missing";
                    if (!Script.StrictEquals(Script.Get(plain, "Enabled"), true)) return "the auto-property was dropped";
                    if (Script.In(plain, "Transform")) return "the delegate crossed";
                    if (!Script.StrictEquals(Script.Get(Script.Get(plain, "Limits"), "MaxItems"), 500)) return "nested instance lost a field";
                    if (Script.IsDefined(Script.Get(Script.Get(plain, "Include"), "$type"))) return "$type survived on Include";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "boxed value",
                What   = "a { $boxed: true, v: 7 } wrapper - the runtime's box - is unwrapped to 7",
                Source = () =>
                {
                    var box = new { v = 7 };

                    Script.Set(box, "$boxed", true);
                    Script.Set(box, "valueOf", (Func<int>)(() => 7));

                    return box;
                },
                ExpectJson  = false,
                ExpectFresh = false,
                Extra = (source, plain) => Script.StrictEquals(plain, 7) ? null : "expected 7, got " + es5.JSON.stringify(plain)
            };

            yield return new PlainCase
            {
                Name   = "shared reference and cycle",
                What   = "one array referenced twice stays one array in the copy, and a self-reference stays a self-reference (JSON threw here)",
                Source = () =>
                {
                    var shared = new[] { 1, 2 };
                    var graph  = new { first = shared, second = shared };

                    Script.Set(graph, "self", graph);

                    return graph;
                },
                ExpectJson = false,
                Extra = (source, plain) =>
                {
                    if (!Script.StrictEquals(Script.Get(plain, "first"), Script.Get(plain, "second"))) return "the shared array was copied twice";
                    if (!Script.StrictEquals(Script.Get(plain, "self"), plain)) return "the cycle was not preserved";
                    if (Script.StrictEquals(Script.Get(plain, "first"), Script.Get(source, "first"))) return "the shared array was not copied";
                    if (Script.IsDefined(Script.Get(Script.Get(plain, "first"), "$type"))) return "$type survived";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "Date",
                What   = "a Date is copied as a Date rather than flattened to an ISO string",
                Source = () => new es5.Date(2026, 0, 2, 3, 4, 5),
                ExpectJson = false,
                Extra = (source, plain) =>
                {
                    if (!Script.InstanceOf(plain, typeof(es5.Date))) return "not a Date";
                    if (((es5.Date)plain).getTime() != ((es5.Date)source).getTime()) return "time changed";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "Uint32Array",
                What   = "a typed array - the semantic tokens payload - is already clone-safe and passes through as itself",
                Source = () => new es5.Uint32Array(es5.ArrayLike<uint>.From(new uint[] { 1, 2, 3 })),
                ExpectJson  = false,
                ExpectFresh = false,
                Extra = (source, plain) =>
                {
                    if (!Script.StrictEquals(plain, source)) return "the typed array was replaced";
                    if (!es5.ArrayBuffer.isView(plain)) return "not a buffer view";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "undefined and function members",
                What   = "a member holding undefined or a function is left out, as JSON left it out",
                Source = () =>
                {
                    var value = new { kept = 1 };

                    Script.Set(value, "gone", Script.Undefined);
                    Script.Set(value, "fn", (Action)(() => { }));

                    return value;
                },
                Extra = (source, plain) =>
                {
                    if (Script.In(plain, "gone")) return "an undefined member crossed";
                    if (Script.In(plain, "fn")) return "a function member crossed";
                    if (!Script.StrictEquals(Script.Get(plain, "kept"), 1)) return "kept was lost";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "NaN and infinity",
                What   = "non-finite numbers are kept (JSON turned them into null)",
                Source = () => new { nan = double.NaN, inf = double.PositiveInfinity },
                ExpectJson = false,
                Extra = (source, plain) =>
                {
                    if (!Script.IsNaN(Script.Get(plain, "nan"))) return "NaN was replaced";
                    if (!Script.StrictEquals(Script.Get(plain, "inf"), double.PositiveInfinity)) return "Infinity was replaced";

                    return null;
                }
            };
        }

        private static IEnumerable<PlainCase> EditorCases(CodeEditor editor)
        {
            yield return new PlainCase
            {
                Name   = "Monaco Uri - toJSON is honoured",
                What   = "monaco.Uri serialises itself through toJSON, and the copy is that serialisation",
                Source = () => MonacoUri.parse("file:///src/main.cs"),
                Extra  = (source, plain) =>
                {
                    if (!Script.StrictEquals(Script.Get(plain, "scheme"), "file")) return "scheme missing";
                    if (!Script.StrictEquals(Script.Get(plain, "path"), "/src/main.cs")) return "path missing";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "editor view state",
                What   = "saveViewState() through EditorViewState.ToPlainObject, then FromPlainObject and RestoreViewState put the caret back",
                Source = () =>
                {
                    editor.SetPosition(new Position { lineNumber = 2, column = 7 });

                    return editor.Editor.saveViewState();
                },
                Extra = (source, plain) =>
                {
                    if (!es5.Array<object>.isArray(Script.Get(plain, "cursorState"))) return "no cursorState array";
                    if (Script.TypeOf(Script.Get(plain, "viewState")) != "object") return "no viewState object";

                    var viaComponent = editor.SaveViewState().ToPlainObject();

                    if (es5.JSON.stringify(viaComponent) != es5.JSON.stringify(plain)) return "EditorViewState.ToPlainObject differs from MonacoEditor.ToPlainObject";

                    editor.SetPosition(new Position { lineNumber = 1, column = 1 });
                    editor.RestoreViewState(EditorViewState.FromPlainObject(plain));

                    var position = editor.GetPosition();

                    if (position is null || position.lineNumber != 2 || position.column != 7) return "the caret did not come back to 2:7";

                    return null;
                }
            };

            yield return new PlainCase
            {
                Name   = "history records",
                What   = "EditorHistoryEntry.ToPlainObject and EditorPlace.ToPlainObject clone as IndexedDB needs, and read back through FromPlainObject",
                Source = () => new EditorHistoryEntry
                {
                    Scope      = "gallery:tests",
                    DocumentId = "samples/plain.cs",
                    Timestamp  = 1_700_000_000_000,
                    Text       = editor.Text,
                    ViewState  = editor.SaveViewState().ToPlainObject(),
                    Language   = "csharp",
                    VersionId  = 3,
                    Label      = "checkpoint",
                    Origin     = EditorHistoryOrigin.Local,
                    Author     = "u-7781",
                    Id         = "r-1"
                }.ToPlainObject(),
                Extra = (source, plain) =>
                {
                    // The record itself is what the store writes - it has to clone as it stands.
                    var error = CloneError(source);

                    if (error is object) return "the entry record does not clone: " + error;

                    var entry = EditorHistoryEntry.FromPlainObject(source);

                    if (entry.Scope != "gallery:tests" || entry.DocumentId != "samples/plain.cs" || entry.Origin != EditorHistoryOrigin.Local || entry.Author != "u-7781" || entry.VersionId != 3) return "the entry did not read back";
                    if (!Script.StrictEquals(Script.Get(source, "docKey"), "13:gallery:tests" + "samples/plain.cs")) return "docKey is not length-prefixed as documented: " + Script.Get(source, "docKey");

                    var place = new EditorPlace
                    {
                        Scope      = "gallery:tests",
                        DocumentId = "samples/plain.cs",
                        Timestamp  = 1_700_000_000_001,
                        ViewState  = editor.SaveViewState().ToPlainObject()
                    }.ToPlainObject();

                    error = CloneError(place);

                    if (error is object) return "the place record does not clone: " + error;
                    if (FindNotPlain(place, "place") is object) return "the place record is not plain: " + FindNotPlain(place, "place");

                    var read = EditorPlace.FromPlainObject(place);

                    if (read.Scope != "gallery:tests" || read.Timestamp != 1_700_000_000_001) return "the place did not read back";

                    return null;
                }
            };
        }

        #endregion

        #region Running

        private void Run(PlainCase testCase)
        {
            string failure = null;
            object source  = null;
            object plain   = null;

            try
            {
                source = testCase.Source();
                plain  = MonacoEditor.ToPlainObject(source);

                failure = CloneError(plain);

                if (failure is object) failure = "structuredClone refused the copy: " + failure;

                if (failure is null) failure = FindNotPlain(plain, "copy");

                if (failure is null && testCase.ExpectJson)
                {
                    var expected = es5.JSON.stringify(source);
                    var actual   = es5.JSON.stringify(plain);

                    if (expected != actual) failure = "differs from the JSON round trip: expected " + expected + " got " + actual;
                }

                if (failure is null && testCase.ExpectFresh && SharesAnObject(source, plain)) failure = "the copy shares an object with its source";

                if (failure is null && testCase.Extra is object) failure = testCase.Extra(source, plain);
            }
            catch (Exception exception)
            {
                failure = "threw: " + exception.Message;
            }

            if (failure is null) _passed++; else _failed++;

            var row = HStack().WS().AlignItemsCenter().Gap(8.px()).PT(4).PB(4).Children(
                Icon(failure is null ? UIcons.CheckCircle : UIcons.CrossCircle).Foreground(failure is null ? Theme.Success.Background : Theme.Danger.Background),
                VStack().Children(
                    TextBlock(testCase.Name).SemiBold(),
                    TextBlock(failure ?? testCase.What).Small().Secondary().Foreground(failure is null ? "" : Theme.Danger.Background)));

            var element = row.Render();

            element.setAttribute("data-case", testCase.Name);
            element.setAttribute("data-result", failure is null ? "pass" : "fail");

            if (failure is object) element.setAttribute("data-failure", failure);

            _results.appendChild(element);

            _summary.Text = _passed + " passed, " + _failed + " failed" + (_editorCasesRan ? "" : " - waiting for the editor");
        }

        private void Finish()
        {
            _results.setAttribute("data-passed", _passed.ToString());
            _results.setAttribute("data-failed", _failed.ToString());
            _results.setAttribute("data-status", "done");

            _summary.Text = _passed + " passed, " + _failed + " failed";

            _timing.Text = Timing();
        }

        /// <summary>
        /// Not a check, a measurement: the structural copy against the JSON round trip it replaced, on
        /// a graph of the Bundled Services schema repeated. Reported, not asserted - timings on a
        /// shared machine are not a test.
        /// </summary>
        private static string Timing()
        {
            var schemas = new object[40];

            for (var i = 0; i < schemas.Length; i++)
            {
                schemas[i] = new
                {
                    type                 = "object",
                    required             = new[] { "name", "port" },
                    additionalProperties = false,
                    properties = new
                    {
                        name = new { type = "string", description = "The service's name." },
                        port = new { type = "integer", minimum = 1, maximum = 65535, description = "The port to listen on." }
                    }
                };
            }

            const int ROUNDS = 200;

            // Warm both paths before timing either.
            for (var i = 0; i < 20; i++) { es5.JSON.parse(es5.JSON.stringify(schemas)); MonacoEditor.ToPlainObject(schemas); }

            var start = es5.Date.now();

            for (var i = 0; i < ROUNDS; i++) es5.JSON.parse(es5.JSON.stringify(schemas));

            var json = es5.Date.now() - start;

            start = es5.Date.now();

            for (var i = 0; i < ROUNDS; i++) MonacoEditor.ToPlainObject(schemas);

            var walk = es5.Date.now() - start;

            return "Timing, " + ROUNDS + " copies of 40 schemas: JSON round trip " + json + " ms, structural copy " + walk + " ms.";
        }

        #endregion

        #region Checks

        /// <summary>The message structuredClone throws for <paramref name="value"/>, or null when it clones.</summary>
        private static string CloneError(object value)
        {
            try
            {
                window.structuredClone(value);

                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        [ObjectLiteral]
        private sealed class Bag
        {
        }

        private static readonly object PLAIN_PROTOTYPE = Transpose.Core.Object.getPrototypeOf(new Bag());

        /// <summary>
        /// Walks <paramref name="value"/> and names the first thing in it that is not plain: a function,
        /// an undefined member, an object with a prototype other than Object.prototype, or an array
        /// carrying a property beyond its elements. Dates and typed arrays are allowed through, since
        /// the copy deliberately keeps them. Returns null for a plain graph.
        /// </summary>
        private static string FindNotPlain(object value, string path)
        {
            return FindNotPlain(value, path, new es5.Map<object, bool>());
        }

        private static string FindNotPlain(object value, string path, es5.Map<object, bool> visited)
        {
            if (value is null) return null;

            var kind = Script.TypeOf(value);

            if (kind == "function") return path + " is a function";
            if (kind != "object") return null;
            if (visited.has(value)) return null;

            visited.set(value, true);

            if (Script.InstanceOf(value, typeof(es5.Date)) || es5.ArrayBuffer.isView(value)) return null;

            var names = Transpose.Core.Object.getOwnPropertyNames(value);

            if (es5.Array<object>.isArray(value))
            {
                foreach (var name in names)
                {
                    if (name == "length") continue;
                    if (!int.TryParse(name, out _)) return path + " is an array carrying a '" + name + "' property";

                    var error = FindNotPlain(Script.Get(value, name), path + "[" + name + "]", visited);

                    if (error is object) return error;
                }

                return null;
            }

            if (!Script.StrictEquals(Transpose.Core.Object.getPrototypeOf(value), PLAIN_PROTOTYPE)) return path + " has a prototype other than Object.prototype";

            foreach (var name in names)
            {
                var member = Script.Get(value, name);

                if (Script.IsUndefined(member)) return path + "." + name + " is undefined";

                var error = FindNotPlain(member, path + "." + name, visited);

                if (error is object) return error;
            }

            return null;
        }

        /// <summary>True when any object or array reachable from <paramref name="plain"/> is one reachable from <paramref name="source"/>.</summary>
        private static bool SharesAnObject(object source, object plain)
        {
            var seen = new es5.Map<object, bool>();

            Collect(source, seen);

            return Reaches(plain, seen, new es5.Map<object, bool>());
        }

        private static void Collect(object value, es5.Map<object, bool> into)
        {
            if (value is null || Script.TypeOf(value) != "object" || into.has(value)) return;

            into.set(value, true);

            foreach (var name in Transpose.Core.Object.getOwnPropertyNames(value)) Collect(Script.Get(value, name), into);
        }

        private static bool Reaches(object value, es5.Map<object, bool> targets, es5.Map<object, bool> visited)
        {
            if (value is null || Script.TypeOf(value) != "object" || visited.has(value)) return false;
            if (targets.has(value)) return true;

            visited.set(value, true);

            foreach (var name in Transpose.Core.Object.getOwnPropertyNames(value))
            {
                if (Reaches(Script.Get(value, name), targets, visited)) return true;
            }

            return false;
        }

        #endregion
    }
}
