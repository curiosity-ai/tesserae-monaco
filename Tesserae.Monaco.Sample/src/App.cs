using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tesserae;
using Tesserae.Monaco;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// A stub app exercising each Tesserae.Monaco feature so it can be eyeballed in a browser.
    ///
    /// Every language service here is deliberately fake and client-side - a fixed completion list, a
    /// hover that echoes the word under the cursor, a formatter that trims whitespace, a validator
    /// that flags TODOs. The package ships no language intelligence of its own, so this is also what
    /// the wiring looks like from a host app's point of view: swap these delegates for calls to your
    /// own backend and nothing else changes.
    /// </summary>
    internal static class App
    {
        private const string SAMPLE_CODE = @"using System;

public class Greeter
{
    // TODO: make the greeting configurable
    public string Greet(string name)
    {
        return $""Hello, {name}!"";
    }
}";

        private const string SAMPLE_CODE_CHANGED = @"using System;

public class Greeter
{
    private readonly string _greeting;

    public Greeter(string greeting = ""Hello"")
    {
        _greeting = greeting;
    }

    public string Greet(string name)
    {
        return $""{_greeting}, {name}!"";
    }
}";

        private const string SAMPLE_JSON = @"{
  ""name"": ""tesserae.monaco"",
  ""embedded"": true,
  ""languages"": [""csharp"", ""json"", ""typescript""]
}";

        private static void Main()
        {
            document.body.style.overflow = "auto";

            // Language-service configuration has to happen before the first editor mounts. It is queued
            // until Monaco loads, so calling it from here is safe.
            AdvancedSamples.ConfigureServices();

            // One child list, built up front: Children(...) does not append to a stack that has already
            // been given some, so adding the second half in a loop silently rendered nothing.
            var sections = new List<IComponent>
            {
                TextBlock("Tesserae.Monaco").Bold().XLarge(),
                TextBlock("Monaco editor, viewer and diff components for Tesserae. Every sample below is self-contained C#.")
                   .Secondary()
                   .PB(16.px()),

                Section("Code editor", "Editable, C# highlighting, word wrap. Alt+Z (or the context menu) toggles wrapping.", EditorSample()),
                Section("Code viewer", "Read-only: highlighting and selection, no editing affordances.", ViewerSample()),
                Section("Diff viewer", "Two documents compared. Toggle between side-by-side and inline.", DiffSample()),
                Section("Completion and hover", "Ctrl+Space for the suggest list; hover a word for documentation.", CompletionAndHoverSample()),
                Section("Formatting", "Shift+Alt+F formats the document, Ctrl+K Ctrl+F the selection (Ctrl+Shift+I on Linux).", FormattingSample()),
                Section("Diagnostics", "Squiggles from a validator that runs as you type - flags any TODO.", DiagnosticsSample()),
                Section("Custom language", "A tiny Monarch-tokenized language registered from C#.", CustomLanguageSample()),
                Section("Auto height", "Grows to fit its content instead of scrolling.", AutoHeightSample()),
                Section("Inside a modal", "Proves the suggest popup escapes a clipping ancestor.", ModalSample())
            };

            // The second half of the sample - decorations, widgets, the rest of the providers, models,
            // events, commands, the language services and the diff editor's own API.
            sections.AddRange(AdvancedSamples.Sections());

            var content = VStack().WS().P(16.px()).Children(sections.ToArray());

            document.body.appendChild(content.Render());
        }

        private static IComponent Section(string title, string description, IComponent body)
        {
            return VStack().WS().PB(24.px()).Children(
                TextBlock(title).Bold().Medium(),
                TextBlock(description).Small().Secondary().PB(8.px()),
                body
            );
        }

        private static IComponent EditorSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SAMPLE_CODE)
               .WordWrap();

            var status = TextBlock("unchanged").Small().Secondary();

            editor.OnChanged(() => status.Text = $"{editor.Text.Length} characters");

            return VStack().WS().Children(
                editor.WS().H(220.px()),
                HStack().WS().PT(4.px()).Children(
                    Button("Go to line 5").OnClick(() => editor.RevealLine(5).SetPosition(new Position { lineNumber = 5, column = 1 }).Focus()),
                    Button("Read-only").OnClick(() => editor.ReadOnly()),
                    Button("Editable").OnClick(() => editor.ReadOnly(false)),
                    status.PL(8.px())
                )
            );
        }

        private static IComponent ViewerSample()
        {
            var viewer = MonacoEditor.Viewer()
               .SetLanguage("json")
               .SetText(SAMPLE_JSON);

            return viewer.WS().H(140.px());
        }

        private static IComponent DiffSample()
        {
            var diff = MonacoEditor.Diff()
               .SetLanguage("csharp")
               .SetContent(SAMPLE_CODE, SAMPLE_CODE_CHANGED);

            var sideBySide = true;

            var toggle = Button("Show inline");

            toggle.OnClick(() =>
            {
                sideBySide = !sideBySide;
                diff.SideBySide(sideBySide);
                toggle.SetText(sideBySide ? "Show inline" : "Show side by side");
            });

            return VStack().WS().Children(
                diff.WS().H(260.px()),
                HStack().WS().PT(4.px()).Children(
                    toggle,
                    Button("Next change").OnClick(() => diff.GoToNextDifference()),
                    Button("Previous change").OnClick(() => diff.GoToPreviousDifference())
                )
            );
        }

        private static IComponent CompletionAndHoverSample()
        {
            // A fixed list stands in for whatever a real backend would return.
            var symbols = new Dictionary<string, string>
            {
                { "Greet",      "Returns a greeting for the given name." },
                { "Greeter",    "A configurable greeter." },
                { "Console",    "The system console." },
                { "WriteLine",  "Writes a line of text to the console." }
            };

            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// Ctrl+Space here, or hover \"Greet\" below\nvar greeter = new Greeter();\ngreeter.Greet(\"world\");\n");

            editor.OnCompletion(context =>
            {
                var items = new List<CompletionItem>();

                foreach (var symbol in symbols)
                {
                    items.Add(new CompletionItem
                    {
                        label         = symbol.Key,
                        kind          = CompletionItemKind.Method,
                        detail        = symbol.Value,
                        documentation = new MarkdownString { value = symbol.Value, isTrusted = true }
                    });
                }

                return Task.FromResult(items.ToArray());
            });

            editor.OnHover(context =>
            {
                if (context.Word is object && symbols.TryGetValue(context.Word, out var documentation))
                {
                    return Task.FromResult($"**{context.Word}**\n\n{documentation}");
                }

                // Null means "no hover here", which is the common case.
                return Task.FromResult<string>(null);
            });

            return editor.WS().H(160.px());
        }

        private static IComponent FormattingSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("public   class    Messy {   \n\n\n\n    public int X {get;set;}    \n}   \n");

            // A real host would call its server's formatter here; this only proves the pathway.
            editor.OnFormat(code =>
            {
                var lines  = (code ?? "").Replace("\r\n", "\n").Split('\n');
                var result = new List<string>();
                var blanks = 0;

                foreach (var line in lines)
                {
                    var trimmed = line.TrimEnd();

                    if (trimmed.Length == 0)
                    {
                        blanks++;
                        if (blanks > 1) continue;
                    }
                    else
                    {
                        blanks = 0;
                    }

                    result.Add(trimmed);
                }

                return Task.FromResult(string.Join("\n", result.ToArray()));
            });

            return VStack().WS().Children(
                editor.WS().H(160.px()),
                HStack().WS().PT(4.px()).Children(
                    TextBlock("Press Shift+Alt+F, or right-click and pick Format Document.").Small().Secondary()
                )
            );
        }

        private static IComponent DiagnosticsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SAMPLE_CODE);

            // Stands in for a server-side compile; the debounce and staleness handling are the
            // component's job, so a real validator looks exactly like this.
            editor.ValidateAsYouType(code =>
            {
                var diagnostics = new List<CodeDiagnostic>();
                var lines       = (code ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var index = lines[i].IndexOf("TODO", StringComparison.Ordinal);

                    if (index < 0) continue;

                    diagnostics.Add(new CodeDiagnostic(
                        startLine:      i,
                        startCharacter: index,
                        endLine:        i,
                        endCharacter:   index + "TODO".Length,
                        message:        "Unresolved TODO.",
                        severity:       MarkerSeverity.Warning));
                }

                return Task.FromResult<ReadOnlyArray<CodeDiagnostic>>(diagnostics.ToArray());
            });

            return VStack().WS().Children(
                editor.WS().H(200.px()),
                TextBlock("Type TODO on any line - the squiggle appears about a second after you stop typing.").Small().Secondary().PT(4.px())
            );
        }

        private static IComponent CustomLanguageSample()
        {
            var greetLanguage = new LanguageDefinition
            {
                Id         = "greet",
                Aliases    = new[] { "Greet" },
                Extensions = new[] { ".greet" },

                // Monaco's Monarch tokenizer, written as an anonymous object mirroring the JS shape.
                Tokenizer = new
                {
                    tokenizer = new
                    {
                        root = new object[]
                        {
                            new object[] { "#.*$",                     "comment" },
                            new object[] { "\\b(greet|from|to)\\b",    "keyword" },
                            new object[] { "\"[^\"]*\"",               "string"  },
                            new object[] { "\\b\\d+\\b",               "number"  }
                        }
                    }
                },

                Configuration = new
                {
                    comments = new { lineComment = "#" },
                    brackets = new object[] { new[] { "(", ")" } }
                },

                TokenColors = new[]
                {
                    new TokenColor("keyword", "c586c0", "bold"),
                    new TokenColor("string",  "ce9178"),
                    new TokenColor("comment", "6a9955", "italic"),
                    new TokenColor("number",  "b5cea8")
                },

                // Without these, Monaco never triggers completion on ':' - it is not a word character.
                CompletionTriggerCharacters = new[] { ":" }
            };

            var editor = MonacoEditor.Editor()
               .SetLanguage(greetLanguage)
               .SetText("# a tiny made-up language\ngreet \"world\" from 1 to 10\n");

            editor.OnCompletion(context => Task.FromResult(new[]
            {
                new CompletionItem { label = "greet", kind = CompletionItemKind.Keyword },
                new CompletionItem { label = "from",  kind = CompletionItemKind.Keyword },
                new CompletionItem { label = "to",    kind = CompletionItemKind.Keyword }
            }));

            return editor.WS().H(140.px());
        }

        private static IComponent AutoHeightSample()
        {
            var viewer = MonacoEditor.Viewer(autoHeight: true)
               .SetLanguage("json")
               .SetText(SAMPLE_JSON);

            return viewer.WS();
        }

        private static IComponent ModalSample()
        {
            return Button("Open editor in a modal").OnClick(() =>
            {
                var editor = MonacoEditor.Editor()
                   .SetLanguage("csharp")
                   .SetText("// Ctrl+Space - the suggest popup must not be clipped by the modal\nvar x = Gr\n");

                editor.OnCompletion(context => Task.FromResult(new[]
                {
                    new CompletionItem { label = "Greeter", kind = CompletionItemKind.Class },
                    new CompletionItem { label = "Greet",   kind = CompletionItemKind.Method }
                }));

                Modal("Editor in a modal")
                   .W(70.vw())
                   .H(60.vh())
                   .Content(editor.WS().HS())
                   .Show();
            });
        }
    }
}
