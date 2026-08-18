using System;
using Transpose;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The rest of the provider objects handed to <c>monaco.languages.register*Provider</c>, alongside
    /// the four in <c>MonacoProviders.cs</c>.
    ///
    /// Same shape throughout: an <c>[ObjectLiteral]</c> whose delegate fields become the plain
    /// JavaScript functions Monaco calls, and a <c>provide*</c> result typed as <see cref="object"/>
    /// because Monaco's <c>ProviderResult&lt;T&gt;</c> is "a T, null, or a thenable of either" - use
    /// <see cref="MonacoEditor.AsPromise"/> for the last.
    ///
    /// A C# delegate declaring fewer parameters than Monaco passes simply ignores the rest, as any
    /// JavaScript function would, so only the parameters a provider actually needs are declared.
    /// </summary>
    [ObjectLiteral]
    public class SignatureHelpProvider
    {
        /// <summary>Characters that open the parameter hints - typically <c>(</c> and <c>,</c>.</summary>
        public string[] signatureHelpTriggerCharacters;

        /// <summary>Characters that re-request them while they are already showing.</summary>
        public string[] signatureHelpRetriggerCharacters;

        /// <summary>Must resolve to a <see cref="SignatureHelpResult"/> - Monaco disposes the result.</summary>
        public Func<ITextModel, Position, object> provideSignatureHelp;
    }

    [ObjectLiteral]
    public class CodeActionProvider
    {
        /// <summary>Which kinds this provider offers, e.g. <c>"quickfix"</c> or <c>"refactor"</c>.</summary>
        public string[] providedCodeActionKinds;

        /// <summary>Must resolve to a <see cref="CodeActionList"/>.</summary>
        public Func<ITextModel, TextRange, ICodeActionContext, object> provideCodeActions;
    }

    /// <summary>What Monaco passes a code-action provider: the markers under the range it asked about.</summary>
    [External]
    [Convention(Notation.None)]
    public interface ICodeActionContext
    {
        CodeMarker[] markers { get; }

        /// <summary>Why the actions were requested: <c>"auto"</c>, <c>"manual"</c> or <c>"invoke"</c>.</summary>
        string trigger { get; }
    }

    [ObjectLiteral]
    public class DefinitionProvider
    {
        public Func<ITextModel, Position, object> provideDefinition;
    }

    [ObjectLiteral]
    public class DeclarationProvider
    {
        public Func<ITextModel, Position, object> provideDeclaration;
    }

    [ObjectLiteral]
    public class TypeDefinitionProvider
    {
        public Func<ITextModel, Position, object> provideTypeDefinition;
    }

    [ObjectLiteral]
    public class ImplementationProvider
    {
        public Func<ITextModel, Position, object> provideImplementation;
    }

    [ObjectLiteral]
    public class ReferenceProvider
    {
        public Func<ITextModel, Position, object> provideReferences;
    }

    [ObjectLiteral]
    public class DocumentHighlightProvider
    {
        public Func<ITextModel, Position, object> provideDocumentHighlights;
    }

    [ObjectLiteral]
    public class DocumentSymbolProvider
    {
        public string displayName;

        public Func<ITextModel, object> provideDocumentSymbols;
    }

    [ObjectLiteral]
    public class RenameProvider
    {
        /// <summary>Must resolve to a <see cref="WorkspaceEdit"/>, whose <c>rejectReason</c> explains a refusal.</summary>
        public Func<ITextModel, Position, string, object> provideRenameEdits;
    }

    [ObjectLiteral]
    public class InlayHintsProvider
    {
        /// <summary>Must resolve to an <see cref="InlayHintList"/>. Monaco asks only for the visible range.</summary>
        public Func<ITextModel, TextRange, object> provideInlayHints;
    }

    [ObjectLiteral]
    public class CodeLensProvider
    {
        /// <summary>Must resolve to a <see cref="CodeLensList"/>.</summary>
        public Func<ITextModel, object> provideCodeLenses;

        /// <summary>Fills in a lens that was handed over without a command. Returning it unchanged is fine.</summary>
        public Func<ITextModel, MonacoCodeLens, object> resolveCodeLens;
    }

    [ObjectLiteral]
    public class FoldingRangeProvider
    {
        public Func<ITextModel, object> provideFoldingRanges;
    }

    [ObjectLiteral]
    public class SelectionRangeProvider
    {
        /// <summary>
        /// Monaco asks for one list per caret and expects one array of <see cref="SelectionRangeResult"/>
        /// back per position, smallest range first.
        /// </summary>
        public Func<ITextModel, Position[], object> provideSelectionRanges;
    }

    [ObjectLiteral]
    public class LinkProvider
    {
        /// <summary>Must resolve to a <see cref="LinksList"/>.</summary>
        public Func<ITextModel, object> provideLinks;
    }

    [ObjectLiteral]
    public class ColorProvider
    {
        public Func<ITextModel, object> provideDocumentColors;

        /// <summary>What the picker writes back when the user chooses a colour.</summary>
        public Func<ITextModel, ColorInformation, object> provideColorPresentations;
    }

    [ObjectLiteral]
    public class DocumentSemanticTokensProvider
    {
        /// <summary>The vocabulary the packed token data refers to.</summary>
        public Func<SemanticTokensLegend> getLegend;

        /// <summary>Must resolve to a <see cref="SemanticTokensResult"/>.</summary>
        public Func<ITextModel, object> provideDocumentSemanticTokens;

        /// <summary>Monaco calls this when it is done with a result; there is nothing to release.</summary>
        public Action<object> releaseDocumentSemanticTokens;
    }

    [ObjectLiteral]
    public class OnTypeFormattingEditProvider
    {
        /// <summary>The characters that trigger a reformat as they are typed.</summary>
        public string[] autoFormatTriggerCharacters;

        public Func<ITextModel, Position, string, object> provideOnTypeFormattingEdits;
    }

    [ObjectLiteral]
    public class LinkedEditingRangeProvider
    {
        /// <summary>Must resolve to a <see cref="LinkedEditingRanges"/>.</summary>
        public Func<ITextModel, Position, object> provideLinkedEditingRanges;
    }

    [ObjectLiteral]
    public class InlineCompletionsProvider
    {
        /// <summary>Must resolve to an <see cref="InlineCompletionList"/>.</summary>
        public Func<ITextModel, Position, object> provideInlineCompletions;

        /// <summary>
        /// Monaco calls this, <b>unguarded</b>, once a suggestion list is no longer referenced - so a
        /// provider without it throws <c>disposeInlineCompletions is not a function</c> the moment ghost
        /// text has been shown. It was called <c>freeInlineCompletions</c> in earlier Monaco versions;
        /// 0.56 renamed it and passes the list plus a reason.
        /// </summary>
        public Action<object, object> disposeInlineCompletions;
    }

    // ---------------------------------------------------------------------------------------------
    // The result shapes Monaco insists on
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Monaco takes ownership of a signature-help result and disposes it, so the value has to arrive
    /// wrapped rather than bare.
    /// </summary>
    [ObjectLiteral]
    public class SignatureHelpResult
    {
        public SignatureHelp value;

        /// <summary>There is nothing to release; Monaco simply requires the member to exist.</summary>
        public Action dispose;
    }

    /// <summary>A disposable list of code actions - the shape <c>provideCodeActions</c> must resolve to.</summary>
    [ObjectLiteral]
    public class CodeActionList
    {
        public MonacoCodeAction[] actions;
        public Action             dispose;
    }

    /// <summary>
    /// A code action in the shape Monaco wants, built from the friendlier <see cref="CodeAction"/> a host
    /// returns: the edits become a <see cref="WorkspaceEdit"/> against the model's own URI.
    /// </summary>
    [ObjectLiteral]
    public class MonacoCodeAction
    {
        public string        title;
        public string        kind;
        public bool          isPreferred;
        public CodeMarker[]  diagnostics;
        public WorkspaceEdit edit;
    }

    /// <summary>A set of edits across documents, matching Monaco's <c>WorkspaceEdit</c>.</summary>
    [ObjectLiteral]
    public class WorkspaceEdit
    {
        public WorkspaceTextEdit[] edits;

        /// <summary>Set instead of edits to tell the user why a rename cannot be done.</summary>
        public string rejectReason;
    }

    /// <summary>One edit within a <see cref="WorkspaceEdit"/>, addressed by the document's URI.</summary>
    [ObjectLiteral]
    public class WorkspaceTextEdit
    {
        public IUri     resource;
        public TextEdit textEdit;
    }

    /// <summary>A place in a document, matching Monaco's <c>Location</c>.</summary>
    [ObjectLiteral]
    public class MonacoLocation
    {
        public IUri      uri;
        public TextRange range;
    }

    /// <summary>A disposable list of inlay hints.</summary>
    [ObjectLiteral]
    public class InlayHintList
    {
        public InlayHint[] hints;
        public Action      dispose;
    }

    /// <summary>A disposable list of code lenses.</summary>
    [ObjectLiteral]
    public class CodeLensList
    {
        public MonacoCodeLens[] lenses;
        public Action           dispose;
    }

    /// <summary>A code lens in Monaco's shape: a range and the command clicking it runs.</summary>
    [ObjectLiteral]
    public class MonacoCodeLens
    {
        public TextRange range;
        public Command   command;
    }

    /// <summary>A command reference, matching Monaco's <c>Command</c>.</summary>
    [ObjectLiteral]
    public class Command
    {
        public string   id;
        public string   title;
        public string   tooltip;
        public object[] arguments;
    }

    /// <summary>A disposable list of links.</summary>
    [ObjectLiteral]
    public class LinksList
    {
        public DocumentLink[] links;
        public Action         dispose;
    }

    /// <summary>One step of a smart-expand chain, matching Monaco's <c>SelectionRange</c>.</summary>
    [ObjectLiteral]
    public class SelectionRangeResult
    {
        public TextRange range;
    }

    /// <summary>Ranges Monaco should edit together, matching Monaco's <c>LinkedEditingRanges</c>.</summary>
    [ObjectLiteral]
    public class LinkedEditingRanges
    {
        public TextRange[] ranges;
    }

    /// <summary>The ghost-text suggestions for one position, matching Monaco's <c>InlineCompletions</c>.</summary>
    [ObjectLiteral]
    public class InlineCompletionList
    {
        public InlineCompletion[] items;
    }

    /// <summary>
    /// Packed semantic tokens, matching Monaco's <c>SemanticTokens</c>. The data has to be a
    /// <see cref="Uint32Array"/> - a plain array is silently ignored.
    /// </summary>
    [ObjectLiteral]
    public class SemanticTokensResult
    {
        public Uint32Array data;
    }

    /// <summary>One entry of a colour picker's menu, matching Monaco's <c>IColorPresentation</c>.</summary>
    [ObjectLiteral]
    public class ColorPresentation
    {
        public string label;
    }

    // ---------------------------------------------------------------------------------------------
    // Payloads for the editor API beyond providers
    // ---------------------------------------------------------------------------------------------

    /// <summary>Which markers <c>monaco.editor.getModelMarkers</c> should return. An empty filter means all of them.</summary>
    [ObjectLiteral]
    public class MarkerFilter
    {
        /// <summary>Restrict to one document.</summary>
        public IUri   resource;

        /// <summary>Restrict to one owner.</summary>
        public string owner;
    }

    /// <summary>Options for <c>monaco.editor.colorize</c>.</summary>
    [ObjectLiteral]
    public class ColorizeOptions
    {
        public int tabSize;
    }

    /// <summary>Options for <c>monaco.editor.colorizeElement</c>.</summary>
    [ObjectLiteral]
    public class ColorizeElementOptions
    {
        public string theme;
        public string mimeType;
        public int    tabSize;
    }

    /// <summary>Options for <c>monaco.editor.createWebWorker</c>.</summary>
    [ObjectLiteral]
    public class WebWorkerOptions
    {
        public string moduleId;
        public object createData;
        public string label;
    }

    // ---------------------------------------------------------------------------------------------
    // Bundled language-service settings
    // ---------------------------------------------------------------------------------------------

    /// <summary>The JSON service's diagnostics settings, matching its <c>DiagnosticsOptions</c>.</summary>
    [ObjectLiteral]
    public class JsonDiagnosticsOptions
    {
        public bool             validate;
        public bool             allowComments;

        /// <summary><c>"error"</c>, <c>"warning"</c> or <c>"ignore"</c>.</summary>
        public string           trailingCommas;

        /// <summary><c>"error"</c>, <c>"warning"</c> or <c>"ignore"</c>.</summary>
        public string           comments;

        public string           schemaValidation;

        /// <summary>Whether Monaco may fetch a schema over HTTP when only its URI is known.</summary>
        public bool             enableSchemaRequest;

        public JsonSchemaEntry[] schemas;
    }

    /// <summary>One registered schema, matching the JSON service's own schema entry.</summary>
    [ObjectLiteral]
    public class JsonSchemaEntry
    {
        public string   uri;

        /// <summary>Globs matched against the model URI. <c>"*"</c> matches every document.</summary>
        public string[] fileMatch;

        /// <summary>The schema itself. Must be clone-safe - it is forwarded to the worker.</summary>
        public object   schema;
    }

    /// <summary>The TypeScript service's compiler options - a subset of TypeScript's own.</summary>
    [ObjectLiteral]
    public class TypeScriptCompilerOptions
    {
        public ScriptTarget target;
        public ModuleKind   module;
        public bool         strict;
        public JsxEmit      jsx;

        /// <summary>Which built-in type libraries to load, e.g. <c>es2020</c> and <c>dom</c>.</summary>
        public string[]     lib;

        /// <summary>On, so a snippet that is not a whole program still type-checks.</summary>
        public bool         allowNonTsExtensions;

        public int          moduleResolution;
        public bool         noEmit;
        public bool         allowJs;
        public bool         esModuleInterop;
    }

    /// <summary>Which classes of TypeScript diagnostic to report.</summary>
    [ObjectLiteral]
    public class TypeScriptDiagnosticsOptions
    {
        public bool  noSemanticValidation;
        public bool  noSyntaxValidation;
        public bool  noSuggestionDiagnostics;

        /// <summary>TypeScript error codes to swallow, e.g. 2304 for "cannot find name".</summary>
        public int[] diagnosticCodesToIgnore;
    }

    /// <summary>The CSS service's settings.</summary>
    [ObjectLiteral]
    public class CssOptions
    {
        public bool           validate;
        public CssLintOptions lint;
    }

    /// <summary>
    /// The CSS linter's per-rule severities. Its keys are rule names, of which there are some thirty,
    /// so they are set by key rather than declared as fields - as with a theme's colours.
    /// </summary>
    [ObjectLiteral]
    public class CssLintOptions
    {
    }

    /// <summary>Setting a CSS lint rule by name.</summary>
    public static class CssLintOptionsExtensions
    {
        /// <summary>Sets one rule, e.g. <c>Set("emptyRules", "warning")</c>.</summary>
        public static CssLintOptions Set(this CssLintOptions options, string rule, string severity)
        {
            Script.Set(options, rule, severity);

            return options;
        }
    }

    /// <summary>The HTML service's settings.</summary>
    [ObjectLiteral]
    public class HtmlOptions
    {
        public HtmlFormatOptions  format;
        public HtmlSuggestOptions suggest;
    }

    /// <summary>The HTML formatter's settings.</summary>
    [ObjectLiteral]
    public class HtmlFormatOptions
    {
        public int    tabSize;
        public bool   insertSpaces;
        public int    wrapLineLength;
        public string unformatted;
        public string contentUnformatted;
        public bool   indentInnerHtml;
        public bool   preserveNewLines;
        public string wrapAttributes;
    }

    /// <summary>What the HTML service suggests.</summary>
    [ObjectLiteral]
    public class HtmlSuggestOptions
    {
        public bool html5;
    }
}
