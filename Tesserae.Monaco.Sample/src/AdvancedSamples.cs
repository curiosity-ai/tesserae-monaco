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
    /// The second half of the sample: decorations, widgets, the language providers beyond completion
    /// and hover, multi-model hosting, events, commands, the bundled language services, and the diff
    /// editor's own API.
    ///
    /// Same rule as the core samples - every provider here is a deliberately fake, client-side stand-in
    /// for whatever a real host would call. What is worth reading is the wiring, not the fake.
    /// </summary>
    internal static class AdvancedSamples
    {
        internal static IComponent[] Sections()
        {
            return new[]
            {
                Section("Decorations", "Highlighted lines, a glyph-margin icon, an overview-ruler mark and injected text - all through one tracked collection.", DecorationsSample()),
                Section("Widgets and view zones", "A widget anchored in the text, one pinned to a corner, and a band of space the editor reflows around.", WidgetsSample()),
                Section("Signature help, quick fixes, ghost text", "Parameter hints inside the parens, a lightbulb fix for the TODO, and an inline suggestion after 'return '.", IntelliSenseExtrasSample()),
                Section("Navigation and symbols", "Go to definition, find references, rename, and the outline - all from a fake index of the document.", NavigationSample()),
                Section("Hints, lenses, folding, links, colours", "Five providers over one document at once.", AnnotationsSample()),
                Section("Semantic tokens", "Highlighting the Monarch tokenizer cannot express: any ALLCAPS word, coloured by the provider.", SemanticTokensSample()),
                Section("Several documents, one editor", "Switching models keeps each document's caret, scroll and undo history.", MultiModelSample()),
                Section("Events", "Focus, blur, caret and content changes, with the version id that lets a host discard stale work.", EventsSample()),
                Section("Actions, commands and keybindings", "A custom context-menu action, a keybinding, and Monaco's own actions run by id.", ActionsSample()),
                Section("JSON schema validation", "The bundled worker checking a document against a schema - and the markers read back out.", JsonSchemaSample()),
                Section("Diff editor", "Change count from the diff worker, collapsed unchanged regions, move detection, an editable baseline.", DiffExtrasSample()),
                Section("Static colorize", "Highlighted HTML with no editor instance at all - for a snippet nobody will interact with.", ColorizeSample()),
                Section("Remount", "Detaching from the DOM tears the editor down; re-attaching rebuilds it with the text and caret intact.", RemountSample()),
                Section("Typed options", "A few of the ~35 options that used to need the raw Options(...) callback.", OptionsSample())
            };
        }

        private static IComponent Section(string title, string description, IComponent body)
        {
            return VStack().WS().PB(24.px()).Children(
                TextBlock(title).Bold().Medium(),
                TextBlock(description).Small().Secondary().PB(8.px()),
                body
            );
        }

        private const string DECORATED_CODE = @"public class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }

    public bool IsValid()
    {
        return Total > 0;
    }
}";

        // -----------------------------------------------------------------------------------------
        // Decorations
        // -----------------------------------------------------------------------------------------

        private static IComponent DecorationsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(DECORATED_CODE)
               .GlyphMargin();

            var status = TextBlock("no decorations").Small().Secondary();

            // Styles the host supplies: the package ships no CSS, the same way it ships no language
            // intelligence.
            InjectDecorationStyles();

            var applied = false;

            var toggle = Button("Decorate");

            toggle.OnClick(() =>
            {
                applied = !applied;

                if (applied)
                {
                    editor.Decorate(new[]
                    {
                        Decoration.Line(3, "tssm-sample-line"),
                        Decoration.Glyph(6, "tssm-sample-glyph", "This method is covered by tests"),
                        Decoration.Range(Ranges.Of(8, 16, 8, 21), "tssm-sample-inline"),
                        Decoration.RulerMark(Ranges.Line(3), "#e2c08d"),
                        Decoration.InlineNote(new Position { lineNumber = 4, column = 39 }, "  // money", "tssm-sample-note")
                    });

                    status.Text = editor.GetDecorationRanges().Length + " decorations - now type above line 3 and they follow the text";
                }
                else
                {
                    editor.ClearDecorations();
                    status.Text = "no decorations";
                }

                toggle.SetText(applied ? "Clear" : "Decorate");
            });

            return VStack().WS().Children(
                editor.WS().H(200.px()),
                HStack().WS().PT(4.px()).Children(toggle, status.PL(8.px()))
            );
        }

        // The package ships no CSS, the same way it ships no language intelligence - a decoration names a
        // class and the host styles it. These are the sample's own.
        private static void InjectDecorationStyles()
        {
            if (document.getElementById(SAMPLE_STYLE_ID) != null) return;

            var style = document.createElement("style");

            style.id          = SAMPLE_STYLE_ID;
            style.textContent =
                ".tssm-sample-line { background: rgba(226,192,141,0.25); }" +
                ".tssm-sample-glyph { background: #4ec9b0; border-radius: 50%; width: 10px !important; height: 10px !important; margin-left: 4px; }" +
                ".tssm-sample-inline { text-decoration: underline wavy #4fc1ff; }" +
                ".tssm-sample-note { color: #6a9955; font-style: italic; }" +
                ".tssm-sample-widget { background: #4ec9b0; color: #1e1e1e; padding: 1px 6px; border-radius: 3px; font: 11px sans-serif; white-space: nowrap; }" +
                ".tssm-sample-overlay { background: rgba(0,0,0,0.6); color: #fff; padding: 2px 8px; border-radius: 3px; font: 11px sans-serif; }" +
                ".tssm-sample-zone { background: rgba(79,193,255,0.12); color: #4fc1ff; font: 11px sans-serif; padding: 4px 12px; }";

            document.head.appendChild(style);
        }

        private const string SAMPLE_STYLE_ID = "tssm-sample-styles";

        // -----------------------------------------------------------------------------------------
        // Widgets and view zones
        // -----------------------------------------------------------------------------------------

        private static IComponent WidgetsSample()
        {
            InjectDecorationStyles();

            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(DECORATED_CODE);

            var badge = DIV();
            badge.className   = "tssm-sample-widget";
            badge.textContent = "public API";

            var contentWidget = new ContentWidget("sample.badge", badge, new Position { lineNumber = 6, column = 1 })
            {
                AllowEditorOverflow = true
            };

            var corner = DIV();
            corner.className   = "tssm-sample-overlay";
            corner.textContent = "read-only region";

            var zoneBody = DIV();
            zoneBody.className   = "tssm-sample-zone";
            zoneBody.textContent = "3 changes by two authors - a view zone the editor reflows around";

            var zone = new ViewZone(4, zoneBody, heightInLines: 2);

            editor
               .AddContentWidget(contentWidget)
               .AddOverlayWidget(new OverlayWidget("sample.corner", corner))
               .AddViewZone(zone);

            var line = 6;

            return VStack().WS().Children(
                editor.WS().H(220.px()),
                HStack().WS().PT(4.px()).Children(
                    Button("Move the badge down").OnClick(() =>
                    {
                        line = line >= editor.LineCount ? 1 : line + 1;
                        contentWidget.Position = new Position { lineNumber = line, column = 1 };
                        editor.LayoutContentWidget(contentWidget);
                    }),
                    Button("Close the view zone").OnClick(() => editor.RemoveViewZone(zone))
                )
            );
        }

        // -----------------------------------------------------------------------------------------
        // Signature help, code actions, inline completions
        // -----------------------------------------------------------------------------------------

        private static IComponent IntelliSenseExtrasSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// TODO: finish this\nvar total = Sum(1, 2);\n\nint Twice(int value)\n{\n    return \n}\n");

            editor.OnSignatureHelp(context => Task.FromResult(new SignatureHelp
            {
                signatures = new[]
                {
                    new SignatureInformation
                    {
                        label         = "Sum(int first, int second)",
                        documentation = new MarkdownString { value = "Adds two numbers." },
                        parameters    = new[]
                        {
                            new ParameterInformation { label = "int first",  documentation = new MarkdownString { value = "The left operand." } },
                            new ParameterInformation { label = "int second", documentation = new MarkdownString { value = "The right operand." } }
                        }
                    }
                },
                activeSignature = 0,
                activeParameter = CountCommasBefore(context)
            }));

            // A quick fix for exactly the marker the core Diagnostics sample reports.
            editor.ValidateAsYouType(code => Task.FromResult<ReadOnlyArray<CodeDiagnostic>>(FindTodos(code)));

            editor.OnCodeActions(context =>
            {
                var actions = new List<CodeAction>();

                foreach (var marker in context.Markers)
                {
                    if (marker.message != TODO_MESSAGE) continue;

                    actions.Add(new CodeAction
                    {
                        title       = "Remove the TODO comment",
                        isPreferred = true,
                        diagnostics = new[] { marker },
                        edits       = new[]
                        {
                            new TextEdit
                            {
                                range = Ranges.Of(marker.startLineNumber, 1, marker.startLineNumber + 1, 1),
                                text  = ""
                            }
                        }
                    });
                }

                return Task.FromResult(actions.ToArray());
            });

            editor.OnInlineCompletion(context =>
            {
                // Ghost text only where it makes sense - right after `return `.
                if (context.TextUntilPosition.EndsWith("return "))
                {
                    return Task.FromResult(new[] { new InlineCompletion { insertText = "value * 2;" } });
                }

                return Task.FromResult<InlineCompletion[]>(null);
            });

            return VStack().WS().Children(
                editor.WS().H(200.px()),
                HStack().WS().PT(4.px()).Children(
                    Button("Parameter hints").OnClick(() =>
                    {
                        editor.SetPosition(new Position { lineNumber = 2, column = 18 });
                        editor.Focus();
                        editor.ShowParameterHints();
                    }),
                    Button("Quick fix").OnClick(() =>
                    {
                        editor.SetPosition(new Position { lineNumber = 1, column = 6 });
                        editor.Focus();
                        editor.ShowQuickFixes();
                    })
                )
            );
        }

        private const string TODO_MESSAGE = "Unresolved TODO.";

        private static CodeDiagnostic[] FindTodos(string code)
        {
            var diagnostics = new List<CodeDiagnostic>();
            var lines       = (code ?? "").Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var index = lines[i].IndexOf("TODO", StringComparison.Ordinal);

                if (index < 0) continue;

                diagnostics.Add(new CodeDiagnostic(i, index, i, index + 4, TODO_MESSAGE, MarkerSeverity.Warning));
            }

            return diagnostics.ToArray();
        }

        private static int CountCommasBefore(CodeContext context)
        {
            var text  = context.TextUntilPosition ?? "";
            var open  = text.LastIndexOf('(');

            if (open < 0) return 0;

            var commas = 0;

            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == ',') commas++;
            }

            return commas;
        }

        // -----------------------------------------------------------------------------------------
        // Navigation and symbols
        // -----------------------------------------------------------------------------------------

        private const string NAVIGABLE_CODE = @"int Twice(int value)
{
    return value * 2;
}

