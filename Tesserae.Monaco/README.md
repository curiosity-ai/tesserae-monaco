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
| History | `PersistHistory(options)`, `PersistHistory(scope, documentId)`, `History`, `ShowHistory()` |
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
| Suggest UI | `ShowSuggestDetails()`, `CloseMessage()` |

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

`OnResolveCompletion` takes either a synchronous delegate or one returning a `Task`; the async
overload is the one a server-backed host wants, since Monaco calls it for the *highlighted* item only.
That is what makes a hundred suggestions cost one documentation lookup instead of a hundred.
`ShowSuggestDetails()` opens the pane that documentation lands in, which Monaco otherwise leaves
collapsed.

`CloseMessage()` takes down Monaco's transient over-the-caret message — "No definition found for
'x'". Monaco shows it whenever a definition provider yields nothing, so a provider that *did* resolve
the symbol and opened its documentation elsewhere needs to close it. Monaco shows the message on the
turn after the provider settles, so the call belongs in a zero-delay timeout rather than inline.

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

### Persisting history across reloads

`PersistHistory(...)` records the document as it is edited and puts it back the next time the same
document is opened — after a reload, or in a new browser session.

```csharp
editor.PersistHistory(new EditorHistoryOptions
{
    Scope      = $"user:{userId}",   // the partition every entry is filed under
    DocumentId = "src/Program.cs"    // the document within it
});
```

Two things are kept, because they are what Monaco hands out serialisably: the **text**, and the
**view state** — caret, selections, scroll offset and folding. Monaco's undo *stack* is not among
them: it lives in the editor's undo service as objects holding closures over the model, with no
accessor and nothing to serialise. A restored revision is therefore applied as an ordinary edit
between two undo stops, which puts it on the live undo stack — so undo reaches back past a restore.

The default store is the browser's **IndexedDB**. It is the only web storage that fits: `sessionStorage`
is emptied when the tab closes; `localStorage` survives but is synchronous (every write blocks the
thread Monaco lays out on), caps around 5 MB, stores strings only, and has no index to prune by.
IndexedDB is asynchronous, sized against available disk, stores the view state as an object, and its
cursors make "newest first" and "older than a month" bounded rather than full scans.

Every entry is stamped with a UTC epoch-millisecond `Timestamp`, and scoped: `Scope` is the partition —
a user id, a workspace id, or a composite — and `DocumentId` addresses the file inside it, so one
origin holds several users' or projects' histories without them seeing each other.

| Option | What it does |
|---|---|
| `Scope`, `DocumentId` | The partition and the document. The only two with no default. |
| `Store` | Where it goes. Defaults to `IndexedDbHistoryStore.Default`. |
| `SnapshotDebounceMs`, `PlaceDebounceMs` | How long typing / the caret has to settle first (1500 ms, 500 ms). |
| `MaxEntries`, `MaxAge` | Retention: 50 revisions, 30 days. 0 for no cap. |
| `RestoreOnMount`, `RestorePlace` | Whether to put the document, and the caret, back on create. |
| `ShouldRestore` | The veto, given what was found. For when a server is also an authority on the document. |
| `Clock` | Where `Timestamp` comes from. Replace it when a server is the authority on time. |
| `OnSaved`, `OnRestored`, `OnError` | Told about each revision written, each one put back, and anything the store raised. |

`editor.History` is the recorder itself: `SaveNowAsync(label)` takes a revision by hand and tags it
(`"before format"`, a commit id), `ListAsync(limit)` lists what is stored newest first, `Restore(entry)`
puts one back, `FlushAsync()` writes what the debounce is holding, and `ClearAsync()` forgets the
document.

#### Browsing what is stored

`editor.ShowHistory()` opens the revisions in a modal: the list on the left, a diff of the selected
revision against what the editor holds *now* on the right, and a **Revert** that puts one back through
the same undoable edit `Restore` uses.

```csharp
Button("History").SetIcon(UIcons.ClockFuturePast).OnClick(() => editor.ShowHistory());
```

It returns the `EditorHistoryModal`, or null when the editor has no history — so a host can hide the
button rather than open an empty overlay. `OnRestored(...)` is told which revision was put back, and
`Modal` is the Tesserae modal itself if it should be sized or hooked differently.

The same surface without the overlay is `new EditorHistoryView(editor.History)` — an `IComponent`, so
it goes in a panel, a split view or a page of its own. It carries the search box that filters revisions
by their **content**, the side-by-side/inline toggle, change navigation, and the "contents are
identical" notice for a revision that matches the editor.

It is composed from Tesserae rather than drawn: `SearchableList` is the list and its search box, a
`Card` over a `ListItemText` is a row, a `Banner` is the notice, a `SplitView` is the two panes and
their draggable divider, and the comparison is this package's own `DiffViewer`. So it ships no
stylesheet — the selected row's colours are `Theme` variables, and the surface follows the app's light
and dark themes as they change.

#### Hooking an external system in

Three ways, in increasing order of involvement.

**Be told.** The browser stays the store; the callback posts what it wants where it wants.

```csharp
editor.PersistHistory(new EditorHistoryOptions
{
    Scope      = scope,
    DocumentId = documentId,
    OnSaved    = entry => Post("/api/history", entry.ToPlainObject())
});
```

**Be the store.** `DelegateHistoryStore` builds one out of lambdas, so a server-backed store is an
object initialiser rather than a class. Every hook is optional and an absent one degrades rather than
fails — a missing reader answers with nothing, a missing writer discards.

