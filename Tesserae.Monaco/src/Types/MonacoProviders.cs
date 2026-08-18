using System;
using System.Threading.Tasks;
using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The provider objects handed to <c>monaco.languages.register*Provider</c>. Each is an
    /// <c>[ObjectLiteral]</c> whose delegate fields become the plain JavaScript functions Monaco
    /// calls, so a provider is written as ordinary C# rather than as a script string.
    ///
    /// The <c>provide*</c> results are typed as <see cref="object"/> on purpose: Monaco's own
    /// <c>ProviderResult&lt;T&gt;</c> is "a T, null, or a thenable of either", so a provider may
    /// hand back a value directly or an <see cref="IPromise"/>. Use
    /// <see cref="MonacoEditor.AsPromise"/> to turn a <see cref="Task"/> into the latter.
    /// </summary>
    [ObjectLiteral]
    public class CompletionItemProvider
    {
        /// <summary>
        /// Characters that pop the suggest widget in addition to normal word characters. Monaco
        /// only auto-triggers on word characters otherwise.
        /// </summary>
        public string[] triggerCharacters;

        /// <summary>
        /// Monaco calls this as <c>(model, position, context, token)</c>; a C# delegate that
        /// declares fewer parameters simply ignores the rest, as any JavaScript function would.
        /// </summary>
        public Func<ITextModel, Position, object> provideCompletionItems;

        /// <summary>
        /// Fills in the expensive parts of an item once it is highlighted. Monaco's signature is
        /// <c>(item, token)</c> - the model and position are <b>not</b> passed here.
        /// </summary>
        public Func<CompletionItem, ICancellationToken, object> resolveCompletionItem;
    }

    /// <summary>A hover provider, as passed to <c>monaco.languages.registerHoverProvider</c>.</summary>
    [ObjectLiteral]
    public class HoverProvider
    {
        public Func<ITextModel, Position, ICancellationToken, object> provideHover;
    }

    /// <summary>A whole-document formatter, as passed to <c>registerDocumentFormattingEditProvider</c>.</summary>
    [ObjectLiteral]
    public class DocumentFormattingEditProvider
    {
        /// <summary>Shown in Monaco's formatter picker when a language has more than one.</summary>
        public string displayName;

        public Func<ITextModel, IFormattingOptions, ICancellationToken, object> provideDocumentFormattingEdits;
    }

    /// <summary>A selection formatter, as passed to <c>registerDocumentRangeFormattingEditProvider</c>.</summary>
    [ObjectLiteral]
    public class DocumentRangeFormattingEditProvider
    {
        public string displayName;

        public Func<ITextModel, TextRange, IFormattingOptions, ICancellationToken, object> provideDocumentRangeFormattingEdits;
    }
}