int Quadruple(int value)
{
    return Twice(Twice(value));
}

var result = Quadruple(3);";

        private static IComponent NavigationSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(NAVIGABLE_CODE)
               .OccurrencesHighlight("singleFile");

            // One fake index over the document stands in for a compiler's symbol table.
            Func<string, string, TextRange[]> findWord = (text, word) =>
            {
                var ranges = new List<TextRange>();
                var lines  = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var from = 0;

                    while (true)
                    {
                        var at = lines[i].IndexOf(word, from, StringComparison.Ordinal);

                        if (at < 0) break;

                        ranges.Add(Ranges.Of(i + 1, at + 1, i + 1, at + 1 + word.Length));
                        from = at + word.Length;
                    }
                }

                return ranges.ToArray();
            };

            editor.OnDefinition(context =>
            {
                if (context.Word is null) return Task.FromResult<CodeLocation[]>(null);

                var hits = findWord(context.Text, context.Word);

                // The declaration is the first occurrence - enough for a stand-in.
                if (hits.Length == 0) return Task.FromResult<CodeLocation[]>(null);

                return Task.FromResult(new[] { new CodeLocation { range = hits[0] } });
            });

            editor.OnReferences(context =>
            {
                if (context.Word is null) return Task.FromResult<CodeLocation[]>(null);

                var hits      = findWord(context.Text, context.Word);
                var locations = new CodeLocation[hits.Length];

                for (var i = 0; i < hits.Length; i++)
                {
                    locations[i] = new CodeLocation { range = hits[i] };
                }

                return Task.FromResult(locations);
            });

            editor.OnDocumentHighlights(context =>
            {
                if (context.Word is null) return Task.FromResult<DocumentHighlight[]>(null);

                var hits       = findWord(context.Text, context.Word);
                var highlights = new DocumentHighlight[hits.Length];

                for (var i = 0; i < hits.Length; i++)
                {
                    highlights[i] = new DocumentHighlight { range = hits[i], kind = DocumentHighlightKind.Text };
                }

                return Task.FromResult(highlights);
            });

            editor.OnRename((context, newName) =>
            {
                if (context.Word is null) return Task.FromResult<TextEdit[]>(null);

                var hits  = findWord(context.Text, context.Word);
                var edits = new TextEdit[hits.Length];

                for (var i = 0; i < hits.Length; i++)
                {
                    edits[i] = new TextEdit { range = hits[i], text = newName };
                }

                return Task.FromResult(edits);
            });

            editor.OnDocumentSymbols(text =>
            {
                var symbols = new List<DocumentSymbol>();
                var lines   = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    // Anything shaped like `int Name(` is a method, as far as this fake is concerned.
                    var paren = lines[i].IndexOf('(');

                    if (paren <= 0 || !lines[i].StartsWith("int ")) continue;

                    var name = lines[i].Substring(4, paren - 4).Trim();

                    symbols.Add(new DocumentSymbol
                    {
                        name           = name,
                        detail         = "int",
                        kind           = SymbolKind.Method,
                        range          = Ranges.Lines(i + 1, Math.Min(i + 4, lines.Length)),
                        selectionRange = Ranges.Of(i + 1, paren - name.Length + 1, i + 1, paren + 1)
                    });
                }

                return Task.FromResult(symbols.ToArray());
            });

            var status = TextBlock("").Small().Secondary();

            return VStack().WS().Children(
                editor.WS().H(220.px()),
                HStack().WS().PT(4.px()).Children(
                    // These go through Trigger, not RunAction: Monaco registers the navigation commands as
                    // keybinding rules rather than editor actions, so getAction does not see them.
                    Button("Go to definition").OnClick(() =>
                    {
                        editor.SetPosition(new Position { lineNumber = 8, column = 13 });
                        editor.Focus();
                        editor.GoToDefinition();
                        status.Text = "jumped to the definition of Twice";
                    }),
                    Button("Find references").OnClick(() =>
                    {
                        editor.SetPosition(new Position { lineNumber = 8, column = 13 });
                        editor.Focus();
                        editor.ShowReferences();
                        status.Text = "opened the references peek";
                    }),
                    Button("Outline").OnClick(() =>
                    {
                        editor.Focus();
                        editor.ShowOutline();
                        status.Text = "opened the outline";
                    }),
                    Button("Rename").OnClick(() =>
                    {
                        editor.SetPosition(new Position { lineNumber = 1, column = 6 });
                        editor.Focus();
                        editor.StartRename();
                        status.Text = "renaming - type a new name";
                    }),
                    status.PL(8.px())
                )
            );
        }

        // -----------------------------------------------------------------------------------------
        // Inlay hints, code lenses, folding, links, colours
        // -----------------------------------------------------------------------------------------

        private const string ANNOTATED_CODE = @"# see https://microsoft.github.io/monaco-editor/ for the real thing