```csharp
var server = new DelegateHistoryStore
{
    Save      = entry => Post("/api/history", entry.ToPlainObject()),
    GetLatest = (scope, document) => GetEntry($"/api/history/latest?scope={scope}&doc={document}"),
    List      = query => GetEntries("/api/history", query)
};
```

**Be both.** `MirroredHistoryStore` writes to the browser *and* the server, reads from the browser, and
falls through to the server when the browser has nothing — which is what a second device, a new browser
profile or a cleared origin needs to pick the document back up. The server's failures are reported
through `OnMirrorError` rather than thrown: a server that is down should cost an editor its backup, not
its history.

```csharp
Store = MirroredHistoryStore.LocalFirst(server)
```

`EditorHistoryEntry.ToPlainObject()` / `FromPlainObject(...)` are the wire contract — the field names
they produce (`scope`, `documentId`, `docKey`, `timestamp`, `text`, `viewState`, `language`,
`versionId`, `label`, `id`) are what an external store implements against.

Note that persistent is not permanent anywhere in the browser: a user agent may evict a whole origin
under storage pressure, and clearing site data always does. That is the case mirroring to a server
exists for.

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

**A grammar that should not be in the initial payload** goes on `TokenizerFactory` instead of
`Tokenizer` — a `Func<Task<object>>` Monaco calls the first time a document uses the language, and
never if none does. `ConfigurationFactory` is its companion for the comment markers and brackets. Both
map onto how Monaco defers its own ~90 grammars, so put the fetch *inside* the delegate:

```csharp
var mylang = new LanguageDefinition
{
    Id               = "mylang",
    TokenizerFactory = async () =>
    {
        await Transpose.Require.RequireAsync("assets/js/mylang.js");
        return JsGlobals.MyLangGrammar;
    },
    TokenColors = new[] { new TokenColor("keyword", "c586c0", "bold") }
};
```

`TokenColors` stay on the definition rather than in the deferred grammar: they are folded into the
themes when those are defined, which happens as Monaco loads, well before any factory runs.

`MonacoEditor.SetTokenizer(languageId, …)` is the other direction — it replaces the grammar of a
language that already exists, one of Monaco's own included. Monaco treats tokenizers as exclusive per
language, so the last one registered wins; that is how a deliberately coarse built-in grammar gets
swapped for a finer one. It takes the same two shapes, eager or deferred, and
`MonacoEditor.SetLanguageConfiguration` does the same for the brackets and comment markers.

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
| `MonacoEditor.AssetsPath` | Folder holding `monaco.js`, its `chunks/` and the `*.worker.js` files (default `assets/js/monaco`, where the build copies them). Set before the first editor is built; the chunks and workers follow it automatically. |
| `MonacoEditor.LoadAsync()` | Loads Monaco (at most once per page). Components await this themselves; call it to warm Monaco up, or before calling `monaco.*` directly. |
| `MonacoEditor.IsLoaded` | Whether `monaco.*` is safe to call. |
| `MonacoEditor.GetLanguageIds()` / `TryGetLanguageIdForExtension` | Monaco's language registry. |
| `MonacoEditor.RegisterLanguage(definition)` | A language of your own, eager or deferred. Idempotent per id. |
| `MonacoEditor.SetTokenizer(id, …)` / `SetLanguageConfiguration(id, …)` | Replace the grammar or the configuration of a language that already exists, Monaco's own included. |
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

esbuild resolves that graph and emits an ES-module entry, the chunks it pulls in, and the five
language-service workers into `assets/js/monaco/`. As much as possible is resolved at build time —
minification, `codicon.ttf` as a data URI, Monaco's stylesheets folded into the JS as self-injecting
`<style>` elements, and the `MonacoEnvironment` worker wiring:

| File | Size | Loaded |
|---|---|---|
| `monaco.js` + its shared `chunks/` | ~4.1 MB | On first editor (an ES module publishing `window.monaco`) |
| `chunks/<language>-*.js` | 1–20 KB each | On demand — the first document in that language |
| `editor.worker.js` | ~300 KB | On demand — diffs, word-based suggestions |
| `json.worker.js` | ~430 KB | On demand — a `json` model |
| `css.worker.js` | ~1 MB | On demand — a `css`/`scss`/`less` model |
| `html.worker.js` | ~750 KB | On demand — an `html`/`handlebars`/`razor` model |
| `ts.worker.js` | ~7 MB | On demand — a `typescript`/`javascript` model |

Nothing is fetched until a component mounts, so a page with no editor on it pays nothing.

**The entry is a module because that is what keeps the grammars lazy.** Monaco registers each of its
~90 grammars, and each of its four language-service modes, behind a dynamic `import()`. Bundled to a
single IIFE those are resolved at build time and inlined, so an app that only shows C# still
downloads Perl, Pascal and PowerQuery. Bundled to ESM with code splitting they stay real dynamic
imports, and each language becomes a chunk fetched the first time a document actually uses it —
walking the whole 29-page sample gallery fetches five of the 92 chunks on disk.

The entry installs `MonacoEnvironment` and resolves the worker and chunk URLs from its own
`import.meta.url`, so pointing `AssetsPath` at another origin moves them all with it — and in that
case the workers load through a same-origin blob shim, since the `Worker` constructor rejects
cross-origin scripts. Set your own `window.MonacoEnvironment` before the first editor if you want a
different worker strategy.

Serve `assets/js/monaco/` as static files with their real paths intact: the chunk names are baked
into the entry's `import` statements, so a host that renames or flattens them breaks the lazy loads.

## License

MIT. Monaco is MIT-licensed too; its license text ships alongside the bundle at
`assets/js/monaco/LICENSE.txt`.
