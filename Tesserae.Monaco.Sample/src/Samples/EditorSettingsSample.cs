using System.Collections.Generic;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 13, Icon = UIcons.SettingsSliders)]
    public class EditorSettingsSample : IComponent, ISample
    {
        private const string CODE = "var total = 42;\nvar ratio = 0.5;\nvar label = \"settings\";\n\nint Add(int a, int b)\n{\n    return a + b;\n}\n";

        private readonly IComponent _content;

        public EditorSettingsSample()
        {
            // The sample's own default: the host asks for inline hints off, which is what a person sees
            // until they save a choice of their own.
            var defaults = new EditorSettings { InlayHints = "off" };

            var first  = Build(defaults);
            var second = Build(defaults);

            var stored = TextBlock().Small().Secondary().Id("stored-settings");

            void ShowStored() => stored.Text = window.localStorage.getItem(EditorSettingsStore.KEY) ?? "(nothing saved: the defaults apply)";

            ShowStored();

            EditorSettingsStore.Subscribe(ShowStored);

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(EditorSettingsSample), UIcons.SettingsSliders, "Per-person editor preferences, kept in the browser")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Every .CodeEditor() has an Editor Settings entry at the foot of its context menu. It opens a dialog with the preferences a person would reasonably want to change - font size, word wrap, the minimap, inline hints, hover and parameter popups, tab size and more - and Save writes them to localStorage, so they apply to every editor of the application and to the next visit."),
                        TextBlock("An application that wants other defaults for its people (inline hints off, say) passes an adjusted EditorSettings to .SettingsDefaults(...). Those apply until the person saves a choice, and are what Reset to defaults returns to. .NoUserSettings() takes the entry away for an editor whose look the application owns.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Saved settings win over everything the application configured, so only offer the entry where that is the intention. A save or a reset reaches every live editor - and every other tab - without a reload; EditorSettingsStore.Subscribe(...) is the same notification for a host that wants to follow it."),
                        TextBlock("All of the dialog's text is English and goes through EditorSettingsDialog.Translate, one hook for an application that translates its interface.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Try it"),
                        TextBlock("Right-click either editor and choose Editor Settings…. Both editors have inline hints off to begin with; switch them on, change the font size or turn on word wrap, press Save, and watch both editors follow."),
                        first.WS().H(170.px()).MT(8),
                        second.WS().H(170.px()).MT(8),
                        HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().PT(8).Children(
                            Button("Open settings").SetIcon(UIcons.Settings).Id("open-settings").OnClick(() => first.ShowSettings()),
                            Button("Reset to defaults").OnClick(() => EditorSettingsStore.Clear())),
                        SampleSubTitle("Stored under " + EditorSettingsStore.KEY),
                        stored.Id("stored-settings"),
                        SampleHint("The inline hints here come from a provider that annotates every assignment with its type.")
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(EditorOptionsSample), typeof(InlayHintsAndLensesSample));
        }

        private static CodeEditor Build(EditorSettings defaults)
        {
            return MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(CODE)
               .SettingsDefaults(defaults)
               .OnInlayHints((text, range) =>
                {
                    var hints = new List<InlayHint>();
                    var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');

                    for (var i = 0; i < lines.Length; i++)
                    {
                        var equals = lines[i].IndexOf('=');

                        if (equals < 0 || !lines[i].TrimStart().StartsWith("var ")) continue;

                        var value = lines[i].Substring(equals + 1).Trim();

                        hints.Add(new InlayHint
                        {
                            position     = new Position { lineNumber = i + 1, column = lines[i].IndexOf("var ") + 5 },
                            label        = value.StartsWith("\"") ? "string " : value.Contains(".") ? "double " : "int ",
                            kind         = InlayHintKind.Type,
                            paddingRight = true
                        });
                    }

                    return Task.FromResult(hints.ToArray());
                });
        }

        public HTMLElement Render() => _content.Render();
    }
}
