using System;
using System.Linq;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    [SampleDetails(Group = "Runtime and hosting", Order = 4, Icon = UIcons.Palette)]
    public class LanguagesAndThemesSample : IComponent, ISample
    {
        private readonly IComponent _content;

        public LanguagesAndThemesSample()
        {
            var viewer = MonacoEditor.Viewer()
               .SetLanguage("csharp")
               .SetText(SampleCode.CSharp);

            var activeTheme = TextBlock().Small().Secondary();

            void ShowActiveTheme() => activeTheme.Text = $"Monaco is rendering with \"{MonacoEditor.ActiveTheme}\".";

            ShowActiveTheme();

            var extension = TextBox().SetPlaceholder("A file extension, e.g. .cs, .ts, .py").Width(280.px());
            var resolved  = TextBlock().Small().Secondary();

            void Resolve()
            {
                if (MonacoEditor.TryGetLanguageIdForExtension(extension.Text, out var languageId))
                {
                    resolved.Text = $"{extension.Text} is the \"{languageId}\" language.";
                    viewer.SetLanguageByExtension(extension.Text);
                }
                else
                {
                    resolved.Text = $"Monaco knows no language for \"{extension.Text}\".";
                }
            }

            extension.OnChange((_, __) => Resolve());

            // GetLanguageIds() answers with an empty array until Monaco is on the page, so the list
            // waits for the load rather than racing it. LoadAsync() is idempotent - every component
            // already awaits it on mount, so this only ever piggybacks on a load in flight.
            var languageList = Defer(async () =>
            {
                await MonacoEditor.LoadAsync();

                var ids = MonacoEditor.GetLanguageIds().OrderBy(id => id, StringComparer.Ordinal).ToArray();

                return (IComponent)VStack().WS().Children(
                    TextBlock($"The bundle ships {ids.Length} languages:").Small().Secondary(),
                    TextBlock(string.Join(", ", ids)).Small().PT(4));
            }, TextBlock("Loading Monaco...").Small().Secondary());

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(LanguagesAndThemesSample), UIcons.Palette, "What Monaco knows how to colour, and which theme it uses")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("MonacoEditor also owns everything that is global rather than per-editor: loading the bundle (LoadAsync, IsLoaded), the language registry (GetLanguageIds, TryGetLanguageIdForExtension, RegisterLanguage), and the two themes the package derives from Tesserae's own colours."),
                        TextBlock("The themes are tss-light and tss-dark. ActiveTheme is whichever matches Tesserae's current mode; DefineThemes() re-derives both from the colours Tesserae is using now, and ApplyTheme() switches every live editor to the matching one.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Nothing has to be loaded by hand: every component awaits LoadAsync() when it mounts. Call it yourself only when you need Monaco's globals before an editor exists - listing the languages, as this page does, or resolving an extension."),
                        TextBlock("Call DefineThemes() and then ApplyTheme() from wherever the app switches Theme.Light() / Theme.Dark() - the sidebar's sun/moon button here does exactly that. Applying without re-defining leaves the editor painted in the colours of the theme it was loaded under, because the editor background is baked into the theme definition. Custom languages registered with a TokenColors array are folded into both themes, so they re-colour with everything else.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Themes"),
                        TextBlock("Switch the theme - the editor below follows, along with the rest of the page."),
                        HStack().WS().AlignItemsCenter().Gap(8.px()).PT(8).Children(
                            Button("Light").SetIcon(UIcons.Sun).OnClick(() => SwitchTheme(false, ShowActiveTheme)),
                            Button("Dark").SetIcon(UIcons.Moon).OnClick(() => SwitchTheme(true, ShowActiveTheme)),
                            activeTheme),
                        viewer.WS().H(200.px()).MT(8),
                        SampleSubTitle("Resolving a file extension"),
                        TextBlock("TryGetLanguageIdForExtension asks Monaco's registry, so it covers every language the bundle ships - and any custom one that declared an extension."),
                        HStack().WS().AlignItemsCenter().Gap(8.px()).PT(8).Children(extension, Button("Resolve").OnClick(() => Resolve()), resolved),
                        SampleSubTitle("Every language Monaco knows"),
                        languageList.WS().MT(8)
                    )).SetTitle("Usage")))
               .SeeAlso(typeof(CustomLanguageSample), typeof(CodeViewerSample), typeof(EditorOptionsSample));
        }

        /// <summary>
        /// The full hand-over: switch Tesserae's theme, re-derive Monaco's two themes from the
        /// colours that are now active, then move every live editor onto the matching one.
        /// </summary>
        private static void SwitchTheme(bool dark, Action onDone)
        {
            if (dark) Theme.Dark();
            else Theme.Light();

            MonacoEditor.DefineThemes();
            MonacoEditor.ApplyTheme();

            onDone();
        }

        public HTMLElement Render() => _content.Render();
    }
}
