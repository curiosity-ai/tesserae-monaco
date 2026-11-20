using System;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Editors", Order = 7, Icon = UIcons.Sparkles)]
    public class AIVariantSample : IComponent, ISample
    {
        private const string ReleaseNotes =
            "# Release 4.2\n" +
            "\n" +
            "This release makes search faster and the editor easier to share.\n" +
            "\n" +
            "## Highlights\n" +
            "\n" +
            "- Search answers in under 80 ms on a million documents.\n" +
            "- Locked lines keep a template's headings in place.\n" +
            "- Editors show which parts a model wrote.\n";

        private const string ReleaseNotesAgain =
            "# Release 4.2\n" +
            "\n" +
            "Faster search, and an editor that is easier to share and to review.\n" +
            "\n" +
            "## Highlights\n" +
            "\n" +
            "- Sub-80 ms search on a million documents.\n" +
            "- Template headings that stay where they are.\n" +
            "- Model-written text marked as such, line by line.\n";

        private const string Code =
            "public sealed class OrderService\n" +
            "{\n" +
            "    private readonly IOrderStore _store;\n" +
            "\n" +
            "    public OrderService(IOrderStore store) => _store = store;\n" +
            "\n" +
            "    public decimal Total(Order order)\n" +
            "    {\n" +
            "        var subtotal = order.Lines.Sum(l => l.Price * l.Quantity);\n" +
            "        var discount = order.Coupon?.Apply(subtotal) ?? 0m;\n" +
            "        return Math.Max(0m, subtotal - discount);\n" +
            "    }\n" +
            "\n" +
            "}\n";

        private const string GeneratedMethod =
            "\n" +
            "    public async Task<Order> CancelAsync(string orderId)\n" +
            "    {\n" +
            "        var order = await _store.GetAsync(orderId) ?? throw new KeyNotFoundException(orderId);\n" +
            "        order.Status = OrderStatus.Cancelled;\n" +
            "        await _store.SaveAsync(order);\n" +
            "        return order;\n" +
            "    }\n";

        private const string Prose =
            "## Why we moved the build\n" +
            "\n" +
            "The old build ran on one machine under a desk. It was slow, it failed when the power did, and only two people knew how to fix it. " +
            "Moving it to the cloud cut a full build from forty minutes to six. Every pull request now gets a preview site of its own.\n" +
            "\n" +
            "## What changes for you\n" +
            "\n" +
            "Nothing, if you only push code. ";

        private const string Continuation =
            "Builds start on their own when a pull request opens. The preview link appears as a comment within a few minutes. If a build fails, the log is one click away.";

        private const string Phrase =
            "The function returns the total for an order.\n" +
            "\n" +
            "It sums each line's price and quantity, then subtracts the coupon.\n";

        private readonly IComponent _content;

        public AIVariantSample()
        {
            // --- The whole editor -------------------------------------------------------------------------------------
            var whole = MonacoEditor.Editor()
               .SetLanguage("markdown")
               .SetText(ReleaseNotes)
               .WordWrap()
               .AI(badge: true);

            var strong = false;

            // --- Line by line -----------------------------------------------------------------------------------------
            var linesStatus = TextBlock("").Small().Secondary();

            var lines = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(Code)
               // The body of Total() came from the model; the rest was typed.
               .MarkAILines(9, 11);

            lines.AIAnnotations.OnChanged(() => linesStatus.Text = Summary(lines.AIAnnotations, "line"));

            // --- Sentence by sentence --------------------------------------------------------------------------------
            var sentencesStatus = TextBlock("").Small().Secondary();

            var sentences = MonacoEditor.Editor()
               .SetLanguage("markdown")
               .SetText(Prose)
               .WordWrap()
               // Two sentences in the middle of a paragraph a person wrote.
               .MarkAI(RangeOf(Prose, "Moving it to the cloud cut a full build from forty minutes to six. Every pull request now gets a preview site of its own."), AIMarking.Sentences);

            sentences.AIAnnotations.OnChanged(() => sentencesStatus.Text = Summary(sentences.AIAnnotations, "sentence"));

            // --- A span ----------------------------------------------------------------------------------------------
            var spanStatus = TextBlock("Select some text, then ask for a rewrite.").Small().Secondary();

            var span = MonacoEditor.Editor()
               .SetLanguage("markdown")
               .SetText(Phrase)
               .WordWrap();

            span.AIAnnotations.OnChanged(() => spanStatus.Text = Summary(span.AIAnnotations, "span"));

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(AIVariantSample), UIcons.Sparkles, "Showing which text a model wrote, and keeping it true as people edit")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Tesserae's AI variant is one purple-to-blue language for anything a model made: AI() on a Card, a Button, a TextArea. The editor takes the same call - .AI(badge: true) puts the AI tint and accent edge over it and an AI badge in its corner, which turns to Edited once the user changes the text, exactly as on a TextArea."),
                        TextBlock("An editor is also where a model's text and a person's end up mixed, so it can go further: InsertAI(...) and MarkAI(...) mark only the parts a model wrote, one mark per line or per sentence. Each mark remembers its text, moves with the edits around it, and is drawn as edited the moment its own text changes - so a reviewer can see what was generated, and what of that a person has since touched.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Lines are the unit for code: a tint behind the line and the AI accent beside the line numbers, dashed once edited. Sentences are the unit for prose: the sentence itself is tinted and underlined, and only the sentence that was changed turns to a dashed underline. Range marks one span, for a rewrite of a selection. Marks also appear in the overview ruler and the minimap, so the share of a long file a model wrote is visible at a glance."),
                        TextBlock("Everything is drawn with Tesserae's --tss-ai-* tokens, so it follows the light and dark themes and an app's own AI hue. Hovering a mark says AI-generated or AI-generated, then edited (AIAnnotations.GeneratedHover / EditedHover, for a host that translates). SetText clears the marks - a new answer is not the old one - and a remount keeps them.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("The whole editor: .AI(badge: true)"),
                        TextBlock("Type anything: the badge turns to Edited. Undo back to the original and it says AI again. Regenerate hands over a new answer, which is what the model wrote from then on."),
                        whole.WS().H(220.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Regenerate").AI().OnClick(() => whole.SetText(whole.Text == ReleaseNotes ? ReleaseNotesAgain : ReleaseNotes)),
                            Button("Toggle strong effect").OnClick(() =>
                            {
                                strong = !strong;
                                whole.AI(strongEffect: strong, badge: true);
                            }),
                            Button("Turn off").OnClick(() => whole.AI(false)),
                            Button("Turn on").OnClick(() => whole.AI(strongEffect: strong, badge: true)))
                    )).SetTitle("Usage")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Line by line: AIMarking.Lines"),
                        TextBlock("Lines 9 to 11 were written by a model. Put the caret on the empty line before the closing brace and ask for a method: it is inserted and marked line by line. Then change one of those lines - only that line goes dashed."),
                        lines.WS().H(320.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Write CancelAsync").AI().OnClick(() =>
                            {
                                var position = lines.GetPosition();
                                if (position is null || lines.Model.Native.getLineContent(position.lineNumber).Trim().Length > 0) lines.SetPosition(new Position { lineNumber = 13, column = 1 });
                                lines.InsertAI(GeneratedMethod, AIMarking.Lines);
                                lines.Focus();
                            }),
                            Button("Accept the caret's line").OnClick(() =>
                            {
                                var position = lines.GetPosition();
                                if (position is object) lines.AIAnnotations.Unmark(position.lineNumber);
                            }),
                            linesStatus.PL(8.px()))
                    )).SetTitle("Lines")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Sentence by sentence: AIMarking.Sentences"),
                        TextBlock("Two sentences of the first paragraph were suggested by a model. Continue writing adds three more at the end, each its own mark. Edit a single word in one of them and only that sentence changes to the edited look."),
                        sentences.WS().H(260.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Continue writing").AI().OnClick(() =>
                            {
                                var model = sentences.Model.Native;
                                var last  = model.getLineCount();
                                sentences.SetPosition(new Position { lineNumber = last, column = model.getLineMaxColumn(last) });
                                sentences.InsertAI(Continuation, AIMarking.Sentences);
                                sentences.Focus();
                            }),
                            sentencesStatus.PL(8.px()))
                    )).SetTitle("Sentences")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("One span: AIMarking.Range"),
                        TextBlock("Select a phrase and ask for a rewrite: the selection is replaced and the new text marked as one span."),
                        span.WS().H(140.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Rewrite selection").AI().OnClick(() =>
                            {
                                span.InsertAI("adds up the price of every line, times its quantity, and takes off whatever the coupon is worth", AIMarking.Range);
                                span.Focus();
                            }),
                            spanStatus.PL(8.px()))
                    )).SetTitle("Range")))
               .SeeAlso(typeof(LockedLinesSample), typeof(DecorationsSample), typeof(HistoryPersistenceSample));
        }

        private static string Summary(AIAnnotations annotations, string unit)
        {
            var count  = annotations.Count;
            var edited = annotations.EditedCount;
            if (count == 0) return "nothing marked as AI-written";
            return $"{count} {unit}{(count == 1 ? "" : "s")} written by AI, {edited} edited since";
        }

        // The range of the first occurrence of snippet in text, in Monaco's one-based lines and columns.
        private static TextRange RangeOf(string text, string snippet)
        {
            var start = text.IndexOf(snippet, StringComparison.Ordinal);
            if (start < 0) return null;

            Position At(int offset)
            {
                int line = 1, column = 1;
                for (var i = 0; i < offset; i++)
                {
                    if (text[i] == '\n') { line++; column = 1; }
                    else column++;
                }
                return new Position { lineNumber = line, column = column };
            }

            var a = At(start);
            var b = At(start + snippet.Length);
            return new TextRange { startLineNumber = a.lineNumber, startColumn = a.column, endLineNumber = b.lineNumber, endColumn = b.column };
        }

        public HTMLElement Render() => _content.Render();
    }
}
