using System;
using Transpose;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco
{
    /// <summary>Where a content widget prefers to sit relative to its position, matching Monaco's <c>ContentWidgetPositionPreference</c>.</summary>
    [Enum(Emit.Value)]
    public enum ContentWidgetPosition
    {
        /// <summary>Exactly at the position, overlapping the text.</summary>
        Exact = 0,
        Above = 1,
        Below = 2
    }

    /// <summary>Where an overlay widget sits in the editor, matching Monaco's <c>OverlayWidgetPositionPreference</c>.</summary>
    [Enum(Emit.Value)]
    public enum OverlayWidgetPosition
    {
        TopRightCorner    = 0,
        BottomRightCorner = 1,
        TopCenter         = 2
    }

    /// <summary>Where a content widget should be placed, matching Monaco's <c>IContentWidgetPosition</c>.</summary>
    [ObjectLiteral]
    public class ContentWidgetPlacement
    {
        public Position                position;
        public TextRange               range;
        public ContentWidgetPosition[] preference;
    }

    /// <summary>Where an overlay widget should be placed, matching Monaco's <c>IOverlayWidgetPosition</c>.</summary>
    [ObjectLiteral]
    public class OverlayWidgetPlacement
    {
        public OverlayWidgetPosition preference;
    }

    /// <summary>
    /// Monaco's <c>IContentWidget</c>: three functions and a flag. Monaco calls the functions on every
    /// render, so <see cref="ContentWidget"/> builds one of these with delegates that read its live
    /// properties.
    /// </summary>
    [ObjectLiteral]
    public class ContentWidgetDescriptor
    {
        public bool                          allowEditorOverflow;
        public Func<string>                  getId;
        public Func<HTMLElement>             getDomNode;
        public Func<ContentWidgetPlacement>  getPosition;
    }

    /// <summary>Monaco's <c>IOverlayWidget</c>.</summary>
    [ObjectLiteral]
    public class OverlayWidgetDescriptor
    {
        public Func<string>                  getId;
        public Func<HTMLElement>             getDomNode;
        public Func<OverlayWidgetPlacement>  getPosition;
    }

    /// <summary>Monaco's <c>IViewZone</c> - the band of space itself, as the accessor wants it.</summary>
    [ObjectLiteral]
    public class ViewZoneDescriptor
    {
        public int         afterLineNumber;
        public int         afterColumn;
        public int         heightInLines;
        public int         heightInPx;
        public HTMLElement domNode;
        public HTMLElement marginDomNode;
    }

    /// <summary>
    /// An element Monaco positions inside the text, anchored to a document position and scrolling with
    /// it. This is the mechanism behind an inline hint, a small inline toolbar, or a "click to expand"
    /// affordance.
    ///
    /// The element is the host's to build and style. After changing <see cref="Position"/>, call
    /// <c>LayoutContentWidget</c> on the editor so Monaco re-places it.
    /// </summary>
    public sealed class ContentWidget
    {
        /// <summary>Identity within one editor. Monaco keys its widget registry on this.</summary>
        public string Id { get; set; }

        /// <summary>The element to place.</summary>
        public HTMLElement DomNode { get; set; }

        /// <summary>The document position to anchor to. Null hides the widget.</summary>
        public Position Position { get; set; }

        /// <summary>Preferred placements, tried in order. Empty means <see cref="ContentWidgetPosition.Exact"/>.</summary>
        public ContentWidgetPosition[] Preferences { get; set; }

        /// <summary>
        /// Let the widget escape the editor's bounds - pair it with the shared overflow host, for the
        /// same reason suggest and hover popups need one, when the editor sits inside a clipping ancestor.
        /// </summary>
        public bool AllowEditorOverflow { get; set; }

        public ContentWidget() { }

        public ContentWidget(string id, HTMLElement domNode, Position position, ContentWidgetPosition preference = ContentWidgetPosition.Above)
        {
            Id          = id;
            DomNode     = domNode;
            Position    = position;
            Preferences = new[] { preference };
        }

        private static readonly ContentWidgetPosition[] EXACT = { ContentWidgetPosition.Exact };

        // Cached: addContentWidget, layoutContentWidget and removeContentWidget all have to be called
        // with the same object identity. The delegates read the live properties rather than a snapshot,
        // because Monaco calls them again on every render.
        private ContentWidgetDescriptor _descriptor;

        internal ContentWidgetDescriptor Descriptor()
        {
            return _descriptor ?? (_descriptor = new ContentWidgetDescriptor
            {
                allowEditorOverflow = AllowEditorOverflow,
                getId               = () => Id,
                getDomNode          = () => DomNode,
                getPosition         = () => Position is null ? null : new ContentWidgetPlacement
                {
                    position   = Position,
                    preference = Preferences is object && Preferences.Length > 0 ? Preferences : EXACT
                }
            });
        }
    }

    /// <summary>
    /// An element pinned to a corner of the editor, outside the scrolling text. Use it for a status
    /// badge or a control that should not move as the user scrolls.
    /// </summary>
    public sealed class OverlayWidget
    {
        public string                Id         { get; set; }
        public HTMLElement           DomNode    { get; set; }
        public OverlayWidgetPosition Preference { get; set; } = OverlayWidgetPosition.TopRightCorner;

        public OverlayWidget() { }

        public OverlayWidget(string id, HTMLElement domNode, OverlayWidgetPosition preference = OverlayWidgetPosition.TopRightCorner)
        {
            Id         = id;
            DomNode    = domNode;
            Preference = preference;
        }

        private OverlayWidgetDescriptor _descriptor;

        internal OverlayWidgetDescriptor Descriptor()
        {
            return _descriptor ?? (_descriptor = new OverlayWidgetDescriptor
            {
                getId       = () => Id,
                getDomNode  = () => DomNode,
                getPosition = () => new OverlayWidgetPlacement { preference = Preference }
            });
        }
    }

    /// <summary>
    /// A horizontal band of blank space between two lines, into which the host renders its own element.
    /// This is how VS Code shows inline blame, a review-comment thread, or a collapsed-region
    /// placeholder: the editor reflows around it, so it is not an overlay.
    ///
    /// Give either <see cref="HeightInLines"/> or <see cref="HeightInPx"/>; leaving both at zero lets
    /// Monaco size the zone from the element itself.
    /// </summary>
    public sealed class ViewZone
    {
        /// <summary>The zone opens below this line. Zero puts it above the first line.</summary>
        public int AfterLineNumber { get; set; }

        /// <summary>Optionally split at a column rather than taking the whole line.</summary>
        public int AfterColumn { get; set; }

        /// <summary>Height in editor lines. Ignored when zero.</summary>
        public int HeightInLines { get; set; }

        /// <summary>Height in pixels. Ignored when zero.</summary>
        public int HeightInPx { get; set; }

        /// <summary>The element rendered inside the band.</summary>
        public HTMLElement DomNode { get; set; }

        /// <summary>
        /// Put the element in the margin rather than the text area, so its content lines up with the
        /// gutter. The text side still gets an empty node, which is what Monaco expects.
        /// </summary>
        public bool MarginDomNodeOnly { get; set; }

        public ViewZone() { }

        public ViewZone(int afterLineNumber, HTMLElement domNode, int heightInLines = 1)
        {
            AfterLineNumber = afterLineNumber;
            DomNode         = domNode;
            HeightInLines   = heightInLines;
        }

        /// <summary>Monaco's own id for the zone once added, or null. Needed to remove it again.</summary>
        public string ZoneId { get; internal set; }

        internal ViewZoneDescriptor Descriptor()
        {
            var node = DomNode ?? DIV();

            var descriptor = new ViewZoneDescriptor { afterLineNumber = AfterLineNumber };

            if (MarginDomNodeOnly)
            {
                descriptor.marginDomNode = node;
                descriptor.domNode       = DIV();
            }
            else
            {
                descriptor.domNode = node;
            }

            // Only assigned when set: an [ObjectLiteral] emits just the fields it was given, and a zero
            // height is not the same as no height.
            if (AfterColumn   != 0) descriptor.afterColumn   = AfterColumn;
            if (HeightInLines != 0) descriptor.heightInLines = HeightInLines;
            if (HeightInPx    != 0) descriptor.heightInPx    = HeightInPx;

            return descriptor;
        }
    }
}
