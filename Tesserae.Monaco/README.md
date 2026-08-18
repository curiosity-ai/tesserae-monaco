# Tesserae.Monaco

**Tesserae.Monaco** is a [Tesserae](https://github.com/curiosity-ai/tesserae) (Transpose C#-to-JavaScript)
wrapper around the [Monaco](https://github.com/microsoft/monaco-editor) code editor — the editor that
powers VS Code.

It lets you drop a code editor, a code viewer or a diff view into a Tesserae app from C#, with no
JavaScript. Monaco ships inside this NuGet package and is copied into your app's output on build, so
there is **no preload step and no CDN dependency** — referencing the package is enough, and it works
offline.

The package depends on **Tesserae only**. It ships no language intelligence of its own: completion,
hover and formatting are delegates you supply, so the same components work against a server-side
compiler, a client-side analyser, or a static word list.

## Usage

```csharp
using Tesserae.Monaco;
using static Tesserae.UI;

var editor = MonacoEditor.Editor()
    .SetLanguage("csharp")
    .SetText("public class Hello { }")
    .WordWrap()
    .OnChanged(() => Console.WriteLine("changed"));

document.body.appendChild(
    Stack().WS().HS().Children(editor).Render()
);
```

The three components are regular `IComponent`s, so the usual Tesserae sizing helpers (`.W()`, `.H()`,
`.WS()`, `.HS()`, `.S()`, `.Grow()`, …) apply. They implement `ISpecialCaseStyling`, so those styles
land directly on the editor container — which matters, because Monaco needs a sized container to
measure itself against.

### Components

| Factory | Component | Use it for |
|---|---|---|
| `MonacoEditor.Editor(autoHeight)` | `CodeEditor` | Editing: completion, hover, formatting, diagnostics |
| `MonacoEditor.Viewer(autoHeight)` | `CodeViewer` | Displaying code — highlighting and selection, no editing affordances |
| `MonacoEditor.Diff()` | `DiffViewer` | Comparing two documents, side-by-side or inline |

Pass `autoHeight: true` to grow the component to fit its content instead of scrolling vertically (the
parent has to be able to grow too).

### How the wrapper reaches Monaco

Monaco is **declared, not scripted**. `src/Interop/` describes the global `monaco` object with
`[External]` interfaces and `src/Types/` the `[ObjectLiteral]` payloads that cross the boundary, so a
call site compiles straight to the JavaScript it names and a wrong name or argument is a build error.
Nothing is emitted for an `[External]` type.

Two consequences worth knowing if you extend it:

- `as` and `is` do not work on an `[External]` interface — a type test needs runtime metadata that is
  never emitted for one, so it throws rather than answering. Use a direct cast.
- The same goes for a BCL generic over one: `List<IJsDisposable>` fails to construct. `DisposableBag`
  holds release closures for exactly this reason.

### Shared editor API

`CodeEditor` and `CodeViewer` share everything below, and each returns its own type so a chain can mix
shared and specific calls. All of it is safe to call **before** the component is mounted: standing
configuration (options, events, actions, widgets) is recorded and replayed, and transient acts
(focusing, revealing) are dropped rather than replayed at the wrong moment.

| Area | Members |
|---|---|
| Content | `Text`, `SetText`, `ApplyEdits`, `PushUndoStop`, `Undo`, `Redo`, `LineCount`, `VersionId`, `GetLineContent`, `GetValueInRange`, `GetOffsetAt`, `GetPositionAt`, `GetWordAt`, `FindMatches`, `Indentation`, `EndOfLine` |
| Language | `SetLanguage(string)`, `SetLanguage(LanguageDefinition)`, `SetLanguageByExtension` |
| Models | `Model`, `SetModel`, `SaveViewState`, `RestoreViewState` |
| Selection | `GetPosition`, `SetPosition`, `GetSelection(s)`, `SetSelection(s)`, `GetSelectedText`, `SelectAll` |
| Scrolling | `RevealLine`, `EnsureLineVisible`, `RevealLineInCenter[IfOutsideViewport]`, `RevealLineNearTop`, `RevealPosition[InCenter]`, `RevealRange…`, `Get/SetScrollTop`, `Get/SetScrollLeft`, `GetScrollHeight`, `GetContentHeight`, `GetContentWidth` |
| Decorations | `Decorate`, `ClearDecorations`, `GetDecorationRanges`, `CreateDecorations` |
| Widgets | `AddContentWidget`, `LayoutContentWidget`, `RemoveContentWidget`, `AddOverlayWidget`, `RemoveOverlayWidget`, `AddViewZone`, `RemoveViewZone` |
| Markers | `SetMarkers`, `SetDiagnostics`, `ClearMarkers`, `GetMarkers`, `OnMarkersChanged` |
| Commands | `AddAction`, `AddCommand`, `CreateContextKey`, `Trigger`, `RunAction`, `IsActionSupported` |
| Built-ins | `Format`, `FormatSelection`, `ShowFind`, `ShowReplace`, `ToggleLineComment`, `ShowSuggestions`, `GoToDefinition`, `ShowReferences`, `StartRename`, `ShowOutline`, `ShowQuickFixes`, `ShowParameterHints` |
| Events | `OnFocused`, `OnBlurred`, `OnWidgetFocused/Blurred`, `OnKeyDown/Up`, `OnMouseDown/Up/Move/Leave`, `OnContextMenu`, `OnPaste`, `OnScrollChanged`, `OnCursorPositionChanged`, `OnSelectionChanged`, `OnContentChanged`, `OnModelChanged`, `OnLanguageChanged`, `OnConfigurationChanged`, `OnLayoutChanged`, `OnContentSizeChanged`, `OnAttemptReadOnlyEdit`, `OnEditorDisposed`, `OnRendered` |
| Options | `ReadOnly`, `WordWrap`, `Minimap`, `LineNumbers`, `GlyphMargin`, `Folding`, `StickyScroll`, `IndentGuides`, `Rulers`, `RenderWhitespace`, `RenderControlCharacters`, `RenderLineHighlight`, `OccurrencesHighlight`, `FontSize`, `FontFamily`, `LineHeight`, `LetterSpacing`, `FontLigatures`, `CursorStyle`, `CursorBlinking`, `Padding`, `Placeholder`, `ReadOnlyMessage`, `DomReadOnly`, `Links`, `MouseWheelZoom`, `SmoothScrolling`, `UnicodeHighlight`, `ScrollBeyondLastLine`, `BracketPairColorization`, `ContextMenu`, `QuickSuggestions`, `QuickSuggestionsDelay`, `AcceptSuggestionOnEnter`, `TabCompletion`, `SemanticHighlighting`, `AriaLabel`, `AccessibilitySupport`, `Theme`, `AutomaticLayout`, `SetOption(name, value)` |
| Escape hatch | `Surface` — an `EditorSurface` over the live editor; `Editor` — the declared `IStandaloneCodeEditor`; `SetRawOption(name, value)` for an option `EditorOptions` does not name; `Options(o => …)`, `Layout()`, `Dispose()` |

**`Trigger` vs `RunAction`.** `Trigger(id)` reaches everything, including commands Monaco binds by
keybinding rule; `RunAction(id)` only sees the editor's own actions but tells you whether the id
matched. The navigation commands are keybinding rules, so `RunAction("editor.action.revealDefinition")`
returns false while `Trigger` on the same id works — hence the `GoToDefinition()` shorthands above.

A component is **remountable**: leaving the DOM disposes the editor (the alternative leaks one per
detach) but the component re-arms, so being re-added rebuilds it and replays the configuration, text
and view state. `Dispose()` is the one-way door.

### `CodeEditor`

Everything above, plus the language providers. The package ships **no** language intelligence — every
one of these is a delegate you supply.

| Area | Members |
|---|---|
| Completion | `OnCompletion(ctx => Task<CompletionItem[]>)`, `OnCompletionRaw`, `OnResolveCompletion`, `OnInlineCompletion` |
| Hover | `OnHover(ctx => Task<string>)`, `OnHoverRaw` |
| Signatures | `OnSignatureHelp` |
| Fixes | `OnCodeActions` |
| Navigation | `OnDefinition`, `OnDeclaration`, `OnTypeDefinition`, `OnImplementation`, `OnReferences`, `OnDocumentHighlights` |
| Symbols | `OnDocumentSymbols`, `OnRename` |
| Formatting | `OnFormat(code => Task<string>)`, `OnTypeFormat` |
| Annotations | `OnInlayHints`, `OnCodeLenses`, `OnFoldingRanges`, `OnSelectionRanges`, `OnDocumentLinks`, `OnColors`, `OnSemanticTokens`, `OnLinkedEditing` |
| Diagnostics | `ValidateAsYouType`, `Validate` (plus the shared marker members) |
| Lifecycle | `OnChanged`, `OnBeforeCreate` |

`OnCompletion`, `OnHover` and the navigation providers hand you a `CodeContext` (the full text, the
text up to the caret, the caret `Offset`, the `Position`, and the `Word`/`WordRange` under the cursor)
so you never touch `dynamic`. The `…Raw` variants take Monaco's `(model, position)` directly.

Monaco's provider registry is global per language, so every callback is gated on its own model — two
editors on `csharp` answer independently — and every registration is released when the component is
torn down.

```csharp
var editor = MonacoEditor.Editor()
    .SetLanguage("csharp")
    .OnCompletion(async ctx => new[]
    {
        new CompletionItem { label = "Console", kind = CompletionItemKind.Class },
        new CompletionItem { label = "WriteLine", kind = CompletionItemKind.Method, insertText = "WriteLine($0)" }
    })
    .OnHover(async ctx => $"**{ctx.Word}** — offset {ctx.Offset}")
    .OnFormat(async code => await MyServer.FormatAsync(code))
    .OnCodeActions(async ctx => await MyServer.GetFixesAsync(ctx.Text, ctx.Markers))
    .OnDefinition(async ctx => await MyServer.FindDefinitionAsync(ctx.Offset))
    .ValidateAsYouType(async code => await MyServer.GetErrorsAsync(code));
```

`ValidateAsYouType` clears the squiggles on each keystroke and only calls the validator after a second
of quiet, then discards the result if the text moved on while it was in flight — so a server-backed
validator is neither hammered nor able to squiggle stale code.

Two Monaco requirements the wrapper handles rather than passing on: injected text (`before`/`after` on
a decoration) needs `showIfCollapsed` when its range is empty, which `Decoration.InlineNote` sets; and
semantic highlighting is off unless the theme opts in, which `OnSemanticTokens` arranges.

### Several documents in one editor

`CodeModel` is a document independent of any editor. Create one per file, `SetModel` to switch, and
save/restore the view state per document so each keeps its caret, scroll and folding.

```csharp
var main  = MonacoEditor.CreateModel(mainSource, "typescript", "file:///src/main.ts");
var utils = MonacoEditor.CreateModel(utilSource, "typescript", "file:///src/utils.ts");

editor.SetModel(main);
// … later
var mainState = editor.SaveViewState();
editor.SetModel(utils).RestoreViewState(utilsState);
```

The URI is not decoration: Monaco's bundled TypeScript service resolves imports by it, and a JSON
schema is matched against it. Models you create are yours to `Dispose()`.

Use `ApplyEdits` rather than assigning `Text` when the change should be undoable and leave the caret
alone — assigning `Text` calls `setValue`, which resets both.

### `DiffViewer`

| Area | Members |
|---|---|
| Content | `Original`, `Modified`, `SetOriginal`, `SetModified`, `SetContent`, `OriginalModel`, `ModifiedModel` |
| Language | `SetLanguage(string)`, `SetLanguage(LanguageDefinition)`, `SetLanguageByExtension`, `SetOriginalLanguage` |
| Layout | `SideBySide`, `Inline`, `HideUnchangedRegions`, `RenderOverviewRuler`, `Minimap`, `FontSize`, `DiffWordWrap` |
| Comparison | `IgnoreTrimWhitespace`, `RenderIndicators`, `ShowMoves`, `MaxComputationTime`, `Editable`, `OriginalEditable`, `RenderMarginRevertIcon` |
| Results | `GetLineChanges`, `ChangeCount`, `IsIdentical`, `OnDiffUpdated` |
| Navigation | `GoToNextDifference`, `GoToPreviousDifference` |
| Sides | `OriginalSide`, `ModifiedSide` — an `EditorSurface` each, for decorating or subscribing to one pane |
| Escape hatch | `Editor` — the declared `IStandaloneDiffEditor`; `SetRawOption(name, value)`, `Options(o => …)` |

The diff is computed on a worker, so read `ChangeCount` or `GetLineChanges` from `OnDiffUpdated` —
reading straight after setting the content gets the previous diff, or none.

```csharp
var diff = MonacoEditor.Diff()
    .SetLanguage("csharp")
    .SetContent(before, after)
    .HideUnchangedRegions()      // collapse long identical runs
    .ShowMoves()                 // draw moved blocks as moves
    .OnDiffUpdated(() => label.Text = $"{diff.ChangeCount} changed blocks");
```

### Custom languages

`MonacoEditor.RegisterLanguage` takes a `LanguageDefinition` — a Monarch tokenizer, an optional
language configuration, theme colours for the tokens it emits, and any punctuation that should trigger
completion. It is idempotent per language id, so a component can register its language
unconditionally.

```csharp
var mylang = new LanguageDefinition
{
    Id          = "mylang",
    Extensions  = new[] { ".mylang" },
    Tokenizer   = new { tokenizer = new { root = new object[] { new object[] { "\\b(if|else)\\b", "keyword" } } } },
    TokenColors = new[] { new TokenColor("keyword", "c586c0", "bold") },
    CompletionTriggerCharacters = new[] { ":", "|" }
};

var editor = MonacoEditor.Editor().SetLanguage(mylang);
```

Monaco only auto-triggers completion on word characters, so a language whose syntax hinges on
punctuation needs `CompletionTriggerCharacters` declared or your completion handler is never asked.

### Theming

The components follow the active Tesserae theme: `MonacoEditor.LIGHT_THEME` / `DARK_THEME` are derived
from `Theme.Secondary.Background` when Monaco loads. After toggling the Tesserae theme at runtime, call
`MonacoEditor.DefineThemes()` then `MonacoEditor.ApplyTheme()`.

| Member | Purpose |
|---|---|
| `MonacoEditor.ThemeColors` | Monaco's theme colour ids — selection, gutter, scrollbar, diff, bracket colours. Only `editor.background` is set by default. |
| `MonacoEditor.AddTokenColors(…)` | Syntax colours for a **built-in** language's tokens, and for the token types a semantic-tokens provider emits. `LanguageDefinition.TokenColors` only covers its own language. |
| `MonacoEditor.LightBase` / `DarkBase` | What the two themes inherit from — set to `"hc-light"` / `"hc-black"` for high contrast. |
| `MonacoEditor.DefineTheme(name, base, rules, colors)` | A theme of your own, for `ApplyTheme(name)` or a component's `Theme(…)`. |

### Bundled language services

The JSON, TypeScript, CSS and HTML workers ship with the package. They validate syntax out of the box;
these turn them into real language services. All are safe to call before Monaco has loaded.

```csharp
MonacoEditor.ConfigureJson(schemas: new[]
{
    new JsonSchema("https://myapp/config.schema.json", new[] { "*" }, new
    {
        type = "object",
        required = new[] { "name" },
        properties = new { name = new { type = "string" } }
    })
});

MonacoEditor.ConfigureTypeScript(target: ScriptTarget.ES2020, strict: true, lib: new[] { "es2020", "dom" });
MonacoEditor.AddTypeScriptLib("declare namespace myApp { function log(m: string): void; }", "file:///myapp.d.ts");
```

`ConfigureJson` is what turns a JSON editor from a syntax check into schema validation, with completion
and hover for the properties the schema describes. `AddTypeScriptLib` is how a script the user writes
gets completion against the host's own API.

Read the results back with `GetMarkers()` / `OnMarkersChanged` — a worker's diagnostics arrive well
after the edit, so polling after typing reads the previous state.

### Global configuration

| Member | Purpose |
|---|---|
| `MonacoEditor.AssetsPath` | Folder holding `monaco.js` and the `*.worker.js` files (default `assets/js/monaco`, where the build copies them). Set before the first editor is built; the workers follow it automatically. |
| `MonacoEditor.LoadAsync()` | Loads Monaco (at most once per page). Components await this themselves; call it to warm Monaco up, or before calling `monaco.*` directly. |
| `MonacoEditor.IsLoaded` | Whether `monaco.*` is safe to call. |
| `MonacoEditor.GetLanguageIds()` / `TryGetLanguageIdForExtension` | Monaco's language registry. |
| `MonacoEditor.OnRenderedMarkdown` | Called with each markdown block Monaco renders in a hover or completion-details popup — for binding behaviour to links in backend-supplied documentation. |
| `MonacoEditor.HTML_MARKER` / `EscapeHtml` | Opt a hover or completion detail into raw HTML rendering. Escape untrusted parts first. |
| `MonacoEditor.WhenLoaded(action)` | Runs `action` once `monaco.*` is safe to touch — immediately if it already is, queued otherwise. The safe way to make any global Monaco call from application code, since most configuration happens while components are being built. |
| `MonacoEditor.CreateModel` / `GetModel` / `GetModels` / `GetEditors` | Documents and editors Monaco currently holds. |
| `MonacoEditor.GetMarkers` / `OnMarkersChanged` | Every squiggle on the page, the host's own and the workers'. |
| `MonacoEditor.Colorize` / `ColorizeAsync` / `ColorizeElement` | Syntax-highlighted HTML with no editor instance — much cheaper than a `CodeViewer` for a snippet nobody will interact with. |
| `MonacoEditor.CreateWebWorker` | A Monaco-managed worker running your own module. |
| `MonacoEditor.ToPlainObject` | A structured-clone-safe copy, for a whole object graph crossing to a worker. For a plain array, `Script.ToArray` is the cheaper fix — a Transpose array carries a `$type` **function**, which `postMessage` refuses. |
| `MonacoEditor.SetLocale` | Translations for Monaco's own UI strings. Must run before the first editor is built. |

Suggest and hover popups render into a single shared, body-mounted host, so they are not clipped when
an editor sits inside a modal, a panel or a split view.

## Which Monaco, and how it is built

**monaco-editor 0.56.0**, pinned in `package.json` and bundled from its **ESM** build by
`build/bundle-monaco.mjs` (esbuild). Nothing Monaco-related is committed to this repo — the bundle is
regenerated from the pinned npm package on every build and is gitignored.

The bundle step is not a convenience, it is required. Monaco's ESM tree cannot be loaded by a browser
directly: it contains ~133 bare `import './x.css'` statements, which browsers reject outright
(*"Expected a JavaScript-or-Wasm module script but the server responded with a MIME type of
text/css"*), spread across 1331 modules. This is what Monaco's own docs mean by "ESM version
(compatible with e.g. webpack)". The alternative — Monaco's prebuilt AMD dist — is deprecated
upstream and slated for removal, so it is deliberately not used.

esbuild resolves that graph and emits seven files into `assets/js/monaco/`. As much as possible is
resolved at build time — the module graph, minification, `codicon.ttf` as a data URI, Monaco's
stylesheet folded into the JS as a self-injecting `<style>`, and the `MonacoEnvironment` worker wiring
— so the browser only ever fetches plain IIFE scripts:

| File | Size | Loaded |
|---|---|---|
| `monaco.js` | ~4.8 MB | On first editor (an IIFE exposing `window.monaco`, stylesheet folded in) |
| `editor.worker.js` | ~300 KB | On demand — diffs, word-based suggestions |
| `json.worker.js` | ~430 KB | On demand — a `json` model |
| `css.worker.js` | ~1 MB | On demand — a `css`/`scss`/`less` model |
| `html.worker.js` | ~750 KB | On demand — an `html`/`handlebars`/`razor` model |
| `ts.worker.js` | ~7 MB | On demand — a `typescript`/`javascript` model |

Only `monaco.js` is fetched to show an editor; the language workers are pulled in by Monaco itself the
first time a model needs one. The bundle installs `MonacoEnvironment` and resolves the worker URLs
from its own script URL, so pointing `AssetsPath` at another origin moves the workers with it — and in
that case they load through a same-origin blob shim, since the `Worker` constructor rejects
cross-origin scripts. Set your own `window.MonacoEnvironment` before the first editor if you want a
different worker strategy.

## License

MIT. Monaco is MIT-licensed too; its license text ships alongside the bundle at
`assets/js/monaco/LICENSE.txt`.
