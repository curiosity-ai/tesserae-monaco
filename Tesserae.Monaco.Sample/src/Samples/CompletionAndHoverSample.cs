using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 0, Icon = UIcons.MagicWand)]
    public class CompletionAndHoverSample : IComponent, ISample
    {
        private readonly IComponent _content;

        // A fixed list stands in for whatever a real backend would return.
        private static readonly Dictionary<string, string> _symbols = new Dictionary<string, string>
        {
            { "Greet",     "Returns a greeting for the given name." },
            { "Greeter",   "A configurable greeter." },
            { "Console",   "The system console." },
            { "WriteLine", "Writes a line of text to the console." }
        };

        public CompletionAndHoverSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// Ctrl+Space here, or hover \"Greet\" below\nvar greeter = new Greeter();\ngreeter.Greet(\"world\");\n");

            editor.OnCompletion(context =>
            {
                var items = new List<CompletionItem>();

                foreach (var symbol in _symbols)
                {
                    // No documentation here on purpose: it is filled in below, once the user actually
                    // highlights the item.
                    items.Add(new CompletionItem
                    {
                        label  = symbol.Key,
                        kind   = CompletionItemKind.Method,
                        detail = symbol.Value
                    });
                }

                return Task.FromResult(items.ToArray());
            });

            // Monaco resolves only the highlighted item, so this is where documentation that costs a
            // round-trip belongs. The overload taking a Task is the one a server-backed host wants;
            // Monaco fills the details pane in when it settles.
            editor.OnResolveCompletion(async (item, token) =>
            {
                if (item.documentation is object) return item;

                await Task.Delay(150);

                if (_symbols.TryGetValue(item.label, out var documentation))
                {
                    item.documentation = new MarkdownString { value = $"**{item.label}**\n\n{documentation}", isTrusted = true };
                }

                return item;
            });

            // The documentation pane is collapsed until the user asks for it; these completions are
            // worth showing it for.
            editor.ShowSuggestDetails();

            editor.OnHover(context =>
            {
                if (context.Word is object && _symbols.TryGetValue(context.Word, out var documentation))
                {
                    return Task.FromResult($"**{context.Word}**\n\n{documentation}");
                }

                // Null means "no hover here", which is the common case.
                return Task.FromResult<string>(null);
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(CompletionAndHoverSample), UIcons.MagicWand, "Suggestions and documentation, supplied by the host")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("The package ships no language intelligence of its own. .OnCompletion(...) and .OnHover(...) take async delegates, and whatever they return is what Monaco shows - so the same components serve a fixed keyword list, a client-side index, or a server-backed compiler, with no other change."),
                        TextBlock("Each delegate is handed a CodeContext: the document, the caret position and the word under it. Returning null from a hover means \"nothing to say here\", which is the common case and costs nothing.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Providers are registered with Monaco per language, not per editor, so each callback is gated on its own model - two csharp editors on one page never answer each other's requests, and every registration is disposed when its editor unmounts."),
                        TextBlock("Anything expensive belongs in .OnResolveCompletion(...) rather than in the list: Monaco calls it for the highlighted item only, so a hundred suggestions cost one lookup instead of a hundred. It takes a Task, which is the point - the round-trip is what made deferring it worth doing. .ShowSuggestDetails() then opens the pane it fills, which is otherwise collapsed until the user presses Ctrl+Space a second time.").MT(8),
                        TextBlock("Hover honours Monaco's cancellation token: Monaco cancels a hover as soon as the pointer moves, and a late answer would flash a stale tooltip over the wrong symbol. Keep your own provider fast, and let a slow backend be cancelled rather than queued.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Press Ctrl+Space for the suggest list and accept an item - it inserts. The documentation beside it arrives a moment later, from the resolve callback. Hover the word Greet for its own documentation."),
                        editor.WS().H(200.px()).MT(8),
                        SampleHint("The suggestions are Greet, Greeter, Console and WriteLine; anything else has no hover.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CustomLanguageSample), typeof(FormattingSample), typeof(DiagnosticsSample), typeof(ModalSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
