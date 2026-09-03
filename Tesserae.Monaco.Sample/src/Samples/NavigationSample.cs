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

        // The definition provider is a static-shaped callback, so the status line it writes to has to
        // be reachable from one. Rebuilt with the page, like everything else here.
        private static TextBlock _status;

        public NavigationSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SampleCode.Navigable)
               .OccurrencesHighlight("singleFile")

                // Because the provider below has a side effect - the Console case writes documentation
                // into the document - and Monaco would otherwise run it on every modifier-hover.
               .NavigateOnClickOnly();

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

                // A symbol this document does not declare, but that the host still knows something
                // about - a framework type, in a real host. Monaco has nowhere to jump to, so the
                // answer is null and the documentation opens somewhere of ours instead.
                if (context.Word == "Console")
                {
                    ShowExternalDocumentation(editor, context.Word);

                    return Task.FromResult<CodeLocation[]>(null);
                }

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

            _status = status;

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(NavigationSample), UIcons.LocationCrosshairs, "Definitions, references, rename and the outline")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Five delegates cover the navigation Monaco already has UI for: .OnDefinition(...) answers Go to Definition and Ctrl-click, .OnReferences(...) fills the references peek, .OnDocumentHighlights(...) highlights the other occurrences of the symbol under the cursor, .OnRename(...) turns F2 into a set of edits, and .OnDocumentSymbols(...) is what the outline and the breadcrumbs read."),
                        TextBlock("Every one of them is answered here from a single word search over the document. A real host would ask its compiler - the shape of the answer is the same.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Rename returns edits, not a new document: Monaco applies them as one undoable step and keeps every caret and decoration tracked through it. Returning the whole text would throw the user's undo history away."),
                        TextBlock("A definition provider that answers by opening documentation of its own - a framework symbol with no source to jump to - should follow the null it returns with .CloseMessage(). Monaco shows \"No definition found\" whenever a provider yields nothing, and there the message is simply wrong; it appears on the turn after the provider settles, so it has to be closed from a zero-delay timeout rather than inline.").MT(8),
                        TextBlock("These commands are registered by Monaco as keybinding rules rather than editor actions, so getAction cannot see them - .GoToDefinition(), .ShowReferences(), .StartRename() and .ShowOutline() go through trigger instead. That is also why .RunAction(\"editor.action.revealDefinition\") returns false while the command itself works.").MT(8),
                        TextBlock("Go to Definition is a hover gesture as well as a click one: while ctrl/cmd is held Monaco asks the provider on every mouse move, to underline the word and preview its source. A provider that only reads is fine with that; one that costs a round-trip, or that answers by opening something, runs while the user is merely passing over the code. .NavigateOnClickOnly() drops the contribution behind those hover gestures and answers the click itself, through the same command F12 and the context menu use - which is what this page does, since its Console case writes into the document.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Put the cursor on Twice and the other occurrences highlight on their own. The buttons run the same commands the keyboard does: F12, Shift+F12, Ctrl+Shift+O and F2. Console is the out-of-band case - no declaration to jump to, so the provider answers by writing to the line below and takes Monaco's \"No definition found\" message back down."),
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
                            Button("Definition of Console").Tooltip("A symbol with no declaration in this document").OnClick(() =>
                            {
                                editor.SetPosition(new Position { lineNumber = 12, column = 3 });
                                editor.Focus();
                                editor.GoToDefinition();
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

        /// <summary>
        /// Answers a definition request out-of-band, and takes Monaco's "No definition found" message
        /// back down.
        ///
        /// Monaco shows that message whenever a definition provider yields nothing - which is right
        /// when the symbol really is unknown, and wrong here, where the request was answered by
        /// opening documentation elsewhere. It is shown on the turn after the provider settles, so
        /// closing it has to wait for that turn: a zero delay is the earliest that works.
        /// </summary>
        private static void ShowExternalDocumentation(CodeEditor editor, string word)
        {
            _status.Text = $"opened the documentation for {word} - no in-editor declaration to jump to";

            window.setTimeout(_ => editor.CloseMessage(), 0);
        }

        public HTMLElement Render() => _content.Render();
    }
}
