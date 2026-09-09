using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Language services", Order = 1, Icon = UIcons.CommentInfo)]
    public class HoverDocumentationSample : IComponent, ISample
    {
        private readonly IComponent _content;

        // The command a link in the documentation runs. Registered once for the page's lifetime rather
        // than per visit: the id is fixed and the handler is static, so a second registration would only
        // shadow the first.
        private const string SHOW_DOCS_COMMAND = "tss-monaco-sample.showDocs";

        private static bool _commandRegistered;

        // A fixed catalogue stands in for whatever a real backend would answer with.
        private sealed class Symbol
        {
            public string      Signature;
            public string      Summary;
            public Parameter[] Parameters;
            public string      Returns;
        }

        private sealed class Parameter
        {
            public string Name;
            public string Description;

            public Parameter(string name, string description)
            {
                Name        = name;
                Description = description;
            }
        }

        private static readonly Dictionary<string, Symbol> _symbols = new Dictionary<string, Symbol>
        {
            {
                "Greet", new Symbol
                {
                    Signature  = "string Greeter.Greet(string name, bool shout = false)",
                    Summary    = "Returns a greeting for the given name. Pass `shout: true` to have it in capitals - useful for a `Console` that nobody is looking at.",
                    Parameters = new[] { new Parameter("name", "Whom to greet. `null` greets the world."), new Parameter("shout", "Whether to return the greeting in capitals.") },
                    Returns    = "The greeting, never null."
                }
            },
            {
                "Greeter", new Symbol
                {
                    Signature  = "class Greeter",
                    Summary    = "A configurable greeter. Construct one, then call `Greet` as often as you like.",
                    Parameters = new Parameter[0]
                }
            },
            {
                "WriteLine", new Symbol
                {
                    Signature  = "void Console.WriteLine(string value)",
                    Summary    = "Writes a line of text to the console.",
                    Parameters = new[] { new Parameter("value", "The text to write.") }
                }
            }
        };

        public HoverDocumentationSample()
        {
            EnsureCommand();

            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("// Hover \"Greet\" or \"WriteLine\", then click the link at the bottom of the tooltip\nvar greeter = new Greeter();\nConsole.WriteLine(greeter.Greet(\"world\", shout: true));\n");

            editor.OnHover(context =>
            {
                // Null means "no hover here", which is the common case.
                if (context.Word is null || !_symbols.TryGetValue(context.Word, out var symbol)) return Task.FromResult<MarkdownString>(null);

                return Task.FromResult(Documentation(context.Word, symbol));
            });

            // The same markdown serves the suggest widget's details pane: Monaco renders both with the
            // same renderer, so a fenced signature is coloured there too.
            editor.OnCompletion(context =>
            {
                var items = new List<CompletionItem>();

                foreach (var symbol in _symbols)
                {
                    // No detail: the details pane shows it above the documentation, and the signature is already in there.
                    items.Add(new CompletionItem { label = symbol.Key, kind = CompletionItemKind.Method });
                }

                return Task.FromResult(items.ToArray());
            },
            triggerCharacters: new[] { "." });

            editor.OnResolveCompletion((item, token) =>
            {
                if (item.documentation is null && _symbols.TryGetValue(item.label, out var symbol)) item.documentation = Documentation(item.label, symbol);

                return item;
            });

            editor.ShowSuggestDetails();

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(HoverDocumentationSample), UIcons.CommentInfo, "Rich, themed documentation in a tooltip, with a link that does something")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A hover is markdown, and Monaco's renderer does more with it than paragraphs: a fenced code block is coloured by the editor's own tokenizer for its language, so a signature looks like the code it describes; --- draws a separator between the signature and the prose; and $(icon) is a codicon when the MarkdownString sets supportThemeIcons. This is exactly how VS Code's own language servers render their hovers, and it needs no CSS."),
                        TextBlock("A link can run code. MonacoEditor.RegisterCommand(id, handler) registers a command, MonacoEditor.CommandLink(id, argument) is the link target, and Monaco routes a click through its command service - the same path F12 and a code lens take. The argument travels as JSON in the link and comes back to the handler as it was, so a link can name the thing it is about.").MT(8),
                        TextBlock("The tooltip's colours are the theme's. Monaco reads the background, border, link and code-block colours of every widget from theme colour ids (editorHoverWidget.background, textLink.foreground, ...), and the package derives those from the active Tesserae theme in MonacoEditor.TesseraeThemeColors() - so the popups follow the sun/moon toggle in the sidebar without a stylesheet rule anywhere.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Do not inject HTML into the rendered popup. An earlier host of these components sent its documentation as escaped HTML behind a marker, watched the popup with a MutationObserver, wrote innerHTML into it, re-measured the widget through Monaco's private hover controller, and cleared the inline styles it left behind with a second observer - four pieces of machinery to bypass Monaco's renderer, every one of them tied to internal class names that move between releases."),
                        TextBlock("Monaco's own supportHtml is not the answer either. It keeps HTML, but sanitises it against an allowlist that, since 0.56, no longer includes class - only style on a span, for its colours - so HTML cannot be styled from a stylesheet, and a bare <T> in the text vanishes as an unknown tag. Markdown, a fenced code block and the theme cover everything the HTML was for.").MT(8),
                        TextBlock("Mark a MarkdownString trusted only when the host controls its text: command links run on a trusted string and are stripped to their text on an untrusted one. The string overload of OnHover trusts what it is given; the MarkdownString overload lets a host decide. And a command that opens something should call MonacoEditor.HideHovers() first - the popup renders in the shared host above everything else on the page and would sit on top of what the command opened.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Hover Greet: the signature is a coloured code block, the sections below it are markdown, and the last line is a command link - click it and a modal opens with the same documentation while the tooltip goes away. Press Ctrl+Space for the suggest list; the details pane beside it renders the same markdown."),
                        TextBlock("Flip the theme with the sun/moon button in the sidebar and hover again: the tooltip's background, border and link colour follow, because they are theme colours rather than CSS.").MT(8),
                        editor.WS().H(200.px()).MT(8),
                        SampleHint("Greet, Greeter and WriteLine have documentation; anything else has no hover.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CompletionAndHoverSample), typeof(LanguagesAndThemesSample), typeof(NavigationSample), typeof(ModalSample));
        }

        // Signature as a fenced block, a separator, then the prose - the shape every language server's hover has.
        private static MarkdownString Documentation(string name, Symbol symbol)
        {
            var markdown = "```csharp\n" + symbol.Signature + "\n```\n\n---\n\n" + symbol.Summary + "\n\n";

            if (symbol.Parameters.Length > 0)
            {
                markdown += "**Parameters**\n\n";

                foreach (var parameter in symbol.Parameters)
                {
                    markdown += "- `" + parameter.Name + "` - " + parameter.Description + "\n";
                }

                markdown += "\n";
            }

            if (symbol.Returns is object) markdown += "**Returns** " + symbol.Returns + "\n\n";

            markdown += "$(book) [Open the documentation for " + name + "](" + MonacoEditor.CommandLink(SHOW_DOCS_COMMAND, name) + ")";

            return new MarkdownString { value = markdown, isTrusted = true, supportThemeIcons = true };
        }

        private static void EnsureCommand()
        {
            if (_commandRegistered) return;

            _commandRegistered = true;

            MonacoEditor.RegisterCommand<string>(SHOW_DOCS_COMMAND, name =>
            {
                // The popup renders above everything else on the page, so it would cover the modal.
                MonacoEditor.HideHovers();

                if (!_symbols.TryGetValue(name, out var symbol)) return;

                var body = VStack().WS().Children(
                    MonacoEditor.Viewer().SetLanguage("csharp").SetText(symbol.Signature).WS().H(60.px()),
                    TextBlock(symbol.Summary).MT(12));

                foreach (var parameter in symbol.Parameters)
                {
                    body.Add(TextBlock(parameter.Name + " - " + parameter.Description).Small().Secondary().MT(4));
                }

                Modal(name).W(520.px()).Content(body).Show();
            });
        }

        public HTMLElement Render() => _content.Render();
    }
}