region palette
    accent = #4fc1ff
    warn   = #e2c08d
endregion

region layout
    padding = 12
    margin  = 8
endregion";

        private static IComponent AnnotationsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("ini")
               .SetText(ANNOTATED_CODE)
               .Folding()
               .Links();

            var clicked = TextBlock("").Small().Secondary();

            editor.OnInlayHints((text, range) =>
            {
                var hints = new List<InlayHint>();
                var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var equals = lines[i].IndexOf('=');

                    if (equals < 0) continue;

                    var value = lines[i].Substring(equals + 1).Trim();

                    hints.Add(new InlayHint
                    {
                        position     = new Position { lineNumber = i + 1, column = equals + 1 },
                        label        = value.StartsWith("#") ? ": color " : ": number ",
                        kind         = InlayHintKind.Type,
                        paddingRight = true
                    });
                }

                return Task.FromResult(hints.ToArray());
            });

            editor.OnCodeLenses(
                text =>
                {
                    var lenses = new List<CodeLensItem>();
                    var lines  = (text ?? "").Replace("\r\n", "\n").Split('\n');

                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (!lines[i].StartsWith("region ")) continue;

                        lenses.Add(new CodeLensItem
                        {
                            range   = Ranges.Line(i + 1),
                            title   = "collapse this region",
                            tooltip = "A code lens supplied from C#"
                        });
                    }

                    return Task.FromResult(lenses.ToArray());
                },
                lens =>
                {
                    clicked.Text = "lens clicked on line " + lens.range.startLineNumber;
                    editor.SetPosition(new Position { lineNumber = lens.range.startLineNumber, column = 1 });
                    editor.RunAction("editor.fold");
                });

            editor.OnFoldingRanges(text =>
            {
                var ranges = new List<FoldingRange>();
                var lines  = (text ?? "").Replace("\r\n", "\n").Split('\n');
                var open    = -1;

                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("region ")) open = i + 1;

                    if (lines[i].StartsWith("endregion") && open > 0)
                    {
                        ranges.Add(new FoldingRange { start = open, end = i + 1, kind = FoldingRangeKind.Region });
                        open = -1;
                    }
                }

                return Task.FromResult(ranges.ToArray());
            });

            editor.OnDocumentLinks(text =>
            {
                var links = new List<DocumentLink>();
                var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var at = lines[i].IndexOf("https://", StringComparison.Ordinal);

                    if (at < 0) continue;

                    var end = lines[i].IndexOf(' ', at);

                    if (end < 0) end = lines[i].Length;

                    links.Add(new DocumentLink
                    {
                        range   = Ranges.Of(i + 1, at + 1, i + 1, end + 1),
                        url     = lines[i].Substring(at, end - at),
                        tooltip = "Open the Monaco docs"
                    });
                }

                return Task.FromResult(links.ToArray());
            });

            editor.OnColors(text =>
            {
                var colors = new List<ColorInformation>();
                var lines  = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var at = lines[i].IndexOf('#');

                    // Only a `#rrggbb` literal, and not the comment marker at the start of a line.
                    if (at < 0 || at + 7 > lines[i].Length || i == 0) continue;

                    var hex = lines[i].Substring(at + 1, 6);

                    colors.Add(new ColorInformation
                    {
                        range = Ranges.Of(i + 1, at + 1, i + 1, at + 8),
                        color = new ColorValue
                        {
                            red   = HexPair(hex, 0),
                            green = HexPair(hex, 2),
                            blue  = HexPair(hex, 4),
                            alpha = 1
                        }
                    });
                }

                return Task.FromResult(colors.ToArray());
            });

            return VStack().WS().Children(
                editor.WS().H(240.px()),
                HStack().WS().PT(4.px()).Children(
                    Button("Toggle inlay hints").OnClick(() => editor.RunAction("editor.action.toggleInlayHints")),
                    clicked.PL(8.px())
                )
            );
        }

        private static double HexPair(string hex, int offset)
        {
            var value = Convert.ToInt32(hex.Substring(offset, 2), 16);

            return value / 255.0;
        }

        // -----------------------------------------------------------------------------------------
        // Semantic tokens
        // -----------------------------------------------------------------------------------------

        private static IComponent SemanticTokensSample()
        {
            var legend = new SemanticTokensLegend
            {
                tokenTypes     = new[] { "macro", "variable" },
                tokenModifiers = new string[0]
            };

            // The legend names token types; the colour for one comes from a theme rule, exactly as for a
            // Monarch token. Without this the provider still runs and nothing looks different.
            MonacoEditor.AddTokenColors(new TokenColor("macro", "4fc1ff", "bold"));

            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// ALLCAPS words are coloured by the semantic provider, not the tokenizer\nvar limit = MAX_ITEMS;\nvar name  = DEFAULT_NAME;\n");

            editor.OnSemanticTokens(legend, text =>
            {
                var builder = new SemanticTokenBuilder();
                var lines   = (text ?? "").Replace("\r\n", "\n").Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var line  = lines[i];
                    var start = -1;

                    for (var c = 0; c <= line.Length; c++)
                    {
                        var isUpper = c < line.Length && (char.IsUpper(line[c]) || line[c] == '_');

                        if (isUpper && start < 0) start = c;

                        if (!isUpper && start >= 0)
                        {
                            if (c - start >= 3) builder.Add(i + 1, start + 1, c - start, 0);

                            start = -1;
                        }
                    }
                }

                return Task.FromResult(builder.Build());
            });

            return editor.WS().H(140.px());
        }

        // -----------------------------------------------------------------------------------------
        // Several documents in one editor
        // -----------------------------------------------------------------------------------------

        private static IComponent MultiModelSample()
        {
            var editor = MonacoEditor.Editor();

            CodeModel first  = null;
            CodeModel second = null;

            EditorViewState firstState  = null;
            EditorViewState secondState = null;

            var showingFirst = true;
            var status       = TextBlock("").Small().Secondary();

            // The models cannot exist before Monaco has loaded, so they are created on first render.
            editor.OnRendered(e =>
            {
                first  = MonacoEditor.CreateModel(NAVIGABLE_CODE + "\n\n// first.cs - scroll me, then switch and come back\n" + DECORATED_CODE, "csharp", "inmemory://sample/first.cs");
                second = MonacoEditor.CreateModel("{\n  \"file\": \"second.json\",\n  \"note\": \"a different document, with its own undo history\"\n}\n", "json", "inmemory://sample/second.json");

                e.SetModel(first);
                status.Text = "showing first.cs";
            });

            return VStack().WS().Children(
                editor.WS().H(200.px()),
                HStack().WS().PT(4.px()).Children(
                    Button("Switch document").OnClick(() =>
                    {
                        if (first is null || second is null) return;

                        // Saving before the switch and restoring after is what keeps each document's
                        // caret, scroll offset and folding - Monaco does not do it for you.
                        if (showingFirst)
                        {
                            firstState = editor.SaveViewState();
                            editor.SetModel(second).RestoreViewState(secondState);
                        }
                        else
                        {
                            secondState = editor.SaveViewState();
                            editor.SetModel(first).RestoreViewState(firstState);
                        }

                        showingFirst = !showingFirst;
                        status.Text  = "showing " + (showingFirst ? "first.cs" : "second.json") + " - " + editor.LineCount + " lines";
                    }),
                    Button("Insert a line (undoable)").OnClick(() =>
                    {
                        // ApplyEdits rather than the Text setter: the undo stack and caret survive.
                        editor.ApplyEdits(new[]
                        {
                            new TextEdit { range = Ranges.Of(1, 1, 1, 1), text = "// inserted, and Ctrl+Z still works\n" }
                        });
                    }),
                    Button("Undo").OnClick(() => editor.Undo()),
                    status.PL(8.px())
                )
            );
        }

        // -----------------------------------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------------------------------

        private static IComponent EventsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// click in, type, move the caret, then click away\nvar x = 1;\n");

            var focus   = TextBlock("focus: -").Small().Secondary();
            var caret   = TextBlock("caret: -").Small().Secondary();
            var changes = TextBlock("changes: -").Small().Secondary();

            editor
               .OnFocused(() => focus.Text = "focus: in")
               .OnBlurred(() => focus.Text = "focus: out (a host would save here)")
               .OnCursorPositionChanged(e => caret.Text = "caret: " + e.position.lineNumber + ":" + e.position.column)
               .OnContentChanged(e =>
               {
                   var first = e.changes is object && e.changes.Length > 0 ? e.changes[0].text : "";

                   changes.Text = "changes: v" + e.versionId + ", " + e.changes.Length + " edit(s), last inserted " + Quote(first);
               });

            return VStack().WS().Children(
                editor.WS().H(140.px()),
                VStack().WS().PT(4.px()).Children(focus, caret, changes)
            );
        }

        private static string Quote(string text)
        {
            if (string.IsNullOrEmpty(text)) return "nothing";

            return "\"" + text.Replace("\n", "\\n") + "\"";
        }

        // -----------------------------------------------------------------------------------------
        // Actions, commands, keybindings
        // -----------------------------------------------------------------------------------------

        private static IComponent ActionsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("var a = 1;\nvar b = 2;\nvar c = 3;\n");

            var log = TextBlock("right-click for \"Wrap in braces\", or press Ctrl+Alt+B").Small().Secondary();

            editor.AddAction(
                "sample.wrapInBraces",
                "Wrap in braces",
                surface =>
                {
                    var selection = surface.GetSelection();
                    var range     = Selections.ToRange(selection);

                    if (range is null) return;

                    var text = surface.GetSelectedText();

                    if (string.IsNullOrEmpty(text)) text = surface.Model?.GetLineContent(range.startLineNumber) ?? "";

                    surface.ApplyEdits(new[] { new TextEdit { range = range, text = "{ " + text.Trim() + " }" } });

                    log.Text = "action ran on lines " + range.startLineNumber + "-" + range.endLineNumber;
                },
                new[] { KeyMod.With(KeyMod.CtrlCmd | KeyMod.Alt, KeyCode.KeyB) },
                "1_modification");

            editor.AddCommand(KeyMod.With(KeyMod.CtrlCmd, KeyCode.KeyS), () => log.Text = "Ctrl+S intercepted - the browser's save dialog never opened");

            return VStack().WS().Children(
                editor.WS().H(140.px()),
                HStack().WS().PT(4.px()).Children(
                    Button("Comment line").OnClick(() => { editor.Focus(); editor.ToggleLineComment(); }),
                    Button("Find").OnClick(() => { editor.Focus(); editor.ShowFind(); }),
                    Button("Select all").OnClick(() => { editor.Focus(); editor.SelectAll(); }),
                    Button("Run custom action").OnClick(() =>
                    {
                        editor.Focus();
                        editor.SetSelection(Ranges.Of(2, 1, 2, 11));
                        editor.Trigger("sample.wrapInBraces");
                    })
                ),
                log.PT(4.px())
            );
        }

        // -----------------------------------------------------------------------------------------
        // JSON schema validation, and reading markers back
        // -----------------------------------------------------------------------------------------

        internal static void ConfigureServices()
        {
            // Queued until Monaco loads - which is why this can run from Main.
            MonacoEditor.ConfigureJson(
                schemas: new[]
                {
                    new JsonSchema(
                        "https://tesserae.monaco/sample-schema.json",
                        new[] { "*" },
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

        private static IComponent JsonSchemaSample()
        {
            // A document that breaks the schema three ways: port is a string and out of range, and
            // `extra` is not allowed.
            var editor = MonacoEditor.Editor()
               .SetLanguage("json")
               .SetText("{\n  \"name\": \"sample\",\n  \"port\": \"8080\",\n  \"extra\": true\n}\n");

            var status = TextBlock("waiting for the worker...").Small().Secondary();

            // Markers from a worker arrive asynchronously, long after the edit - so this has to be an
            // event, not a read after typing.
            editor.OnMarkersChanged(() =>
            {
                var markers = editor.GetMarkers();
                var text    = markers.Length + " marker(s) from the JSON worker";

                if (markers.Length > 0) text += ": " + markers[0].message;

                status.Text = text;
            });

            var ts = MonacoEditor.Editor()
               .SetLanguage("typescript")
               .SetText("// the TypeScript worker, configured with the host's own declarations\nsampleHost.log(42);\nconst n: number = \"not a number\";\n");

            return VStack().WS().Children(
                editor.WS().H(140.px()),
                status.PT(4.px()).PB(8.px()),
                ts.WS().H(120.px())
            );
        }

        // -----------------------------------------------------------------------------------------
        // Diff editor
        // -----------------------------------------------------------------------------------------

        private static IComponent DiffExtrasSample()
        {
            var padding = "\n// a long run of identical lines, so there is something to collapse\n";

            for (var i = 0; i < 12; i++)
            {
                padding += "// line " + i + "\n";
            }

            var left  = "int Twice(int v) { return v * 2; }" + padding + "int Half(int v) { return v / 2; }\n";
            var right = "int Half(int v) { return v / 2; }" + padding + "int Twice(int v) { return v * 3; }\n";

            var diff = MonacoEditor.Diff()
               .SetLanguage("csharp")
               .SetContent(left, right)
               .ShowMoves()
               .RenderMarginRevertIcon()
               .OriginalEditable();

            var status   = TextBlock("computing...").Small().Secondary();
            var collapsed = false;

            // The diff is computed on a worker, so the change count is only meaningful from here.
            diff.OnDiffUpdated(() =>
            {
                status.Text = diff.ChangeCount + " changed block(s)" + (diff.IsIdentical ? " - identical" : "");
            });

            diff.OnRendered(d =>
            {
                // Both sides are surfaces, so the baseline can be watched even though it is the left one.
                d.OriginalSide.OnContentChanged(e => status.Text = "baseline edited - v" + e.versionId);
            });

            var collapse = Button("Collapse unchanged");

            collapse.OnClick(() =>
            {
                collapsed = !collapsed;
                diff.HideUnchangedRegions(collapsed);
                collapse.SetText(collapsed ? "Expand unchanged" : "Collapse unchanged");
            });

            return VStack().WS().Children(
                diff.WS().H(240.px()),
                HStack().WS().PT(4.px()).Children(
                    collapse,
                    Button("Next change").OnClick(() => diff.GoToNextDifference()),
                    status.PL(8.px())
                )
            );
        }

        // -----------------------------------------------------------------------------------------
        // Static colorize
        // -----------------------------------------------------------------------------------------

        private static IComponent ColorizeSample()
        {
            var host = DIV();

            // No editor, no model, no view - just highlighted markup. The element fills in when
            // Monaco's colorizer resolves.
            MonacoEditor.WhenLoaded(() =>
            {
                var colorized = MonacoEditor.Colorize(
                    "var greeting = \"hello\";\nvar count = 42; // no editor was created for this",
                    "csharp");

                colorized.style.fontFamily = "'Cascadia Code', Consolas, monospace";
                colorized.style.fontSize   = "12px";

                host.appendChild(colorized);
            });

            return Raw(host);
        }

        // -----------------------------------------------------------------------------------------
        // Remount
        // -----------------------------------------------------------------------------------------

        private static IComponent RemountSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// edit me, put the caret somewhere, then detach and re-attach\nvar survives = true;\n");

            var slot = DIV();
            slot.style.width  = "100%";
            slot.style.height = "140px";

            var rendered = editor.Render();
            slot.appendChild(rendered);

            var status = TextBlock("attached").Small().Secondary();

            return VStack().WS().Children(
                Raw(slot),
                HStack().WS().PT(4.px()).Children(
                    Button("Detach").OnClick(() =>
                    {
                        if (rendered.parentElement is object)
                        {
                            rendered.remove();
                            status.Text = "detached - the editor was torn down";
                        }
                    }),
                    Button("Re-attach").OnClick(() =>
                    {
                        if (rendered.parentElement is null)
                        {
                            slot.appendChild(rendered);
                            status.Text = "re-attached - text and caret restored";
                        }
                    }),
                    status.PL(8.px())
                )
            );
        }

        // -----------------------------------------------------------------------------------------
        // Typed options
        // -----------------------------------------------------------------------------------------

        private static IComponent OptionsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(DECORATED_CODE)
               .FontSize(13)
               .LineNumbers("relative")
               .Rulers(new[] { 60, 100 })
               .RenderWhitespace("boundary")
               .StickyScroll()
               .Padding(8, 8)
               .Placeholder("nothing here yet")
               .CursorBlinking("smooth")
               .SmoothScrolling();

            var minimap = false;

            var toggle = Button("Show minimap");

            toggle.OnClick(() =>
            {
                minimap = !minimap;
                editor.Minimap(minimap);
                toggle.SetText(minimap ? "Hide minimap" : "Show minimap");
            });

            return VStack().WS().Children(
                editor.WS().H(220.px()),
                HStack().WS().PT(4.px()).Children(
                    toggle,
                    Button("Absolute line numbers").OnClick(() => editor.LineNumbers("on")),
                    Button("Glyph margin").OnClick(() => editor.GlyphMargin()),
                    Button("Bigger font").OnClick(() => editor.FontSize(16))
                )
            );
        }
    }
}
