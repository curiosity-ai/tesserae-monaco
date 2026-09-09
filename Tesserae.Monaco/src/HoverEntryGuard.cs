using System;
using Transpose;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Keeps a hover tooltip open while the pointer travels from the word into the tooltip.
    ///
    /// Every editor here renders its popups into the shared body-mounted overflow host
    /// (<c>fixedOverflowWidgets</c>), so the hover widget is not a descendant of the editor's DOM. That
    /// has one consequence Monaco does not account for: moving the pointer onto the tooltip fires
    /// <c>mouseleave</c> on the editor, and Monaco's hover controller answers a leave by hiding the hover
    /// unless the pointer is inside the widget's rectangle <b>inset by 3px</b> - a margin left for the
    /// widget's resize sashes. A pointer entering from the word crosses that 3px band first, so at any
    /// speed slower than a flick the first event inside the tooltip is the one that dismisses it.
    /// Measured: the hover went at the first pixel inside the widget's bottom edge, every time.
    ///
    /// Inside Monaco's own layout the widget is part of the editor, so entering it is a mouse <i>move</i>
    /// rather than a leave, and a move while a sticky hover is showing gets the <c>hidingDelay</c> grace
    /// period - long enough for the pointer to clear the band. This restores that grace where the layout
    /// cannot: the controller has a public <c>shouldKeepOpenOnEditorMouseMoveOrLeave</c> for exactly
    /// this - its own features hold a hover open with it - and the guard sets it as the pointer enters
    /// the widget, and clears it as the pointer leaves the widget again.
    ///
    /// Two details carry it. The flag has to be set <i>before</i> Monaco's own <c>mouseleave</c>
    /// listener runs, which is why it is set from a <b>capture-phase</b> listener on the editor's
    /// container: capture on an ancestor precedes every listener on the element itself. And the leave
    /// Monaco skips is also the one that would have cancelled the grace-period scheduler an earlier move
    /// armed; left armed, it fires 300ms later against a stale position and can hide the hover the
    /// pointer is now over, so the scheduler is cancelled here in its place.
    ///
    /// Nothing else changes: leaving the widget outward still hides through the widget's own
    /// <c>mouseleave</c>, and leaving it back into the editor hands over to the ordinary move handling,
    /// with the editor's first mouse move clearing the flag as a safety net should the widget vanish
    /// under the pointer.
    /// </summary>
    internal static class HoverEntryGuard
    {
        private const string CONTENT_HOVER_CONTROLLER_ID = "editor.contrib.contentHover";

        // The content hover's resizable root - the node Monaco measures the pointer against.
        private const string HOVER_WIDGET_SELECTOR = ".monaco-resizable-hover";

        internal static void Install(IStandaloneCodeEditor editor, DisposableBag disposables)
        {
            var container = editor?.getDomNode();

            if (container is null) return;

            HTMLElement guardedWidget = null;

            Action<Event> onWidgetLeave = _ => Release(editor);

            Action<Event> onEditorLeave = e =>
            {
                var entered = e.As<MouseEvent>().relatedTarget.As<HTMLElement>();
                var widget  = entered?.closest(HOVER_WIDGET_SELECTOR).As<HTMLElement>();

                if (widget is null) return;

                var controller = Controller(editor);

                if (controller is null) return;

                controller.shouldKeepOpenOnEditorMouseMoveOrLeave = true;
                controller._reactToEditorMouseMoveRunner?.cancel();

                // One widget per editor, so this attaches once; the check is for a widget rebuilt after a dispose.
                if (widget != guardedWidget)
                {
                    guardedWidget?.removeEventListener("mouseleave", onWidgetLeave);
                    widget.addEventListener("mouseleave", onWidgetLeave);
                    guardedWidget = widget;
                }
            };

            container.addEventListener("mouseleave", onEditorLeave, true);

            disposables.Add(editor.onMouseMove(_ => Release(editor)));

            disposables.Add(() =>
            {
                container.removeEventListener("mouseleave", onEditorLeave, true);
                guardedWidget?.removeEventListener("mouseleave", onWidgetLeave);
                guardedWidget = null;
            });
        }

        private static void Release(IStandaloneCodeEditor editor)
        {
            var controller = Controller(editor);

            if (controller is null) return;

            controller.shouldKeepOpenOnEditorMouseMoveOrLeave = false;
        }

        // A direct cast, never `as`: an [External] interface has no metadata for a runtime type test.
        private static IContentHoverController Controller(IStandaloneCodeEditor editor)
            => (IContentHoverController)editor.getContribution(CONTENT_HOVER_CONTROLLER_ID);
    }
}
