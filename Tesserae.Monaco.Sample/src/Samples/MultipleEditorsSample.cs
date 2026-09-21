using System.Collections.Generic;
using System.Threading.Tasks;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// Four Monaco surfaces on one page, all on the same language, each wired to its own providers -
    /// the page that proves nothing leaks between them.
    /// </summary>
    [SampleDetails(Group = "Runtime and hosting", Order = 4, Icon = UIcons.Duplicate)]
    public class MultipleEditorsSample : IComponent, ISample
    {
        /// <summary>A stable hook for the scope log, so a browser check can read what answered what.</summary>
        internal const string LOG_CLASS = "tssm-sample-scope-log";

        private const int LOG_LENGTH = 8;

        private const string ALPHA_TEXT =
            "// alpha.cs - its own completion, hover, diagnostics and keybinding\n" +
            "var first = alphaOne;\n" +
            "// TODO: alpha flags TODO; beta flags FIXME\n";

        private const string BETA_TEXT =
            "// beta.cs - same language, a different set of everything\n" +
            "var second = betaOne;\n" +
            "// FIXME: beta flags FIXME; alpha flags TODO\n";

        private const string VIEWER_TEXT =
            "// viewer.cs - csharp too, and wired to nothing\n" +
            "var third = alphaOne;\n" +
            "// TODO: no squiggle here - alpha's validator only sees alpha's model\n";

        private const string DIFF_ORIGINAL =
            "// diff.cs - the modified side has a provider of its own\n" +
            "var fourth = diffOne;\n" +
            "// TODO: no squiggle here either\n";

        private const string DIFF_MODIFIED =
            "// diff.cs - the modified side has a provider of its own\n" +
            "var fourth = diffTwo;\n" +
            "// TODO: no squiggle here either\n" +
            "// type diffO on the right for its completion\n";

        private readonly IComponent    _content;
        private readonly List<string>  _entries = new List<string>();
        private readonly TextBlock     _log     = TextBlock("nothing yet").Small().Secondary().Class(LOG_CLASS);

        public MultipleEditorsSample()
        {
            var alpha  = BuildEditor("alpha", ALPHA_TEXT, new[] { "alphaOne", "alphaTwo" }, "TODO");
            var beta   = BuildEditor("beta",  BETA_TEXT,  new[] { "betaOne",  "betaTwo"  }, "FIXME");

            var viewer = MonacoEditor.Viewer()
               .SetLanguage("csharp")
               .SetText(VIEWER_TEXT);

            // A diff is read-only on both sides by default; .Editable() opens the modified one, which
            // is the side a provider is registered on.
            var diff = MonacoEditor.Diff()
               .SetLanguage("csharp")
               .Editable()
               .SetContent(DIFF_ORIGINAL, DIFF_MODIFIED);

            // A diff editor is two editors, and the modified one takes providers like any other. Its
            // completion is registered for csharp as well, and answers only for its own model - so the
            // two editors above never see diffOne/diffTwo, and it never sees theirs.
            diff.OnCompletion("csharp", context =>
            {
                Log("diff (modified side): completion");

                return Task.FromResult(Items(new[] { "diffOne", "diffTwo" }, "diff"));
            },
            triggerCharacters: new[] { "." });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(MultipleEditorsSample), UIcons.Duplicate, "Four surfaces, one language, and no crosstalk between them")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A page can hold as many editors, viewers and diffs as it likes, and every feature is configured per component: the two editors below share the csharp language and nothing else. Each has its own completion list, its own hover, its own validator and its own Ctrl+Alt+K - and the log under them names whichever one answered."),
                        TextBlock("That needs work under the hood, because Monaco's language providers are registered globally per language rather than per editor: without gating, both editors would answer every csharp request and the suggest list would show all four words. Every registration the package makes is gated on its own model, and released when its component unmounts.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("What is per editor: completion, hover, formatting, diagnostics, every other provider, decorations, widgets, view zones, actions, keybindings added with .AddCommand(...) and .OnSave(...), options and the undo stack. Configure them on the component and they stay there."),
                        TextBlock("What is global, and shared on purpose: the themes, languages registered with MonacoEditor.RegisterLanguage(...), commands registered with MonacoEditor.RegisterCommand(...) - a second registration under the same id replaces the first, so give each editor its own id or pass the editor's name as the link argument - and the overflow host the popups render into.").MT(8),
                        TextBlock("Cost is the thing to weigh, not correctness: every editor is a full Monaco instance with its own DOM, view and workers' share of the traffic. Several documents in one editor - see Several Documents - is cheaper when only one is on screen at a time; several editors are right when they are all visible at once, as here.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Two editors, same language"),
                        TextBlock("Press Ctrl+Space in each: alpha offers alphaOne/alphaTwo, beta offers betaOne/betaTwo, and neither offers the other's. Hover the words for the same split. Type a TODO in alpha and a FIXME in beta - each validator flags only its own word, in its own editor."),
                        HStack().WS().Wrap().MT(8).Children(
                            Pane("alpha.cs", alpha.WS().H(170.px())).PR(6),
                            Pane("beta.cs", beta.WS().H(170.px())).PL(6)),
                        TextBlock("Keybindings are per editor too: Ctrl+Alt+K and Ctrl+S run the handler of whichever editor has focus, not the one registered last.").MT(12),
                        SampleSubTitle("A read-only viewer, and a diff, on the same page"),
                        TextBlock("Both are csharp as well. The viewer is wired to nothing: alphaOne has no hover there and its TODO is not flagged, even though the two editors above answer both for their own documents. Being read-only, it has no suggest widget at all."),
                        TextBlock("The diff is two editors in one component, and its modified side is editable and carries a completion of its own - press Ctrl+Space on the right-hand pane for diffOne/diffTwo. Neither of the two editors above offers those, and it does not offer theirs.").MT(8),
                        HStack().WS().Wrap().MT(8).Children(
                            Pane("viewer.cs (read-only)", viewer.WS().H(150.px())).PR(6),
                            Pane("diff.cs (modified side editable)", diff.WS().H(150.px())).PL(6)),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(12).Children(
                            Button("Focus alpha").OnClick(() => alpha.Focus()),
                            Button("Focus beta").OnClick(() => beta.Focus()),
                            Button("Clear log").OnClick(() =>
                            {
                                _entries.Clear();
                                _log.Text = "nothing yet";
                            })),
                        VStack().WS().PT(8).Children(
                            TextBlock("Scope log (newest first)").Small().SemiBold(),
                            _log.MT(4)),
                        SampleHint("Every provider call writes its editor's name here. A line naming an editor you were not in is crosstalk.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(SeveralDocumentsSample), typeof(CompletionAndHoverSample), typeof(MultiEditorSample), typeof(RemountSample));
        }

        /// <summary>
        /// One half of a row: a caption over a surface. Exactly half the width rather than a flex-grow
        /// share, because a Monaco container's intrinsic width is whatever Monaco last wrote into it -
        /// two editors left to grow into the leftover space came out 900px and 250px.
        /// </summary>
        private static IComponent Pane(string caption, IComponent surface)
        {
            return VStack().W(50.percent()).MinWidth(260.px()).Children(
                TextBlock(caption).Small().SemiBold(),
                surface.MT(4));
        }

        /// <summary>
        /// One editor, with a full set of per-editor features keyed to its name: the completion list, the
        /// hover documentation, the validator's word, and two keybindings.
        /// </summary>
        private CodeEditor BuildEditor(string name, string text, string[] words, string flagged)
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(text);

            editor.OnCompletion(context =>
            {
                Log(name + ": completion");

                return Task.FromResult(Items(words, name));
            },
            triggerCharacters: new[] { "." });

            editor.OnHover(context =>
            {
                if (context.Word is null) return Task.FromResult<string>(null);

                foreach (var word in words)
                {
                    if (word != context.Word) continue;

                    Log(name + ": hover on " + word);

                    return Task.FromResult("**" + word + "**\n\nDocumented by the " + name + " editor.");
                }

                return Task.FromResult<string>(null);
            });

            // Each validator flags its own word, and its markers land on its own model.
            editor.ValidateAsYouType(code =>
            {
                var diagnostics = new List<CodeDiagnostic>();
                var lines       = code.Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var at = lines[i].IndexOf(flagged);

                    if (at < 0) continue;

                    diagnostics.Add(new CodeDiagnostic(i, at, i, at + flagged.Length, flagged + " found by the " + name + " editor", MarkerSeverity.Warning));
                }

                if (diagnostics.Count > 0) Log(name + ": " + diagnostics.Count + " " + flagged);

                return Task.FromResult<ReadOnlyArray<CodeDiagnostic>>(diagnostics.ToArray());
            });

            editor.OnSave(() =>
            {
                Log(name + ": Ctrl+S");

                return Task.CompletedTask;
            });

            editor.AddCommand(KeyMod.With(KeyMod.CtrlCmd | KeyMod.Alt, KeyCode.KeyK), () => Log(name + ": Ctrl+Alt+K"));

            return editor;
        }

        /// <summary>The same completion list every time, built from the editor's own words.</summary>
        private static CompletionItem[] Items(string[] words, string owner)
        {
            var items = new CompletionItem[words.Length];

            for (var i = 0; i < words.Length; i++)
            {
                items[i] = new CompletionItem
                {
                    label  = words[i],
                    kind   = CompletionItemKind.Variable,
                    detail = "from the " + owner + " editor"
                };
            }

            return items;
        }

        private void Log(string entry)
        {
            _entries.Insert(0, entry);

            while (_entries.Count > LOG_LENGTH) _entries.RemoveAt(_entries.Count - 1);

            _log.Text = string.Join(" · ", _entries);
        }

        public HTMLElement Render() => _content.Render();
    }
}
