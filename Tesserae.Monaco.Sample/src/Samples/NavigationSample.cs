using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 6, Icon = UIcons.LocationCrosshairs)]
    public class NavigationSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public NavigationSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SampleCode.Navigable)
               .OccurrencesHighlight("singleFile");

            // One fake index over the document stands in for a compiler's symbol table: every provider
            // below answers from the same word search.
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

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(NavigationSample), UIcons.LocationCrosshairs, "Definitions, references, rename and the outline")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Five delegates cover the navigation Monaco already has UI for: .OnDefinition(...) answers Go to Definition and Ctrl-click, .OnReferences(...) fills the references peek, .OnDocumentHighlights(...) highlights the other occurrences of the symbol under the cursor, .OnRename(...) turns F2 into a set of edits, and .OnDocumentSymbols(...) is what the outline and the breadcrumbs read."),
                        TextBlock("Every one of them is answered here from a single word search over the document. A real host would ask its compiler - the shape of the answer is the same.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Rename returns edits, not a new document: Monaco applies them as one undoable step and keeps every caret and decoration tracked through it. Returning the whole text would throw the user's undo history away."),
                        TextBlock("These commands are registered by Monaco as keybinding rules rather than editor actions, so getAction cannot see them - .GoToDefinition(), .ShowReferences(), .StartRename() and .ShowOutline() go through trigger instead. That is also why .RunAction(\"editor.action.revealDefinition\") returns false while the command itself works.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Put the cursor on Twice and the other occurrences highlight on their own. The buttons run the same commands the keyboard does: F12, Shift+F12, Ctrl+Shift+O and F2."),
                        editor.WS().H(240.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).PT(8).AlignItemsCenter().Children(
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
                            status.PL(8.px())),
                        SampleHint("Rename edits every occurrence at once, and Ctrl+Z undoes all of them together.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CompletionAndHoverSample), typeof(ActionsAndCommandsSample), typeof(InlayHintsAndLensesSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
