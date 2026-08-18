using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 2, Icon = UIcons.KeyboardBrightness)]
    public class ActionsAndCommandsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public ActionsAndCommandsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText("var a = 1;\nvar b = 2;\nvar c = 3;\n");

            var log = TextBlock("right-click for \"Wrap in braces\", or press Ctrl+Alt+B").Small().Secondary();

            // An action has an id, a label, a keybinding and a context-menu group - so it shows up in the
            // menu and the command palette, exactly like one of Monaco's own.
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

            // A command is a keybinding with no menu entry - and it wins over the browser's own.
            editor.AddCommand(KeyMod.With(KeyMod.CtrlCmd, KeyCode.KeyS), () => log.Text = "Ctrl+S intercepted - the browser's save dialog never opened");

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(ActionsAndCommandsSample), UIcons.KeyboardBrightness, "Your own actions, keybindings, and Monaco's by id")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock(".AddAction(id, label, run, keybindings, group) adds an action to this editor: it appears in the context menu and the command palette, takes the keybinding you give it, and receives the editor surface so it can read the selection and apply edits. .AddCommand(keybinding, handler) is the same thing without any UI - a shortcut, nothing more."),
                        TextBlock("Monaco's own actions are reachable by id too: .RunAction(\"editor.action.commentLine\") and the named shortcuts around it - .ToggleLineComment(), .ShowFind(), .SelectAll(), .Format() - all go through the same lookup.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Build keybindings from KeyMod and KeyCode rather than raw numbers, and prefer KeyMod.CtrlCmd over Ctrl - it is Cmd on macOS, which is what a Mac user expects. An action's keybinding only applies while the editor has focus, so it cannot steal a shortcut from the rest of the app."),
                        TextBlock("Two ways to run something by id, and the difference matters: .RunAction(...) only sees editor actions and reports whether the id matched, while .Trigger(...) also reaches commands Monaco registered as keybinding rules - the navigation ones - but says nothing about whether anything ran. Reach for RunAction first, and Trigger when it returns false.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Right-click in the editor for \"Wrap in braces\" in the first group of the menu, or press Ctrl+Alt+B. Ctrl+S is intercepted while the editor has focus. The buttons run Monaco's own actions by id."),
                        editor.WS().H(160.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).PT(8).Children(
                            Button("Comment line").OnClick(() => { editor.Focus(); editor.ToggleLineComment(); }),
                            Button("Find").OnClick(() => { editor.Focus(); editor.ShowFind(); }),
                            Button("Select all").OnClick(() => { editor.Focus(); editor.SelectAll(); }),
                            Button("Run custom action").OnClick(() =>
                            {
                                editor.Focus();
                                editor.SetSelection(Ranges.Of(2, 1, 2, 11));
                                editor.Trigger("sample.wrapInBraces");
                            })),
                        log.PT(8),
                        SampleHint("The action's edit is one undoable step, so Ctrl+Z unwraps the line again.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(EventsSample), typeof(NavigationSample), typeof(FormattingSample));
        }

        public HTMLElement Render() => _content.Render();
    }
}
