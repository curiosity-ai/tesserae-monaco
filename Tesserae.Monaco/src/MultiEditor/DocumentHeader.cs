using System;
using System.Collections.Generic;
using System.Linq;
using Tesserae;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// One setting worth reading without opening the settings overlay - the method and the path of an
    /// endpoint, the schedule of a task. <see cref="DocumentHeader"/> draws these as chips beside the
    /// settings button, and accents the ones the host reports as changed.
    /// </summary>
    public sealed class SettingSummary
    {
        /// <param name="label">The name shown before the value; may be empty for a value that speaks for itself.</param>
        /// <param name="value">The value shown.</param>
        /// <param name="name">
        /// The setting's own name, matched against the changed set so the chip can be accented. Defaults to
        /// <paramref name="label"/>, which is right whenever the label <i>is</i> the setting's name.
        /// </param>
        public SettingSummary(string label, string value, string name = null)
        {
            Label = label;
            Value = value;
            Name  = name;
        }

        /// <summary>The name shown before the value.</summary>
        public string Label { get; set; }

        /// <summary>The value shown.</summary>
        public string Value { get; set; }

        /// <summary>What <see cref="MultiEditor.MarkSettingsDirty"/> calls this setting; <see cref="Label"/> when unset.</summary>
        public string Name { get; set; }

        /// <summary>An icon before the label. None when unset.</summary>
        public UIcons? Icon { get; set; }

        /// <summary>Hover text for the chip. None when unset.</summary>
        public string Tooltip { get; set; }

        internal string Key => string.IsNullOrEmpty(Name) ? Label : Name;
    }

    /// <summary>
    /// The strip above a document's editor: the settings worth seeing at a glance, and the button that
    /// opens the rest. It is the always-visible home for a document's settings - a tab title has room for
    /// an icon, a name and its unsaved-changes marker and nothing else, and a menu nobody opens is not an
    /// affordance.
    ///
    /// It also carries the answer to "what is unsaved?": the tab's marker is one dot for the whole
    /// document, so when settings change it is this button that says <i>settings</i>, by going from a flat
    /// button to a filled brand-coloured one - while a changed chip shows the pending value in the accent
    /// colour.
    ///
    /// Every word it says comes from the <see cref="DocumentSettingsText"/> it is given; it ships no copy
    /// of its own, so a button with no label supplied is drawn as its gear alone.
    ///
    /// <see cref="MultiEditor"/> builds one for every document that has <see cref="EditorDocument.Settings"/>
    /// and keeps it in step. A host putting a bare <see cref="CodeEditor"/> on a page can compose one itself.
    /// </summary>
    public sealed class DocumentHeader : IComponent
    {
        private readonly Stack                _chips;
        private readonly Stack                _root;
        private readonly Button               _button;
        private readonly DocumentSettingsText _text;
        private          Action               _onOpen;
        private          bool                 _dirty;
        private          string[]             _changed = new string[0];

        private IEnumerable<SettingSummary> _summary;

        /// <param name="text">Where every label and tooltip comes from. An empty one says nothing at all.</param>
        public DocumentHeader(DocumentSettingsText text = null)
        {
            _text = text ?? new DocumentSettingsText();

            // A default Button is already flat - transparent, no shadow, with the themed hover - and turning
            // IsPrimary on fills it in the brand colour with a legible foreground. NoBackground is
            // deliberately NOT used: tss-btn-primary's background rule carries a :not(.tss-disabled) and so
            // out-specifies tss-btn-nobg's transparent, while the label takes the brand colour - which is
            // brand text on a brand fill, i.e. an invisible label in a coloured block.
            _button = Button().SetIcon(UIcons.Settings).OnClick(() => _onOpen?.Invoke());

            _chips = HStack().NoWrap().AlignItemsCenter().Gap(14.px()).Grow().MinWidth(0.px()).Style(s => s.overflow = "hidden");

            // A stable hook for a host that wants to restyle the strip, and for a test that wants to find it.
            _root = HStack().Class("tssm-document-header").WS().NoShrink().NoWrap().AlignItemsCenter().Gap(8.px()).PL(12).PR(8)
               .Background(Theme.Secondary.Background)
               .Style(s => s.borderBottom = "1px solid " + Theme.Default.Border)
               .Children(_chips, _button);

            ApplyState();
        }

        /// <summary>The button, for restyling it or reading its state.</summary>
        public Button Button => _button;

        /// <summary>What the button does. Handlers replace one another.</summary>
        public DocumentHeader OnOpenSettings(Action handler)
        {
            _onOpen = handler;

            return this;
        }

        /// <summary>
        /// The chips shown to the left of the button. Null or empty leaves the strip to the button alone,
        /// which is still worth showing - it is what says the document has settings.
        /// </summary>
        public DocumentHeader Summary(IEnumerable<SettingSummary> summary)
        {
            _summary = summary;

            RebuildChips();

            return this;
        }

        /// <summary>
        /// Whether the settings differ from what was last saved, and which ones. The names are what the
        /// host's <see cref="DocumentSettingsText.ChangedButton"/> formats, and a chip whose setting is
        /// named is accented - a host that does not track them can report <paramref name="dirty"/> alone.
        /// Names nothing in the summary knows about still reach the label.
        /// </summary>
        public DocumentHeader Changed(bool dirty, params string[] changedSettings)
        {
            _dirty   = dirty;
            _changed = changedSettings ?? new string[0];

            ApplyState();
            RebuildChips();

            return this;
        }

        /// <summary>The names last given to <see cref="Changed"/>.</summary>
        public string[] ChangedSettings => _changed;

        /// <summary>Whether the settings were last reported as unsaved.</summary>
        public bool IsDirty => _dirty;

        /// <summary>The root element.</summary>
        public HTMLElement Render() => _root.Render();

        private void ApplyState()
        {
            var dirty = _dirty || _changed.Length > 0;

            _button.Text      = dirty ? DocumentSettingsText.Of(_text.ChangedButton, _changed) : DocumentSettingsText.Of(_text.SettingsButton);
            _button.IsPrimary = dirty;

            _button.SetTitle(dirty
                ? DocumentSettingsText.Of(_text.ChangedTooltip, _changed)
                : DocumentSettingsText.Of(_text.SettingsTooltip));
        }

        private void RebuildChips()
        {
            _chips.Clear();

            if (_summary is null) return;

            foreach (var setting in _summary)
            {
                if (setting is null) continue;

                _chips.Add(Chip(setting, _changed.Contains(setting.Key)));
            }
        }

        private static IComponent Chip(SettingSummary setting, bool changed)
        {
            var accent = changed ? Theme.Primary.Background : null;
            var parts  = new List<IComponent>(3);

            if (setting.Icon.HasValue) parts.Add(Icon(setting.Icon.Value, accent ?? Theme.Secondary.Foreground));

            if (!string.IsNullOrEmpty(setting.Label)) parts.Add(TextBlock(setting.Label).Small().Secondary().NoWrap());

            var value = TextBlock(setting.Value ?? "").Small().SemiBold().NoWrap().Ellipsis().MaxWidth(260.px());

            if (accent is object) value.Foreground(accent);

            parts.Add(value);

            var chip = HStack().NoWrap().AlignItemsCenter().Gap(4.px()).Children(parts.ToArray());

            return string.IsNullOrEmpty(setting.Tooltip) ? chip : chip.Tooltip(setting.Tooltip);
        }
    }
}
