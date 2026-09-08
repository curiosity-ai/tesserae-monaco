using System;
using System.Linq;
using System.Threading.Tasks;
using Tesserae;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The overlay a document's settings are edited in - what <c>editor.ShowSettings(id)</c> opens and what
    /// the settings button in the <see cref="DocumentHeader"/> reaches.
    ///
    /// It is an <b>editing surface, not a transaction</b>, and that is the decision the rest of it follows
    /// from. Editing a setting makes the <i>document</i> unsaved, exactly as typing in its editor does, so
    /// there is no Cancel that discards and no second save of its own: closing the overlay keeps the pending
    /// edits and leaves the tab's marker showing, Save is the document's own save - the same one Ctrl+S runs -
    /// and Revert, when the host offers one, is how the edits are given up deliberately. Two saves that could
    /// disagree about what "saved" means is the thing this shape exists to avoid.
    ///
    /// A line in the footer, beside Save, says how many settings are waiting - a user who closed the overlay
    /// has only the tab's one dot to go on otherwise. It lives there rather than above the fields because a
    /// notice that comes and goes in the content flow shoves every field down the moment something changes,
    /// which is the one place a settings form must stay still: the pointer is on the control that just moved.
    /// The footer's middle slot is a fixed-height, no-wrap, clipping row, so the line can appear, change and
    /// go without moving anything, and it keeps its height whatever it says - so even its own row never
    /// reflows.
    ///
    /// Every word comes from the <see cref="DocumentSettingsText"/> it is given; the overlay ships no copy of
    /// its own, so a button with no label supplied is drawn as its icon alone.
    /// </summary>
    public sealed class DocumentSettingsModal
    {
        private readonly Modal            _modal;
        private readonly Stack            _host;
        private readonly Stack            _status;
        private readonly Icon             _statusIcon;
        private readonly TextBlock        _statusText;
        private readonly Button           _save;
        private readonly Button           _revert;
        private readonly Func<IComponent>      _content;
        private readonly DocumentSettingsText  _text;

        private Func<Task<bool>> _onSave;
        private Func<Task>       _onRevert;

        /// <param name="title">The overlay's heading.</param>
        /// <param name="content">
        /// Builds the settings themselves. Called now, and again after a revert - so it has to read the
        /// host's current values rather than close over a snapshot of them.
        /// </param>
        /// <param name="text">Where every label and line comes from. An empty one says nothing at all.</param>
        public DocumentSettingsModal(string title, Func<IComponent> content, DocumentSettingsText text = null)
        {
            _content = content;
            _text    = text ?? new DocumentSettingsText();

            _statusIcon = Icon(UIcons.Disk, Theme.Secondary.Foreground);
            _statusText = TextBlock("").Small().NoWrap().Ellipsis();

            // The native title rather than a tooltip: it is rewritten on every state change, and a Tippy
            // instance is built once.
            _status = HStack().NoWrap().AlignItemsCenter().Gap(6.px()).MinWidth(0.px()).PL(8).PR(8)
               .Children(_statusIcon, _statusText);

            _host = VStack().S().ScrollY();

            _save = Button(DocumentSettingsText.Of(_text.SaveButton)).Primary().SetIcon(UIcons.Disk).Disabled().OnClick(() => SaveAsync().FireAndForget());

            _revert = Button(DocumentSettingsText.Of(_text.RevertButton)).SetIcon(UIcons.Undo).Disabled().Collapse().OnClick(() => RevertAsync().FireAndForget());

            _modal = Modal(HStack().NoWrap().AlignItemsCenter().Gap(8.px()).Children(
                    Icon(UIcons.Settings),
                    TextBlock(title ?? "").SemiBold()))
               .W(720.px())
               .MaxWidth(95.vw())
               .LightDismiss()
               .ShowCloseButton()
               .SetLeftFooterCommands(_revert)
               .SetFooter(_status)
               .SetFooterCommands(Button(DocumentSettingsText.Of(_text.CloseButton)).SetIcon(UIcons.CrossSmall).OnClick(() => _modal.Hide()), _save)
               .Content(_host);

            Rebuild();
        }

        /// <summary>The overlay itself, for sizing it differently or hooking its show and hide.</summary>
        public Modal Modal => _modal;

        /// <summary>Whether it is on screen.</summary>
        public bool IsVisible => _modal.IsVisible;

        /// <summary>
        /// What Save does - the document's save, which persists the code and the settings together. Without
        /// one the button stays disabled.
        /// </summary>
        public DocumentSettingsModal OnSave(Func<Task<bool>> save)
        {
            _onSave = save;

            return this;
        }

        /// <summary>
        /// What Revert does - puts the host's settings back to what was last saved. Without one the button is
        /// not shown at all, since an overlay that cannot revert should not pretend it can.
        /// </summary>
        public DocumentSettingsModal OnRevert(Func<Task> revert)
        {
            _onRevert = revert;

            if (revert is object) _revert.Show(); else _revert.Collapse();

            return this;
        }

        /// <summary>
        /// Tells the overlay where the document stands: which settings are unsaved, and whether saving is
        /// possible at all. Called every time the shell's own state moves, so the banner and the buttons
        /// stay right while it is open.
        /// </summary>
        public DocumentSettingsModal SetState(bool settingsDirty, string[] changedSettings, bool canSave)
        {
            var changed = changedSettings ?? new string[0];
            var dirty   = settingsDirty || changed.Length > 0;

            _save.IsEnabled   = canSave && _onSave is object;
            _revert.IsEnabled = dirty;

            // The brand colour, the same one the header's settings button, the accented chip and the tab's
            // own dot use for "unsaved" - not a warning tone, which would say something is wrong.
            var colour = dirty ? Theme.Primary.Background : Theme.Secondary.Foreground;

            _statusText.Text   = dirty ? DocumentSettingsText.Of(_text.PendingNotice, changed) : DocumentSettingsText.Of(_text.SaveModel);
            _statusText.Weight = dirty ? TextWeight.SemiBold : TextWeight.Regular;

            _status.Render().title = dirty
                ? DocumentSettingsText.Of(_text.PendingTooltip, changed)
                : DocumentSettingsText.Of(_text.SaveModelTooltip);

            _statusText.Foreground(colour);
            _statusIcon.Foreground(colour);

            return this;
        }

        /// <summary>Builds the settings afresh from the factory - what a revert needs so the fields show the restored values.</summary>
        public DocumentSettingsModal Rebuild()
        {
            _host.Clear();

            var content = _content?.Invoke();

            if (content is object) _host.Add(content);

            return this;
        }

        /// <summary>Opens it.</summary>
        public DocumentSettingsModal Show()
        {
            _modal.Show();

            return this;
        }

        /// <summary>Closes it. The pending edits stay pending - see the class summary.</summary>
        public DocumentSettingsModal Hide()
        {
            _modal.Hide();

            return this;
        }

        private async Task SaveAsync()
        {
            if (_onSave is null) return;

            if (await _onSave()) _modal.Hide();
        }

        private async Task RevertAsync()
        {
            if (_onRevert is null) return;

            await _onRevert();

            Rebuild();
        }
    }
}
