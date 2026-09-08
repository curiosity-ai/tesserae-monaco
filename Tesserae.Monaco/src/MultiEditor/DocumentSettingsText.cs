using System;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Every word the settings surface says, supplied by the host. The package ships the scaffold - the
    /// strip, the overlay, the dirty plumbing - and none of the copy: an application that translates its
    /// interface cannot use English baked into a component, and what to call a "setting" or how to phrase
    /// "not saved yet" is the host's voice, not the toolkit's.
    ///
    /// Nothing here has a default. A member left null is simply not said: a button with no label is drawn
    /// as its icon alone, and an absent tooltip is no tooltip. So a host can supply as little or as much as
    /// it likes, and no fallback English can leak into a translated interface.
    ///
    /// Hand it to the shell once with <see cref="MultiEditor.SettingsText"/>.
    /// </summary>
    public sealed class DocumentSettingsText
    {
        /// <summary>The settings button's label while the settings match what was saved.</summary>
        public string SettingsButton { get; set; }

        /// <summary>That button's hover text while the settings match what was saved.</summary>
        public string SettingsTooltip { get; set; }

        /// <summary>
        /// The button's label once the settings are unsaved, given the changed names - which may be empty
        /// when the host reported dirty without naming them.
        /// </summary>
        public Func<string[], string> ChangedButton { get; set; }

        /// <summary>The button's hover text once the settings are unsaved.</summary>
        public Func<string[], string> ChangedTooltip { get; set; }

        /// <summary>The overlay's heading, given the document's title. <see cref="EditorDocument.SettingsTitle"/> wins over this.</summary>
        public Func<string, string> Title { get; set; }

        /// <summary>The overlay's Save button - the document's own save, which persists the code and the settings together.</summary>
        public string SaveButton { get; set; }

        /// <summary>
        /// A dirty tab's hover text: <paramref name="codeChanged"/> says whether the body changed as well,
        /// and the names are the changed settings - empty for a document whose code alone is unsaved. Null
        /// leaves the tab showing its status message, or its id.
        /// </summary>
        public Func<bool, string[], string> TabTooltip { get; set; }

        /// <summary>
        /// The line the close prompt adds under its question, saying which half is unsaved - same arguments
        /// as <see cref="TabTooltip"/>. Null adds no line, leaving the prompt as it is for a document with
        /// no settings.
        /// </summary>
        public Func<bool, string[], string> ClosePrompt { get; set; }

        /// <summary>The palette section the documents with settings are listed under. No section when unset.</summary>
        public string PaletteSection { get; set; }

        /// <summary>The subtitle on each of those palette entries. None when unset.</summary>
        public string PaletteSubtitle { get; set; }

        internal static string Of(string text) => text ?? "";

        internal static string Of(Func<string[], string> format, string[] names) => format is null ? "" : format(names ?? new string[0]) ?? "";

        internal static string Of(Func<string, string> format, string argument) => format is null ? "" : format(argument) ?? "";

        internal static string Of(Func<bool, string[], string> format, bool flag, string[] names) => format is null ? null : format(flag, names ?? new string[0]);
    }
}
