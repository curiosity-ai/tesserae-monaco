using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 0, Icon = UIcons.Settings)]
    public class EditorOptionsSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public EditorOptionsSample()
        {
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(SampleCode.CSharpChanged)

                // Construction options: everything Monaco can only be told once, plus the defaults
                // this editor wants to start from.
               .Options(options =>
                {
                    options.minimap                 = new MinimapOptions { enabled = true, side = "right", renderCharacters = false };
                    options.fontSize                = 13;
                    options.lineHeight              = 20;
                    options.renderWhitespace        = "selection";
                    options.bracketPairColorization = new BracketPairColorizationOptions { enabled = true };
                    options.padding                 = new PaddingOptions { top = 8, bottom = 8 };
                    options.scrollBeyondLastLine    = false;

                    // Off, the wheel keeps scrolling the page once the editor has nothing left to
                    // scroll - which is what a short editor inside a long page wants.
                    options.scrollbar = new ScrollbarOptions { alwaysConsumeMouseWheel = false };
                });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(EditorOptionsSample), UIcons.Settings, "Monaco's own options, at construction and at runtime")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Every component takes .Options(options => ...), which adjusts Monaco's construction options just before the editor is created. EditorOptions is an object literal that emits only the fields you assign, so it is also exactly what updateOptions wants - an option you never mention is left alone."),
                        TextBlock("It covers the options this wrapper surfaces plus the ones a host usually reaches for. Anything else is still reachable: the object is a plain JavaScript object at runtime, so ((dynamic)options).someOption = value works for the rest.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Use .Options(...) for what has to be true before the first paint - font metrics, padding, the minimap, whether the editor scrolls past the last line. Use .Editor.updateOptions(...) afterwards for anything the user toggles; .Editor is null until the component has mounted, so guard for it or call it from .OnRendered(...)."),
                        TextBlock("Two options are set by the package itself and should not be overridden: fixedOverflowWidgets and overflowWidgetsDomNode point the suggest and hover popups at a shared body-mounted host, which is what stops them being clipped by a modal or a panel.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Constructed with options"),
                        TextBlock("A minimap on the right, 13px text on a 20px line, 8px of padding, bracket-pair colouring, and a mouse wheel that hands scrolling back to the page at the ends."),
                        editor.WS().H(260.px()).MT(8),
                        SampleSubTitle("Changed at runtime"),
                        TextBlock("The same options object drives updateOptions, so each button below sends only the field it names."),
                        HStack().WS().Wrap().Gap(8.px()).PT(8).Children(
                            Button("Minimap off").OnClick(() => Update(editor, new EditorOptions { minimap = new MinimapOptions { enabled = false } })),
                            Button("Minimap on").OnClick(()  => Update(editor, new EditorOptions { minimap = new MinimapOptions { enabled = true } })),
                            Button("Bigger text").OnClick(() => Update(editor, new EditorOptions { fontSize = 18, lineHeight = 26 })),
                            Button("Smaller text").OnClick(() => Update(editor, new EditorOptions { fontSize = 13, lineHeight = 20 })),
                            Button("Show whitespace").OnClick(() => Update(editor, new EditorOptions { renderWhitespace = "all" })),
                            Button("Hide whitespace").OnClick(() => Update(editor, new EditorOptions { renderWhitespace = "selection" })),
                            Button("No line numbers").OnClick(() => Update(editor, new EditorOptions { lineNumbers = "off" })),
                            Button("Line numbers").OnClick(() => Update(editor, new EditorOptions { lineNumbers = "on" })))
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CodeEditorSample), typeof(LanguagesAndThemesSample), typeof(DiffViewerSample));
        }

        private static void Update(CodeEditor editor, EditorOptions options)
        {
            // .Editor is null until the component has mounted, which cannot happen from a button on
            // the page it is on - but a host calling this earlier has to check.
            if (editor.Editor is object) editor.Editor.updateOptions(options);
        }

        public HTMLElement Render() => _content.Render();
    }
}
