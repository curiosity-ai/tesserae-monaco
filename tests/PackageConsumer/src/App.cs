using System.Threading.Tasks;
using Tesserae;
using Tesserae.Monaco;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace PackageConsumer
{
    // A plain package consumer: PackageReference only, nothing from the repo. One editor with its own
    // completion and hover, a diff, a multi-editor with views, and persisted history with its modal -
    // enough of the surface that a chunked build has to fetch the package's deferred chunks to run it.
    // scripts/test-package-consumer.mjs drives it by the ids below.
    public static class App
    {
        public static void Main()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("public class Greeter\n{\n    public void Greet() { }\n}\n")
               .PersistHistory("consumer", "greeter.cs")
               .OnCompletion(ctx => Task.FromResult(new[]
                {
                    new CompletionItem { label = "consumerOne", kind = CompletionItemKind.Method },
                    new CompletionItem { label = "consumerTwo", kind = CompletionItemKind.Method },
                }))
               .OnHover(ctx => Task.FromResult(ctx.Word is null ? null : "**hover:" + ctx.Word + "**"));

            var diff = MonacoEditor.Diff().SetLanguage("csharp")
               .SetContent("int a = 1;\nint b = 2;\n", "int a = 1;\nint b = 3;\nint c = 4;\n");

            var shell = MonacoEditor.MultiEditor().Views("consumer");
            shell.Documents(new[]
            {
                new EditorDocument("one.cs", "one.cs") { Text = "class One {}", Language = "csharp" },
                new EditorDocument("two.cs", "two.cs") { Text = "class Two {}", Language = "csharp" },
            });

            var root = VStack().WS().Gap(8.px()).P(16).Children(
                TextBlock("consumer").Id("consumer-title"),
                Button("History").Id("history-btn").OnClick(() => editor.ShowHistory()),
                Button("Open both").Id("open-btn").OnClick(() => { shell.Open("one.cs"); shell.Open("two.cs"); }),
                editor.WS().H(200.px()),
                diff.WS().H(200.px()),
                shell.WS().H(300.px()));

            document.body.appendChild(root.Render());
        }
    }
}
