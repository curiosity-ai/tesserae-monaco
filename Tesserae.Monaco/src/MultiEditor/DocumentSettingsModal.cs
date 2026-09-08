using System;
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
    /// It is deliberately almost nothing: a name, the close button in its corner, whatever the host built,
    /// and Save. Everything else that could go in here belongs to the host's form instead - a Revert button,
    /// a validation summary, sections, help - which is what keeps it usable for settings this package has
    /// never heard of.
    ///
    /// It is an <b>editing surface, not a transaction</b>. Editing a setting makes the <i>document</i>
    /// unsaved, exactly as typing in its editor does, so there is no Cancel that discards and no second save
    /// of its own: closing the overlay keeps the pending edits, and Save is the document's own save - the
    /// same one Ctrl+S runs, which persists the code and the settings together. The overlay therefore knows
    /// nothing about dirty state; where that is shown is the settings button on the strip.
    /// </summary>
    public sealed class DocumentSettingsModal
    {
        private readonly Modal  _modal;
        private readonly Button _save;

        private Func<Task<bool>> _onSave;

        /// <param name="title">The overlay's heading.</param>
        /// <param name="content">The settings themselves, built by the host.</param>
        /// <param name="saveButtonText">The Save button's label; drawn as its icon alone when unset.</param>
        public DocumentSettingsModal(string title, IComponent content, string saveButtonText = null)
        {
            _save = Button(saveButtonText ?? "").Primary().SetIcon(UIcons.Disk).OnClick(() => SaveAsync().FireAndForget());

            _modal = Modal(HStack().NoWrap().AlignItemsCenter().Gap(8.px()).Children(
                    Icon(UIcons.Settings),
                    TextBlock(title ?? "").SemiBold()))
               .W(720.px())
               .MaxWidth(95.vw())
               .LightDismiss()
               .ShowCloseButton()
               .SetFooterCommands(_save)
               .Content(content ?? Raw());
        }

        /// <summary>The overlay itself, for sizing it differently, restyling it, or hooking its show and hide.</summary>
        public Modal Modal => _modal;

        /// <summary>The Save button, for relabelling or disabling it.</summary>
        public Button SaveButton => _save;

        /// <summary>Whether it is on screen.</summary>
        public bool IsVisible => _modal.IsVisible;

        /// <summary>
        /// What Save does - the document's save, which persists the code and the settings together. The
        /// overlay closes when it reports success. Without one the button does nothing.
        /// </summary>
        public DocumentSettingsModal OnSave(Func<Task<bool>> save)
        {
            _onSave = save;

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
    }
}
