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

### `CodeEditor`

| Area | Members |
|---|---|
| Content | `Text`, `SetText`, `GetPosition`, `SetPosition`, `RevealLine`, `Focus` |
| Language | `SetLanguage(string)`, `SetLanguage(LanguageDefinition)`, `SetLanguageByExtension` |
| Appearance | `ReadOnly`, `WordWrap`, `IsWordWrapped`, `Options(o => …)` |
| Events | `OnChanged`, `OnBeforeCreate`, `OnRendered` |
| Completion | `OnCompletion(ctx => Task<CompletionItem[]>)`, `OnCompletionRaw`, `OnResolveCompletion` |
| Hover | `OnHover(ctx => Task<string>)`, `OnHoverRaw` |
| Formatting | `OnFormat(code => Task<string>)` — enables Shift+Alt+F and Ctrl+K Ctrl+F |
| Diagnostics | `SetMarkers`, `SetDiagnostics`, `ClearMarkers`, `ValidateAsYouType`, `Validate` |
| Escape hatch | `Editor` — the raw Monaco `IStandaloneCodeEditor`; `Layout()`, `Dispose()` |

`OnCompletion` and `OnHover` hand you a `CodeContext` (the full text, the text up to the caret, the
caret `Offset`, the `Position`, and the `Word`/`WordRange` under the cursor) so you never touch
`dynamic`. The `…Raw` variants take Monaco's `(model, position)` directly when you need more control.

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
    .ValidateAsYouType(async code => await MyServer.GetErrorsAsync(code));
```

`ValidateAsYouType` clears the squiggles on each keystroke and only calls the validator after a second
of quiet, then discards the result if the text moved on while it was in flight — so a server-backed
validator is neither hammered nor able to squiggle stale code.

### `DiffViewer`

| Area | Members |
|---|---|
| Content | `Original`, `Modified`, `SetOriginal`, `SetModified`, `SetContent` |
| Language | `SetLanguage(string)`, `SetLanguage(LanguageDefinition)`, `SetLanguageByExtension` |
| Layout | `SideBySide`, `Inline` |
| Comparison | `IgnoreTrimWhitespace`, `RenderIndicators`, `Editable` |
| Navigation | `GoToNextDifference`, `GoToPreviousDifference` |
| Escape hatch | `Editor` — the raw Monaco `IStandaloneDiffEditor` |

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

### Global configuration

| Member | Purpose |
|---|---|
| `MonacoEditor.AssetsPath` | Folder holding `monaco.js`, `monaco.css` and the `*.worker.js` files (default `assets/js/monaco`, where the build copies them). Set before the first editor is built. |
| `MonacoEditor.LoadAsync()` | Loads Monaco (at most once per page). Components await this themselves; call it to warm Monaco up, or before calling `monaco.*` directly. |
| `MonacoEditor.IsLoaded` | Whether `monaco.*` is safe to call. |
| `MonacoEditor.GetLanguageIds()` / `TryGetLanguageIdForExtension` | Monaco's language registry. |
| `MonacoEditor.OnRenderedMarkdown` | Called with each markdown block Monaco renders in a hover or completion-details popup — for binding behaviour to links in backend-supplied documentation. |
| `MonacoEditor.HTML_MARKER` / `EscapeHtml` | Opt a hover or completion detail into raw HTML rendering. Escape untrusted parts first. |

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

esbuild resolves that graph and emits eight files into `assets/js/monaco/`:

| File | Size | Loaded |
|---|---|---|
| `monaco.js` | ~4.5 MB | On first editor (an IIFE exposing `window.monaco`) |
| `monaco.css` | ~340 KB | On first editor |
| `editor.worker.js` | ~300 KB | On demand — diffs, word-based suggestions |
| `json.worker.js` | ~430 KB | On demand — a `json` model |
| `css.worker.js` | ~1 MB | On demand — a `css`/`scss`/`less` model |
| `html.worker.js` | ~750 KB | On demand — an `html`/`handlebars`/`razor` model |
| `ts.worker.js` | ~7 MB | On demand — a `typescript`/`javascript` model |

Only `monaco.js` + `monaco.css` are fetched to show an editor; the language workers are pulled in by
Monaco itself the first time a model needs one, wired up through `MonacoEnvironment.getWorker`. When
`AssetsPath` points at a different origin, workers are loaded through a same-origin blob shim, since
the `Worker` constructor rejects cross-origin scripts.

## License

MIT. Monaco is MIT-licensed too; its license text ships alongside the bundle at
`assets/js/monaco/LICENSE.txt`.
