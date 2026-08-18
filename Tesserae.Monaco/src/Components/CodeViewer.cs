using System;
using Transpose;
using Tesserae;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// A read-only Monaco surface for displaying code: syntax highlighting, selection and copying, but
    /// none of the editing affordances (no suggest widget, no error squiggles, no formatting).
    ///
    /// Use <see cref="CodeEditor"/> when the user is meant to type, and
    /// <see cref="MonacoEditor.Colorize"/> when a static, non-interactive snippet is all that is wanted -
    /// that needs no editor instance at all. Create one with <see cref="MonacoEditor.Viewer(bool)"/>.
    ///
    /// Decorations, widgets, events, actions and the typed options come from
    /// <see cref="MonacoTextComponent{T}"/>, so a viewer can still highlight ranges and be clicked
    /// through - it just has no language intelligence wired to it.
    /// </summary>
    [Transpose.Name("tssm.CodeViewer")]
    public sealed class CodeViewer : MonacoTextComponent<CodeViewer>
    {
        private readonly bool                  _autoHeight;
        private          bool                  _editable;
        private          Action<EditorOptions> _configureOptions;

        protected override CodeViewer Self => this;

        internal CodeViewer(bool autoHeight)
        {
            _autoHeight = autoHeight;
        }

        /// <summary>
        /// Allows typing. A viewer that is editable is still just a viewer - it has no completion, hover
        /// or diagnostics wiring; reach for <see cref="CodeEditor"/> for that.
        /// </summary>
        public CodeViewer Editable(bool editable = true)
        {
            _editable = editable;

            ReadOnly(!editable);
            ContextMenu(editable);

            return this;
        }

        /// <summary>Whether typing is allowed.</summary>
        public bool IsEditable => _editable;

        /// <summary>
        /// Adjusts the Monaco construction options before the viewer is created - the escape hatch for
        /// options the typed setters don't cover. Applied after them, so it always wins.
        /// </summary>
        public CodeViewer Options(Action<EditorOptions> configureOptions)
        {
            _configureOptions = configureOptions;

            return this;
        }

        protected override IEditor Create(HTMLElement container)
        {
            var options = BuildBaseOptions(InitialLanguage, InitialText, !_editable, InitialWordWrap, _autoHeight);

            // A viewer is for reading: no suggest widget, and no gutter noise it can't act on.
            options.quickSuggestions     = false;
            options.occurrencesHighlight = "off";
            options.renderLineHighlight  = "none";
            options.contextmenu          = _editable;

            FinishOptions(options, OptionSetters, _configureOptions);

            return MonacoApi.editor.create(container, options);
        }

        protected override void AfterCreate()
        {
            BindSurface();

            if (_autoHeight) EnableAutoHeight();

            RaiseRendered();
        }

        protected override void BeforeDispose()
        {
            UnbindSurface();
        }
    }
}
