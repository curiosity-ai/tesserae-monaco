using System.Linq;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Editors", Order = 6, Icon = UIcons.Lock)]
    public class LockedLinesSample : IComponent, ISample
    {
        private const string Template =
            "# Incident report\n" +
            "\n" +
            "## Summary\n" +
            "\n" +
            "What happened, in two or three sentences.\n" +
            "\n" +
            "## Impact\n" +
            "\n" +
            "Who was affected, and for how long.\n" +
            "\n" +
            "## Timeline\n" +
            "\n" +
            "- 09:12 first alert\n" +
            "\n" +
            "## Root cause\n" +
            "\n" +
            "## Action items\n" +
            "\n" +
            "- [ ] \n";

        private readonly IComponent _content;

        public LockedLinesSample()
        {
            // The two classes the locked lines carry. The package ships no CSS, so this is the host's job.
            SampleStyles.Ensure();

            var locked  = TextBlock("").Small().Secondary();
            var blocked = TextBlock("").Small().Secondary();
            var count   = 0;

            CodeEditor editor = null;

            editor = MonacoEditor.Editor()
               .SetLanguage("markdown")
               .SetText(Template)
               .WordWrap()
               // Every heading the template starts with. A heading the user writes later is not locked.
               .LockLines(line => line.StartsWith("#"))
               .OnContentChanged(_ => window.setTimeout(__ => ShowLocked(editor, locked), 0))
               .OnRendered(_ => ShowLocked(editor, locked));

            editor.LockedLines.OnBlocked(line =>
            {
                count++;
                blocked.Text = $"refused {count} edit{(count == 1 ? "" : "s")} - the last one would have changed line {line}";
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(LockedLinesSample), UIcons.Lock, "Lines the user can write around but not change")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("A locked line cannot be edited or deleted, but everything around it can: text can be typed above and below it, Enter at its start opens a line above, Enter at its end opens one below, and the lines beside it can be removed. The lock follows the line rather than its number, so once the user adds three lines above a locked heading, it is locked three lines further down."),
                        TextBlock(".LockLines(...) takes line numbers or a predicate over each line's text, applied to the document as it is when the editor mounts. Here every markdown heading of the template is locked, which turns a free-text editor into a form whose sections the user fills in but cannot remove or rename.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("An edit that would touch a locked line is refused before it is applied - typing, paste, cut, drag and drop, Delete or Backspace across a line break, Alt+Up/Down, select-all and type - so the document never flickers and the undo stack never sees it. Monaco shows its read-only bubble at the caret; set LockedLines.BlockedMessage for your own text, or null for none, and LockedLines.OnBlocked(...) to react yourself."),
                        TextBlock("The locks are the host's own edits too: ApplyEdits and ExecuteEdits are held to them unless run inside LockedLines.RunUnguarded(...). Replacing the whole text with SetText re-applies the original numbers and predicates to the new text. Style the lines through the tssm-locked-line and tssm-locked-line-gutter classes - this page paints a tinted band and a bar in the gutter.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Try to change or delete a heading - by typing in it, Backspace at the start of the line below it, selecting across it, or Ctrl+A then Delete. Then add text under each heading, or press Enter at the start or end of a heading, and watch the locked line numbers follow."),
                        editor.WS().H(360.px()).MT(8),
                        VStack().WS().PT(8).Children(locked, blocked),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Lock the caret's line").SetIcon(UIcons.Lock).OnClick(() =>
                            {
                                var position = editor.GetPosition();
                                if (position is object) editor.LockLines(position.lineNumber);
                                ShowLocked(editor, locked);
                            }),
                            Button("Unlock the caret's line").SetIcon(UIcons.Unlock).OnClick(() =>
                            {
                                var position = editor.GetPosition();
                                if (position is object) editor.LockedLines.Unlock(position.lineNumber);
                                ShowLocked(editor, locked);
                            }),
                            Button("Reset the template").OnClick(() =>
                            {
                                editor.SetText(Template);
                                ShowLocked(editor, locked);
                            })),
                        SampleHint("Undo works as usual: a refused edit was never applied, so there is nothing of it to undo.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(DecorationsSample), typeof(CodeEditorSample), typeof(RemountSample));
        }

        private static void ShowLocked(CodeEditor editor, TextBlock target)
        {
            var lines = editor.LockedLines?.LineNumbers ?? new int[0];
            target.Text = "locked lines: " + (lines.Length == 0 ? "none" : string.Join(", ", lines.Select(l => l.ToString())));
        }

        public HTMLElement Render() => _content.Render();
    }
}
