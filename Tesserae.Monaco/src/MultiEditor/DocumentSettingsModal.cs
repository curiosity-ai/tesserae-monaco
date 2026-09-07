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
    /// A banner says how many settings are waiting whenever any are, since a user who closed the overlay has
    /// only the tab's one dot to go on otherwise.
    /// </summary>
    public sealed class DocumentSettingsModal
    {
        private readonly Modal            _modal;
        private readonly Stack            _host;
        private readonly Banner           _pending;
        private readonly Button           _save;
        private readonly Button           _revert;
        private readonly Func<IComponent> _content;

        private Func<Task<bool>> _onSave;
        private Func<Task>       _onRevert;

        /// <param name="title">The overlay's heading.</param>
        /// <param name="content">
        /// Builds the settings themselves. Called now, and again after a revert - so it has to read the
        /// host's current values rather than close over a snapshot of them.
        /// </param>
        public DocumentSettingsModal(string title, Func<IComponent> content)
        {
            _content = content;

            _pending = Banner().Warning().Compact().SetIcon(UIcons.Disk).Collapse();

            _host = VStack().WS().Grow().MinHeight(0.px()).ScrollY();

            _save = Button("Save").Primary().SetIcon(UIcons.Disk).Disabled().OnClick(() => SaveAsync().FireAndForget());

            _revert = Button("Revert settings").SetIcon(UIcons.Undo).Disabled().Collapse().OnClick(() => RevertAsync().FireAndForget());

            _modal = Modal(HStack().NoWrap().AlignItemsCenter().Gap(8.px()).Children(
                    Icon(UIcons.Settings),
                    TextBlock(title ?? "Settings").SemiBold()))
               .W(720.px())
               .MaxWidth(95.vw())
               .LightDismiss()
               .ShowCloseButton()
               .SetLeftFooterCommands(_revert)
               .SetFooterCommands(Button("Close").OnClick(() => _modal.Hide()), _save)
               .Content(VStack().S().Gap(8.px()).Children(_pending, _host));

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

            if (dirty)
            {
                _pending.SetTitle(changed.Length > 0
                    ? DocumentHeader.ChangedLabel(changed) + " not saved yet"
                    : "The settings have unsaved changes");

                _pending.SetText(changed.Length > 0
                    ? "Waiting to be saved: " + string.Join(", ", changed) + ". Saving the document saves the code and the settings together."
                    : "Saving the document saves the code and the settings together.");

                _pending.Show();
            }
            else
            {
                _pending.Collapse();
            }

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
