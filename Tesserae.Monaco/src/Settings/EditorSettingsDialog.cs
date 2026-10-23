using System;
using System.Collections.Generic;
using System.Linq;
using Tesserae;
using static Tesserae.UI;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The modal behind a code editor's <b>Editor Settings</b> context-menu entry: every
    /// <see cref="EditorSettings"/> preference as a labelled control, grouped by what it affects. Save writes
    /// them to <see cref="EditorSettingsStore"/>; <b>Reset to defaults</b> forgets them. It is built from
    /// Tesserae components alone, so it follows the active theme.
    ///
    /// Its text is English. A host that translates its interface sets <see cref="Translate"/> once and every
    /// label, description and button goes through it.
    /// </summary>
    public static class EditorSettingsDialog
    {
        /// <summary>Maps an English label of this dialog (and of the menu entry) to the host's language. Identity when unset.</summary>
        public static Func<string, string> Translate { get; set; }

        internal static string T(string text) => Translate is null ? text : (Translate(text) ?? text);

        /// <summary>
        /// Opens the dialog over <paramref name="current"/> (what the editor is using now). Save hands the edited
        /// copy to <paramref name="onSave"/>; Reset calls <paramref name="onReset"/>. Both close the dialog.
        /// </summary>
        public static void Show(EditorSettings current, Action<EditorSettings> onSave, Action onReset)
        {
            var edited  = current.Clone();
            var readers = new List<Action<EditorSettings>>();

            IComponent Section(string title) => TextBlock(T(title)).SemiBold().PT(12).PB(4);

            IComponent Row(string label, string description, IComponent control)
            {
                var caption = VStack().Gap(2.px()).Grow().Children(
                    TextBlock(T(label)),
                    string.IsNullOrEmpty(description) ? Raw() : TextBlock(T(description)).Small().Secondary());

                return HStack().NoWrap().AlignItemsCenter().Gap(16.px()).PT(4).PB(4).Children(caption, control.W(190.px()).NoShrink());
            }

            IComponent Check(string label, string description, bool value, Action<EditorSettings, bool> write)
            {
                var toggle = Toggle().Checked(value);

                readers.Add(s => write(s, toggle.IsChecked));

                return Row(label, description, toggle);
            }

            IComponent Choice(string label, string description, string value, string[][] options, Action<EditorSettings, string> write)
            {
                var dropdown = Dropdown().Single().Items(options.Select(o => DropdownItem(T(o[1])).SetKey(o[0]).SelectedIf(o[0] == value)).ToArray());

                readers.Add(s =>
                {
                    var selected = dropdown.SelectedItems.FirstOrDefault();

                    if (selected is object) write(s, selected.Key);
                });

                return Row(label, description, dropdown);
            }

            IComponent Number(string label, string description, int value, int min, int max, Action<EditorSettings, int> write)
            {
                var picker = NumberPicker(value).SetMin(min).SetMax(max).SetStep(1);

                readers.Add(s => write(s, Math.Max(min, Math.Min(max, picker.Value))));

                return Row(label, description, picker);
            }

            var content = VStack().Children(
                Section("Appearance"),
                Number("Font size",           "In pixels.",                                        edited.FontSize, 8, 40, (s, v) => s.FontSize = v),
                Choice("Line numbers",        null,                                                edited.LineNumbers,      new[] { new[] { "on", "On" }, new[] { "relative", "Relative to the cursor" }, new[] { "off", "Off" } }, (s, v) => s.LineNumbers = v),
                Check("Word wrap",            "Wrap long lines instead of scrolling sideways.",    edited.WordWrap,         (s, v) => s.WordWrap = v),
                Check("Minimap",              "A zoomed-out overview of the file.",                edited.Minimap,          (s, v) => s.Minimap = v),
                Choice("Show whitespace",     null,                                                edited.RenderWhitespace, new[] { new[] { "none", "Never" }, new[] { "selection", "In the selection" }, new[] { "boundary", "At word boundaries" }, new[] { "all", "Always" } }, (s, v) => s.RenderWhitespace = v),
                Check("Indent guides",        "Vertical lines at each indentation level.",         edited.IndentGuides,     (s, v) => s.IndentGuides = v),
                Check("Colorize bracket pairs", "Matching brackets share a colour.",               edited.BracketPairColorization, (s, v) => s.BracketPairColorization = v),
                Check("Sticky scroll",        "Pin the enclosing scope to the top while scrolling.", edited.StickyScroll,   (s, v) => s.StickyScroll = v),
                Check("Code folding",         "Collapse blocks from the gutter.",                  edited.Folding,          (s, v) => s.Folding = v),
                Check("Font ligatures",       "Join symbol pairs such as => and != into one glyph.", edited.FontLigatures,  (s, v) => s.FontLigatures = v),

                Section("Hints and assistance"),
                Choice("Inline hints",        "Parameter names and inferred types shown inside the code.", edited.InlayHints, new[] { new[] { "off", "Off" }, new[] { "offUnlessPressed", "Only while holding Ctrl+Alt" }, new[] { "on", "Always" } }, (s, v) => s.InlayHints = v),
                Check("Hover documentation",  "Show details when the pointer rests on a word.",    edited.Hover,            (s, v) => s.Hover = v),
                Check("Parameter hints",      "Show the signature while typing the arguments of a call.", edited.ParameterHints, (s, v) => s.ParameterHints = v),
                Check("Suggest as you type",  "Open the suggestion list without asking for it.",   edited.QuickSuggestions, (s, v) => s.QuickSuggestions = v),
                Check("Inline suggestions",   "Offer completions as grey text after the cursor.",  edited.InlineSuggestions, (s, v) => s.InlineSuggestions = v),
                Check("Code lenses",          "Show reference counts above declarations.",         edited.CodeLens,         (s, v) => s.CodeLens = v),

                Section("Editing"),
                Number("Tab size",            "Columns per indentation level.",                    edited.TabSize, 1, 16,   (s, v) => s.TabSize = v),
                Check("Indent with spaces",   "Insert spaces instead of tab characters.",          edited.InsertSpaces,     (s, v) => s.InsertSpaces = v),
                Check("Format on paste",      "Re-indent pasted code to fit its surroundings.",    edited.FormatOnPaste,    (s, v) => s.FormatOnPaste = v),
                Check("Smooth scrolling",     null,                                                edited.SmoothScrolling,  (s, v) => s.SmoothScrolling = v),
                Check("Zoom with Ctrl + wheel", "Change the font size with the mouse wheel.",      edited.MouseWheelZoom,   (s, v) => s.MouseWheelZoom = v));

            Modal modal = null;

            var save = Button(T("Save")).Primary().SetIcon(UIcons.Disk).Id("tssm-settings-save").OnClick(() =>
            {
                foreach (var read in readers) read(edited);

                modal.Hide();
                onSave?.Invoke(edited);
            });

            var cancel = Button(T("Cancel")).Id("tssm-settings-cancel").OnClick(() => modal.Hide());

            var reset = Button(T("Reset to defaults")).Id("tssm-settings-reset").OnClick(() =>
            {
                modal.Hide();
                onReset?.Invoke();
            });

            modal = Modal(HStack().NoWrap().AlignItemsCenter().Gap(8.px()).Children(Icon(UIcons.Settings), TextBlock(T("Editor Settings")).SemiBold()))
               .W(560.px())
               .MaxWidth(95.vw())
               .LightDismiss()
               .ShowCloseButton()
               .SetLeftFooterCommands(reset)
               .SetFooterCommands(cancel, save)
               .Content(content);

            modal.Show();
        }
    }
}
