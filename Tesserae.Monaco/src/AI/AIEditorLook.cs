using System;
using Transpose;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco
{
    /// <summary>
    /// <c>AI()</c> on a whole editor: Tesserae's AI field look over it, and, with <c>badge: true</c>, the same
    /// <c>AI</c> / <c>Edited</c> badge <c>TextArea.AI(badge: true)</c> shows, in the editor's top-right corner. The badge
    /// follows Tesserae's rule exactly: the text in the editor when the variant was switched on (or when it mounted),
    /// and again after every <c>SetText</c>, is what the model wrote; while the editor still holds it the badge says
    /// <c>AI</c>, once the user changes it it says <c>Edited</c>, and putting the text back returns it to <c>AI</c>.
    /// The component owns one of these across remounts, so the baseline is kept while the editor is torn down.
    /// </summary>
    internal sealed class AIEditorLook
    {
        private readonly HTMLElement   _container;
        private readonly DisposableBag _listeners = new DisposableBag();

        private bool          _strong;
        private bool          _badge;
        private string        _editedText;
        private string        _baseline;
        private Badge         _badgeComponent;
        private HTMLElement   _badgeHost;
        private OverlayWidget _widget;
        private EditorSurface _surface;
        private bool          _edited;

        internal AIEditorLook(HTMLElement container)
        {
            _container = container;
        }

        /// <summary>Whether the text differs from what the model wrote. False without a badge.</summary>
        internal bool IsEdited => _edited;

        internal void Enable(bool strongEffect, bool badge, string editedText, EditorSurface surface)
        {
            AIStyles.Ensure();

            _strong     = strongEffect;
            _badge      = badge;
            _editedText = string.IsNullOrEmpty(editedText) ? "Edited" : editedText;

            _container.classList.add(AIStyles.Editor);
            _container.UpdateClassIf(strongEffect, "tss-ai-strong");

            if (surface is object)
            {
                // Switched on (again) over a live editor: what it holds now is what the model wrote.
                _baseline = surface.Text;
                Attach(surface);
            }
        }

        internal void Disable()
        {
            Detach();
            _container.classList.remove(AIStyles.Editor);
            _container.classList.remove("tss-ai-strong");
            _baseline = null;
        }

        internal void Attach(EditorSurface surface)
        {
            // Never two badges: a second Attach (a re-enable over a live editor) replaces the first.
            Detach();

            _surface = surface;

            if (!_badge || surface is null) return;

            _badgeComponent = AIBadge(strongEffect: _strong);
            _badgeHost      = (HTMLElement)document.createElement("div");
            _badgeHost.className = AIStyles.BadgeHost;
            _badgeHost.appendChild(_badgeComponent.Render());

            _widget = new OverlayWidget("tssm.ai.badge", _badgeHost, OverlayWidgetPosition.TopRightCorner);
            surface.AddOverlayWidget(_widget);

            var editor = surface.Native;
            var model  = editor.getModel();

            // A remount builds the editor from the text it left with, so a baseline from before is kept; the first
            // mount takes the text as it is.
            if (_baseline is null) _baseline = surface.Text;

            if (model is object)
            {
                _listeners.Add(model.onDidChangeContent(e =>
                {
                    // setValue is the application handing over a new answer: that is now what the model wrote.
                    if (e.isFlush) _baseline = model.getValue();
                    Apply();
                }));
            }

            Apply();
        }

        internal void Detach()
        {
            _listeners.DisposeAll();

            if (_surface is object && _widget is object) _surface.RemoveOverlayWidget(_widget);

            _widget         = null;
            _badgeHost      = null;
            _badgeComponent = null;
            _surface        = null;
        }

        private void Apply()
        {
            if (_badgeComponent is null || _surface is null) return;

            var text = _surface.Text;

            _edited = text != _baseline;

            _badgeComponent.SetText(_edited ? _editedText : "AI");
            _badgeComponent.Outline(_edited);

            _container.UpdateClassIf(_edited, "tss-ai-edited");
            _badgeHost.setAttribute("title", _edited ? "AI-generated, then edited" : "AI-generated");

            // An empty editor holds nothing a model wrote, so there is nothing to badge.
            _badgeHost.style.display = string.IsNullOrEmpty(text) ? "none" : "";
        }
    }
}
