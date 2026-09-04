# CLAUDE.md

Guidance for working in this repository.

## What this is

`tesserae.monaco` ships **Tesserae.Monaco**, a [Tesserae](https://github.com/curiosity-ai/tesserae)
(Transpose C#-to-JavaScript) wrapper around the [Monaco](https://github.com/microsoft/monaco-editor)
code editor, plus a sample app that exercises it in a browser.

The components were extracted from Mosaik's front-end (`CodeEditor`, `CodeViewer`, and the diff
editors that were hand-rolled inline in three different views). The package depends on **Tesserae
only** — see "No language intelligence" below.

```
Tesserae.Monaco/               the package
  src/MonacoEditor.cs          static factory: Editor() / Viewer() / Diff() / MultiEditor()
  src/MonacoEditor.Runtime.cs  loading, themes, custom languages, the overflow-widget host
  src/Components/              MonacoComponent (lifecycle base), CodeEditor, CodeViewer, DiffViewer
  src/MultiEditor/             MultiEditor (tree + tabs shell) and EditorDocument - see below
  src/Interop/                 [External] declarations of the `monaco` object - see below
  src/Types/                   [ObjectLiteral] interop types, CodeDiagnostic, CodeContext, LanguageDefinition
  build/bundle-monaco.mjs      esbuild: Monaco's ESM build -> the scripts we ship
  buildTransitive/*.targets     copies the bundle into a consuming app's output
Tesserae.Monaco.Sample/        the sample gallery - a sidebar of features, one page each
  src/App.cs                   sidebar, routing, reflection-based page discovery
  src/Samples/                 one ISample per feature, plus the shared page furniture
```

## Build

```bash
dotnet tool update --global Transpose.Compiler
dotnet tool update --global dotnet-serve
export PATH="$PATH:$HOME/.dotnet/tools"

dotnet build Tesserae.Monaco.slnx
```

The first build runs `npm install && npm run bundle` in `Tesserae.Monaco/` via the `BundleMonaco`
MSBuild target, so node is a prerequisite.

`Tesserae.Monaco/.npmrc` sets `min-release-age=7`, so `npm install` only resolves versions published
more than a week ago — a supply-chain cooldown, giving a compromised release time to be caught before
it reaches this build. It needs **npm >= 11.10.0** (the setting landed there; absent in 11.9.0). Older
npm accepts the key and ignores it silently — no warning, no resolution change — so an old client
builds as before rather than failing. It only affects resolution: `npm ci` installs the exact versions
in `package-lock.json`, and `monaco-editor` is pinned exactly, so the window really only governs
esbuild's caret range and the transitive tree. One sharp edge: when the window blocks a fix
`npm audit fix` wants, npm keeps the vulnerable version, warns, and exits non-zero — add that package
to `min-release-age-exclude[]` rather than dropping the window.

To see the sample:

```bash
cd Tesserae.Monaco.Sample/bin/Debug/netstandard2.0/tps/
dotnet serve --port 5000
```

## Publishing the sample to GitHub Pages

`.github/workflows/pages.yml` builds the sample on every push to `main` that touches the package or
the sample and pushes the result to the `gh-pages` branch, which GitHub serves at
<https://curiosity-ai.github.io/tesserae-monaco/>. It is modelled on `graph-kit`'s workflow of the
same name — same `contents: write` permission, same `gh-pages` concurrency group, same
`peaceiris/actions-gh-pages` publish step, actions pinned by SHA — with the build steps this repo
needs instead of `npm ci && npm run samples:dist`. The NuGet pipeline
(`.azure-devops/build-nuget-transpose.yml`) is untouched and still the thing that ships the package;
Pages only publishes the gallery.

The Azure pipeline is the reference for the build half, and the two agree deliberately: .NET 10 SDK,
Node 22, `Transpose.Compiler` as a global tool, the Monaco bundle as its own step, then
`dotnet build … -c Release`.

**The Transpose output folder is the site.** There is no site generator and nothing to template:
`tps.json` emits an `index.html` that pulls every script with a relative path, and
`Tesserae.Monaco.targets` copies Monaco in beside it. So `scripts/stage-samples.mjs` only copies
that folder to `_site/`, drops the compiler's `.tps-manifest.app.json` (its record of what it wrote,
for cleaning the folder next build — not something the page fetches), and writes `.nojekyll`.

Four things that make the sub-path work, none of which needed a change to publish:

- **Nothing is absolute.** The generated `index.html` references its scripts, `assets/css/tss.css`
  and the fonts relatively, so the whole site moves under `/tesserae-monaco/` untouched. Keep it
  that way — a `<base>` tag or an absolute path is a bug, not a fix.
- **The Monaco bundle finds its own workers and chunks**, from `import.meta.url`, so the five
  `*.worker.js` files and the `chunks/` folder follow `monaco.js` under the sub-path with no setting
  to keep in sync.
- **Routing is hash-based** (`#/view/Code%20Editor`), so every page is a URL on `index.html` and
  Pages needs no rewrite rule and no `404.html` fallback.
- **`.nojekyll`** — without it Jekyll would eat paths beginning with `_`. `enable_jekyll: false`
  makes the publish action write one too; the staging script writes it anyway so a locally staged
  `_site/` is byte-identical to what gets published.

Two build details the workflow does not leave to chance:

- **Release, not Debug.** Transpose only selects the `.min.js` resource set in Release, so a Debug
  publish would serve a variant nothing else verifies.
- **`npm ci`, not `npm install`.** The `BundleMonaco` MSBuild target would run `npm install` itself,
  but `npm ci` installs exactly what `package-lock.json` pins, which side-steps the `.npmrc`
  `min-release-age` cooldown entirely — that only governs `npm install` resolution, and only on
  npm >= 11.10.0, which the runner's npm is not. Running it as its own step also puts the bundle in
  the log; `BundleMonaco` is `Inputs`/`Outputs`-guarded on `version.txt`, so the build then skips it
  rather than installing twice.

Verified before shipping by staging a Release build into a directory named `tesserae-monaco/` and
serving its *parent*, which reproduces the sub-path Pages actually serves from — then walking all 29
pages by hash navigation with the console watched. Editors rendered and tokenized, the diff's
worker-produced decorations arrived, the custom `greet` language coloured its keywords, and no page
logged an error or a failed request. Serving the staged folder at the root would not have tested
anything: a leading-slash regression only shows up one directory down.

## Monaco is bundled, not vendored

Nothing Monaco-related is committed. `Tesserae.Monaco/assets/` is gitignored and regenerated from the
pinned npm package (`monaco-editor` in `package.json`) on every build.

**The esbuild step is required, not a convenience.** Monaco offers two builds and neither can be
consumed as-is:

- The **ESM** build (`esm/vs/...`, what we use) contains ~133 bare `import './x.css'` statements.
  Browsers reject those (`Expected a JavaScript-or-Wasm module script but the server responded with a
  MIME type of "text/css"`), and it is 1331 modules deep. Monaco's docs call it "compatible with
  e.g. webpack" for precisely this reason — it needs a bundler.
- The **AMD** build (`min/vs/...`) does load directly via `vs/loader.js`, and worked when tried, but
  upstream marks AMD as deprecated and slated for removal. Don't reintroduce it.

So `build/bundle-monaco.mjs` produces `monaco.js`, the `chunks/` beside it, and five `*.worker.js`
files. Everything that can be resolved ahead of time is: minification, `codicon.ttf` as a data URI,
Monaco's stylesheets folded into the JS as self-injecting `<style>` elements, and
`MonacoEnvironment.getWorker` baked in with the worker filenames.

### The entry is a module, and that is what makes the grammars lazy

`monaco.js` is an **ES module**, not an IIFE, and code-split (`splitting: true`). That is not a
stylistic choice — it is the only shape in which Monaco's own laziness survives bundling.

Monaco registers each of its ~90 grammars, and each of its four language-service modes, behind a
dynamic `import()`: `esm/vs/languages/definitions/csharp/register.js` declares
`loader: () => import('./csharp.js')`, and `languages/features/json/register.js` reaches its mode
through `import('./jsonMode.js')`. Bundled to an IIFE, esbuild resolves those at build time and
inlines them, so a page that only ever shows C# still downloads Perl, Pascal and PowerQuery — that
is what the previous single 4.8 MB `monaco.js` was. Bundled to ESM with splitting, they stay real
dynamic imports and each becomes a chunk fetched the first time a document uses that language.

Measured on the sample gallery: **4.1 MB across five files up front** (the entry plus the editor's
shared chunks), and one small chunk per language after that — walking all 29 pages fetched
`csharp`, `ini`, `typescript` and the `json`/`ts` modes and nothing else, out of 92 chunks on disk.
The bundle script prints both halves of that number on every build, so a stray static import
dragging the grammars back into the entry shows up in the build log rather than in a profile.

Two consequences of being a module, both already handled:

- **The base URL comes from `import.meta.url`**, not `document.currentScript.src` — which is
  `null` inside a module. The workers and the chunks therefore follow wherever `monaco.js` is served
  from, with no second setting to keep in sync.
- **The CSS cannot be an esbuild output.** esbuild emits one `.css` file per *entry point*, so a
  chunk's styles would have nowhere to go and a lazily-loaded grammar's styles would never be
  requested at all. The `css-inline` plugin turns every `import './x.css'` into a module that appends
  its own `<style>`, which keeps each chunk self-contained: the styles arrive exactly when the code
  that needs them does.

MonacoEnvironment still has to exist *before* the editor evaluates, since Monaco reads it the first
time it needs a worker and there is no way to supply it afterwards. It lives in its own module that
the entry imports first — ES module imports evaluate in source order, which is what makes "first"
mean anything. The `||` guard means a host can still install its own `MonacoEnvironment` beforehand
if it wants a different worker strategy.

That entry is fetched through **`Transpose.Require.RequireAsync(RequireKind.Module, …)`** — the
loader in the Transpose runtime — rather than Tesserae's `Require` or a hand-rolled `<script>`
element. It is the same loader every Transpose library now uses: it shares one fetch between callers,
resolves the URL against the document base, and forgets a failed load so a later mount can retry. The
`RequireKind.Module` argument is load-bearing: the URL ends in `.js`, so the loader's own sniffing
would pick a classic `<script>`, and the entry's `import` statements would be a syntax error.

Nothing loads until a component mounts (or a host calls `MonacoEditor.LoadAsync()`), so a page with
no editor on it pays nothing at all.

If you bump the `monaco-editor` pin, re-run the browser verification below — worker entry-point paths
and the CSS-import situation have both changed between minor versions.

## Monaco assets are NOT Transpose resources

This is the trap to avoid. `tps.json` deliberately declares only the four self-JS resources. Monaco is
packed into the nupkg under `monaco/` and copied into the consumer's output by
`buildTransitive/Tesserae.Monaco.targets`. Three reasons it cannot be a `resources` entry:

- Transpose emits a `<script>` tag for every `.js` resource. Eagerly injecting `monaco.js`, its
  chunks and the workers both breaks them and costs megabytes on first paint — and a classic
  `<script>` tag cannot evaluate the module entry at all.
- `"outputFormatting": "Both"` renames resources into `.min.js` variants, so a file cannot keep the
  name its own loader expects.
- The `files` globs are single-level (`*`, not `**`), so a nested tree needs one entry per folder.

Mosaik works around the injection problem with a `monaco#name.js.dontload` suffix that its **server**
strips at runtime (`Library/Curiosity.Shared.Library/FrontEndWatcher.cs`). A standalone package has no
server, so that trick is not available here.

Two gotchas in the targets file:

- `$(OutDir)` is empty at import time (the file is imported before the .NET SDK's own targets), so the
  destination path must be computed **inside** the target, not in a top-level `PropertyGroup`.
- A `ProjectReference` does not import the referenced project's `buildTransitive` targets, which is
  why `Tesserae.Monaco.Sample.csproj` imports the file by hand. That is deliberate: the sample then
  exercises exactly the copy a real package consumer gets.

## Persisting history: what Monaco actually gives you

`PersistHistory(...)` (`src/History/`) keeps an editor's document across a reload and a closed browser.
Two things shaped it more than the feature list did.

**Monaco's undo stack is not persistable, and pretending otherwise is the trap here.** It lives in the
editor's `UndoRedoService` as `IUndoRedoElement` objects holding closures over the model - no public
accessor, nothing serialisable. What Monaco *does* hand out is `saveViewState()`, whose
`ICodeEditorViewState` its own typings call "(serializable)": `cursorState`, `viewState` (the scroll
offset) and `contributionsState` (folding among them). So the persistable history is a log of text
snapshots plus that view state, which is what `EditorHistoryEntry` is. Restoring one is applied as an
**edit over the full range**, not `setValue`, so it lands on Monaco's live undo stack and a user can
undo a restore.

That edit needs `pushUndoStop()` on **both** sides. Measured: without the leading one Monaco merges the
replacement into the undo element the user's last keystrokes are still building, so a single Ctrl+Z
undoes the restore *and* the typing before it - which is the exact "undo ate my work" that choosing an
edit over `setValue` was supposed to prevent. It looks correct in a test that restores into an
untouched document, and only fails after someone has typed.

**IndexedDB, not `localStorage`.** `sessionStorage` is out immediately - it is emptied when the tab
closes. `localStorage` survives and is wrong for the rest: it is synchronous, so every snapshot blocks
the thread Monaco lays out and tokenises on; it caps around 5 MB per origin, which a revision log of a
real file passes quickly; it stores strings only, so a view state costs a `JSON.stringify` each way;
and it has no index, so pruning by age means reading every key. The Cache API persists and is
asynchronous but is a store of HTTP responses keyed by request. File System Access handles are real
files and cost a user gesture and a permission prompt each - right for "save as", wrong for an autosave
nobody asked for. IndexedDB is asynchronous, sized against available disk, stores the view state as an
object through structured clone, and its indexes and cursors make "newest first" and "older than a
month" bounded rather than full scans. Note that persistent is still not permanent: a user agent may
evict a whole origin under storage pressure, which is what `MirroredHistoryStore` exists for.

`Transpose.Core` binds the whole IndexedDB surface (`dom.indexedDB`, `IDBDatabase`, `IDBObjectStore`,
`IDBIndex`, `IDBKeyRange`, `IDBCursorWithValue`), so per "Declare Monaco, not the platform" above
nothing is declared here for it. Four things that binding does and does not have, each found by
compiling:

- **`IDBTransactionMode.readonly` needs `@readonly`** - the field is literally named after the C#
  keyword. It emits `"readonly"`.
- **A ternary over two of those literal types does not compile.** `IDBCursorDirection.prev` and
  `.next` are distinct nested types that each convert to `IDBCursorDirection` but not to each other, so
  one arm needs the cast: `query.NewestFirst ? (IDBCursorDirection)IDBCursorDirection.prev : ...`.
- **There is no `getAll`/`getAllKeys`** on either the store or the index. Everything reads through a
  cursor, which is why `ListAsync` applies its limit inside the `onsuccess` callback rather than by
  advancing past rows - a row the query's timestamp bounds reject must not count towards it.
- **The store names for a multi-store transaction need `Script.ToArray`**, like anything else a C#
  array crosses on.

**A cast to an `[ObjectLiteral]` type works on a plain object read back from storage.** This decided the
whole record design and is worth knowing generally: `(HistoryRecord)value` emits
`Transpose.cast(value, HistoryRecord)`, which for a `$literal: true` type has no constructor to test
against and hands the same object straight back. Confirmed by reading the emit and running it. So one
`[ObjectLiteral]` serves both writing and reading, and the second `[External]` declaration of the same
shape that the reading side would otherwise need does not exist. A Transpose *class* would not do: its
prototype makes `structuredClone` refuse the value, which is the same trap `MonacoEditor.ToPlainObject`
exists for on the Monaco side.

Three smaller decisions, each of which cost a round to get right:

- **Two object stores, not one.** `revisions` holds the text snapshots; `places` holds one overwritten
  row per document with the caret and scroll. Folding them together would rewrite the whole document
  every time someone merely scrolled.
- **The revisions store has no key path and `autoIncrement: true`**, so the primary key is an insertion
  counter - which is what makes a reverse cursor over the `docKey` index newest-first without a second
  index on the timestamp.
- **`docKey` is length-prefixed** (`"17:gallery:demo-usersamples/history.cs"`) rather than joined by a
  separator, so there is no character to reserve or escape and no scope/document pair can collide with
  another.

**The viewer is composed, not drawn.** `EditorHistoryView` (and `EditorHistoryModal` around it, reached
as `editor.ShowHistory()`) is the revision list plus a diff of the selected revision against the
editor's current text. Every part of it is a Tesserae component: `SearchableList<T>` is the list *and*
the "search by content" box — its `ISearchableItem.IsMatch` runs over each revision's stored text, which
is what makes the search a content search — a `Banner` is the "contents are identical" notice with its
own dismiss, a `SplitView` gives the two panes a draggable divider, and `Collapse()`/`Show()` are how
the notice and the second pane header come and go. There is no `HTMLElement` in it beyond the
`IComponent.Render()` signature, and **no stylesheet**. The first version hand-rolled the rows as divs
with a `<style>` element injected for their hover and selected states; composing them instead deleted
that file's worth of CSS and got the hover, the scrolling, the empty state and the search for nothing.

**A row is a `Button` with `ReplaceContent(...)`**, which is the toolkit's own answer to "what owns a
row's behaviour" — `ListItemText`'s docs say as much: it draws text and nothing else, and the *lister*
owns the border, the hover and the click. A default `Button` is already transparent, borderless and
shadowless and brings the themed hover and pressed backgrounds, the pointer, the focus ring and
Enter/Space with it, so a flat list row costs one call and no CSS. Two things to know:

- **Never give the button itself a background.** A `Button` with one is a *coloured* button, and a
  coloured button brightens on hover: `Background(...)` adds `tss-btn-filter-effects`, whose `:hover`
  is `filter: brightness(1.1)`. That is right for a saturated brand colour and wrong for a near-white
  wash — 235 × 1.1 clamps to 255, so the selected row went **pure white** under the pointer while the
  computed background never changed. Not a fault in the toolkit: the row is not a coloured button, so
  it should not say it is. The selected wash goes on a `Stack` *inside* the button, which is the whole
  reason `Revision` has a `_surface` — and it carries the padding too, so the wash covers the row
  rather than sitting inside its inset.
- **Clear a background rather than setting it to `transparent`.** An inline style beats the class rule,
  so a transparent one leaves the hover with nothing to paint — measured on the first version of this,
  where the rows stopped answering the pointer at all while looking exactly right.
- **`Button.Color(...)` is worse than `Background(...)` here**: it adds `tss-btn-nobg` permanently, and
  that rule sits *after* the default hover rule in the stylesheet, so the hover stays dead even once
  the inline style is gone.
- A `Card` was the first attempt (it has `HoverColor()` and `OnClick`) and reads as a card, which a list
  of revisions is not. `OmniResult` was the second, and is worse: it has the list-row visuals, but it is
  the *omnibox's* row — checkbox selection semantics, a pages rail, modal-opening — and `tss-omniresult`
  in a history sidebar misleads whoever reads it next.

**The author is a component the host supplies**, through `EditorHistoryView.RenderAuthor(...)`. What a
revision carries is whoever the store recorded, which in a real app is an id; the name behind it is a
lookup. So the hook hands back an `IComponent` rather than a string, and `InlineLabel(async label =>
…)` is the whole mechanism: built from a task it draws a skeleton while the task runs, shows whatever
the task set on it, and takes its own slot out of the line — separator included — when the task sets
nothing, which is what an id nobody can resolve should look like. Setting the hook after the rows have
loaded re-draws them, so `editor.ShowHistory().RenderAuthor(...)` stays one line. The default, when a
host says nothing, is an `InlineLabel` of the recorded name behind a swatch of its own colour.

Two things it needs from the recorder, which is why they exist on `EditorHistory`: `CurrentText` (the
diff's right-hand side, re-read after every revert rather than captured once) and `IsAttached` (whether
`Restore` has an editor to put a revision into — the Revert button is disabled without one).

**A revision says where it came from and who made it**, because a history fed by one browser is the
uninteresting case: as soon as a server mirrors it, a list mixes drafts this browser saved with
checkpoints someone else made, and a reader cannot use it without being told which is which.
`EditorHistoryEntry.Origin` (`Local` / `Remote` / `Unknown`) and `.Author` carry that, `origin` is
stored as the *string* `"local"`/`"remote"` so an unknown value reads back as `Unknown` rather than as
the wrong member, and nothing has to be labelled by hand in the normal arrangement — the recorder
stamps `Local` on what it writes, and `MirroredHistoryStore` stamps `Remote` on everything it reads
back from the store behind it, whatever the row itself says (a revision this browser mirrored is,
from another device, exactly the one that did not happen there).

That is also why `MirroredHistoryStore.ListAsync` **merges** rather than preferring the primary, which
is the one place the store's "a primary that answers is trusted" rule does not hold: browsing means
seeing all of it, and two sources interleave in time. It dedupes on the *timestamp*, since the two
stores key rows differently (an IndexedDB insertion counter against whatever a server uses) and cannot
be compared by id, while a mirrored revision is the same millisecond on both sides. `GetLatestAsync` is
untouched — what a reload restores is still the local draft when there is one.

Three things about the row that each took a measurement:

- **What a revision was is not a line count.** The rows carried one and it was the first thing to go
  when the list had to get out of the diff's way: the pane beside it says what changed, exactly, and a
  number that only ever means "bigger or smaller than the last one" spends a line saying less than
  that.
- **Sort over the union, in the view.** Every store answers newest-first on its own, so it is tempting
  to trust the order; but a `DelegateHistoryStore` is whatever a host wrote, and a merged list has no
  inherited order at all. `LoadAsync` sorts.
- **Hashing a name onto 0-359 for its colour does not work.** Two names that land eight degrees apart
  are one colour rendered inconsistently, not two colours. Measured with the demo's own names:
  "Alex Kim" and "build-bot" came out seven degrees apart, both green. It picks from a palette of ten
  mutually distinguishable hues instead, so two people are either clearly different or exactly the
  same — and the name is written in the pill, which settles the second case. The slot comes from the
  hash's whole range (`hash * 10 / modulus`), not `hash % 10`: the low digits of an accumulator that
  small are barely mixed, and the remainder put two of the three demo names in one slot.
- **Only the swatch is coloured.** A hue picked to read on white is the one that disappears on a dark
  surface, and nothing re-renders the row through a theme change — so the label keeps its own themed
  text colour and the hue is carried by `InlineLabel.SetColor(...)`, a square that is exempt from the
  line's grey precisely because it is nothing but its colour.

**Read everything off the editor before the first `await`.** `Detach` starts the flush and then drops
the surface, so an `async` body only sees a live editor up to its first `await`. This bit twice: first
by reading the place inside the second write, then — after that was "fixed" — by passing
`CapturePlace()` as an *argument* to it, which is evaluated where it is written, i.e. still after the
await. Both readings have to be locals taken before either write. The symptom is subtle: the caret
still came back, because an earlier debounced save had written one, just not the one the user left at.

**The debounce needs `visibilitychange`.** A snapshot fires after typing stops, so the last keystrokes
before a tab closes exist only because the recorder also flushes when the page is hidden. That event
rather than `beforeunload`, which a mobile browser switched away from may never deliver.

Verified in a browser, Debug and Release: typing then reloading restores the text and the caret to the
same line and column; the flush on hide saves an edit made well inside the debounce; restoring on mount
adds no revision of its own; navigating away and back keeps the text; undo after a restore reaches back
past it; the stored record carries the documented field names with `viewState` as a real object;
querying a second scope's key returns nothing; and pruning 30 rows to 20 keeps the newest 20, pruning
by a 5-second age keeps exactly the 4 inside it, and a delete empties the document.

## The `MultiEditor` shell

`src/MultiEditor/` is the tabbed shell around the editors - the shape of Mosaik's `manage/build` view,
made general: a `Tree` of `EditorDocument`s, a `Pivot` with one tab per open one, and the wiring
between them. It exists because that wiring is what every editor shell re-writes; the rule for what
goes in it is **compose Tesserae, do not draw**. Everything visible is a Tesserae component - `SplitView`,
`Tree`, `Pivot`, `SearchBox`, `CommandPalette`, `Dialog`, `TabSaveIndicator`, `UnsavedChangesGuard` -
and the package ships no stylesheet of its own; a status tint is `Tree.Item.IconColor`, a tab title is
an `HStack` of `Icon` and `TextBlock`. When something generic is missing from Tesserae, it goes into
Tesserae: this shell is why `Pivot` closes on a middle click and selects the neighbour on close, and why
`Tree` has `Filter` and `Item.IconColor`. **The Tesserae pin therefore has to carry those** - the
`2026.9.70280-local` pin is a placeholder for the Tesserae release that does, to be replaced with the
published version number once it exists; the branch was verified against a locally packed Tesserae.

Decisions that shape it, each the cheapest way to a behaviour a user notices:

- **Hidden tabs stay mounted.** Tabs are `cached: true`, so the pivot only hides the ones not in
  front, and the editor in a hidden tab keeps its caret, scroll, undo history and markers for free.
  The alternative - one editor and `SetModel` per tab, with view state saved and restored - is what
  the Several Documents page shows and is cheaper per tab, but a tab whose content is a form rather
  than an editor has no model to swap, and the shell has to host both. Closing a tab is the one-way
  door: `OpenTab.Dispose` calls `CodeEditor.Dispose()`, since a mere removal would only tear the
  editor down until its next mount.
- **Dirty state is a DOM id.** `TabSaveIndicator.TabId("tssm-doc", id)` is put on the tab title, and
  `MarkDirty`/`MarkClean`/`OnSave` go through it - which is what lets `UnsavedChangesGuard.TrackOpenTabs()`
  find the dirty tabs for the leave-page prompt without the shell and the guard knowing each other.
  A code editor's dirty state is `Text != savedText`; a `Content` tab reports its own through
  `MultiEditor.MarkDirty`.
- **The URL is read before it is written.** Re-opening the tabs from `?open=` selects each in turn, and
  each selection rewrites the query string - so `?active=` is read first, and nothing is written until
  the restore has run (`_urlRestored`). Ids the URL names that the catalog has not delivered yet wait
  in `_pendingOpen` and open when `Documents(...)` arrives, so a host can mount the shell before its
  first server round trip. Documents opened outside the catalog (`Open(document)`, a "new file") are
  not written: nothing could re-open them.
- **`Documents(...)` rebuilds the tree from scratch** and re-binds open tabs by id. Folder expansion is
  remembered per path (`_expanded`) and re-applied, which is also what `PersistLayout` stores; opening a
  folder for a filter match does not touch it, because `Tree.Filter` raises no `OnExpanded`.
- **Ctrl+S twice over.** Inside Monaco `CodeEditor.OnSave` binds the key ahead of the browser; outside
  it - focus in a form tab, or nowhere - the shell's own `keydown` listener saves the active document,
  and skips a press whose target is inside `.monaco-editor` so a save does not run twice.

Verified in the gallery with Playwright, Debug and Release: three tabs open with three live editors,
the URL carries the open set and the active tab through a reload, a middle click closes, a dirty tab
prompts and "Close without saving" discards, a content search filters the tree to one folder, a flagged
document turns its icon red, a form in a tab reports its own dirty state, Ctrl+P opens by name, and an
untitled document joins the tree and the URL when saved.

## No language intelligence

The package ships **no** completion, hover or formatting logic — those are delegates the host supplies
(`OnCompletion`, `OnHover`, `OnFormat`, `ValidateAsYouType`). Keep it that way: it is what lets the
package depend on Tesserae alone, and what lets Mosaik keep its server-backed C# providers while using
these components. Don't add an HTTP call, a `Mosaik.*` reference, or a bundled analyser.

Equally, don't add a Mosaik-specific language. `PatternSyntaxEditor` stayed in Mosaik; only the general
mechanism came across, as `LanguageDefinition` + `MonacoEditor.RegisterLanguage`.

## Monaco is declared, not scripted

There is **no `Script.Write` in this package**. `src/Interop/` declares the global `monaco` object to
the compiler with `[External]`, and `src/Types/EditorOptions.cs` + `src/Types/MonacoProviders.cs`
declare the `[ObjectLiteral]` payloads that cross the boundary. Nothing is emitted for an `[External]`
type — a call site compiles straight to the JavaScript it names — so a typo or a wrong argument is a
build error instead of a runtime one, and adding a member to an interface costs nothing at runtime.
Keep it that way: reach for a new declaration, not a script string.

Rules learned wiring that up, each confirmed by reading the emitted JS:

- **`[Convention(Notation.None)]` on every external type**, or the compiler camel-cases members and
  `monaco.KeyMod.Alt` is emitted as `monaco.KeyMod.alt`. `[ObjectLiteral]` *fields* are already left
  alone, so those only need `[Name]` when the field name has to differ from the JavaScript one.
- **`[Name]` does not survive overloads.** Two methods that emit the same JavaScript name get a `$1`
  suffix and stop matching Monaco — declaring `getOption` twice, for its string and number ids, gave
  `getOption$1` and `getOption`. It is declared once, as `getNumberOption`, for that reason.
- **`[Name]` cannot express a dotted key.** `[Name("editor.background")]` on an `[ObjectLiteral]` field
  emits `$o.editor.background = …`, a nested access. Theme colours are set with `Script.Set` instead,
  which is the one place a JavaScript name is still a string.
- **An `[ObjectLiteral]` emits only the fields actually assigned**, which is what lets one
  `EditorOptions` type serve both construction and `updateOptions`, where an unmentioned option has to
  stay untouched. `new EditorOptions { readOnly = true }` is exactly `{ readOnly: true }`.
- **A C# array carries `$type`, so it cannot be `structuredClone`d.** `System.Array.init` stamps the
  array with the Transpose class describing its element type — a *function*. Monaco hands a
  formatter's edits to its editor worker to be minimised, and posting a typed array fails the whole
  worker message with `DataCloneError`. Use `Script.ToArray` for anything Monaco forwards to a worker;
  arrays that stay on the main thread (markers, completion items, theme rules) are fine as they are.
- **Two declarations on one type cannot share a `[Name]`, but on two types they can.** `getOption` is
  heterogeneous - each option id has its own value type - and `IStandaloneCodeEditor.getNumberOption`
  already claims the name, so a second declaration there would be emitted as `getOption$1` and stop
  matching Monaco. `IStringOptions` is a separate `[External]` interface declaring the same call for
  string-valued ids; the direct cast that reaches it emits nothing, so `((IStringOptions)editor)
  .getOption(id)` is exactly `editor.getOption(id)` in the output.
- **Never `await` an `IPromise`.** Its awaiter is typed as handing back the resolved values as
  `object[]`, but `Transpose.toPromise` passes a native promise straight through — so the awaited value
  is the single resolved value, `.Length` reads `undefined`, and the result silently vanishes. That is
  exactly how the hover provider looked when it broke: no error, just no tooltip. Use `IPromise.Then`;
  `MonacoEditor.AsPromise` covers the other direction. Awaiting a `Task` is fine.
- **Monaco's `resolveCompletionItem` takes `(item, token)`**, not `(model, position, item, token)` —
  the model and position from the original request are not repeated. Typing the provider is what
  surfaced that; the four-parameter version had been silently receiving the item as its `model`. The
  `OnResolveCompletion` overload that hands over a `CodeContext` reads them off the editor at resolve time.
- **`getOption` is heterogeneous**, and one declaration can only claim one value type, so the string
  options go through `getOptions().get(id)` (`EditorSurface.GetOption`, typed `object`) rather than a
  second `[Name("getOption")]`. `GoToDefinitionOnClickOnly` reads `multiCursorModifier` that way.
- **Go-to-definition is a hover gesture too.** Monaco's `gotodefinitionatposition` contribution resolves
  the definition on every mouse move while the modifier is held, to draw the link underline and the
  source preview — a request per pixel for a server-backed provider. `EditorSurface.DisposeContribution`
  turns a contribution off by id, and `GoToDefinitionOnClickOnly` uses it, then answers the click itself
  through `trigger("editor.action.revealDefinition")`. The mouse event's buttons and modifiers live on
  `IEditorMouseEvent.event` (`@event` in C#), which Monaco reads into `leftButton`/`ctrlKey`/... rather
  than passing `button` through.

### Declare Monaco, not the platform

`src/Interop/` is for **Monaco**. Anything that is part of the browser or of .NET is already declared —
`Transpose.Core` binds the DOM (`dom.*`) and the ES5/ES6 globals (`es5.*`), and `Transpose.BCL` is the
BCL — so a second declaration of one is duplicated surface that drifts out of step with the real thing
and, worse, shadows it: a `public class Uint32Array` in this namespace hides `es5.Uint32Array` from
every consumer that has both in scope. Before declaring a global, grep `Transpose.Core` for it.

What that rules out, each of which was in here once:

- `[Name("JSON")]` for `parse`/`stringify` → **`es5.JSON`**.
- `Uint32Array` for the semantic-tokens payload → **`es5.Uint32Array`**, built from a C# array through
  `es5.ArrayLike<uint>.From(...)`, which is `[Template("{0}")]` and so emits the array itself. The
  overload set on the constructor is wide enough that a bare `uint[]` is ambiguous — name the
  conversion.
- Hand-formatting a hex byte → **`ToString("x2")`**. The BCL's integer formatter pads as well as
  converting, and it is a real `System.Int32.format` call in the emit.
- Bridging an `IPromise` to a `Task` through a `TaskCompletionSource` → **`Task.FromPromise`**, the
  mirror of `PromiseExtensions.ToPromise`. Pass a `Func<object, T>` to pick the resolved value out; a
  rejection faults the task. (This does *not* make `await` on an `IPromise` safe — see above.) A
  `TaskCompletionSource` is still right where it does more than adapt, which is why the hover provider
  keeps one: it races the provider's answer against Monaco's cancellation.

Two of the platform used to be genuinely missing from `Transpose.Core` — `document.getAnimations()`
and the effect's `target` — and `Interop/DomAnimations.cs` declared them here as a result. Both gaps
were filled upstream (`Document.getAnimations`, `Element.getAnimations`, `dom.KeyframeEffect`), so
that file is **deleted**: everything the animation wait needs (`dom.Animation`,
`dom.AnimationEffectReadOnly`, `dom.ComputedTimingProperties` and its `endTime` included) now comes
from `Transpose.Core`. Nothing platform-level is declared in `src/Interop/` any more — keep it that
way.

### Extending the typed interop

Two rules fall out of `[External]` types having **no emitted metadata**, both learned by crashing into
them:

- **`as` and `is` do not work on an `[External]` interface.** They compile to a runtime type test, which
  reads `constructor` off metadata that was never emitted, so it throws rather than answering false.
  `Instance as IStandaloneCodeEditor` took out every editor's `AfterCreate` with
  `Cannot read properties of undefined (reading 'constructor')`. Use a direct cast, which emits nothing.
- **A BCL generic over one cannot be constructed.** `List<IJsDisposable>` fails the same way, inside
  `genericName`. `DisposableBag` holds `List<Action>` of release closures instead.

Beyond that: two declarations cannot share a `[Name]` (the second is emitted with a `$1` suffix and
stops matching Monaco), which is why `getOption` is wrapped once as `getNumberOption` and
`setSelection` takes `object` rather than having a range and a selection overload. And a C# array still
needs `Script.ToArray` before it crosses to a worker — Transpose stamps a `$type` **function** onto
typed arrays, and `postMessage` refuses the whole value with a `DataCloneError` that quotes a function
body and names nothing useful. `MonacoEditor.ToPlainObject` is the fix for a whole object graph — a
JSON schema, a worker's `createData`, a view state going into IndexedDB — and it is a **structural
copy**, not the `JSON.parse(JSON.stringify(...))` round trip it used to be. Things learned replacing it,
each confirmed in the browser by the Plain Objects page (below):

- **What breaks `structuredClone` is a function, not a prototype.** A class instance clones — the
  prototype is simply dropped — so the failure was always the `$type` *function* on every C# array,
  a delegate field, or a box's `constructor`. The copy therefore skips function-valued and undefined
  members and rebuilds arrays; the test checks plainness (prototype, no functions, no undefined)
  separately, because `structuredClone` passing does not prove it.
- **Honour `toJSON`.** The Transpose runtime puts a `toJSON` on every class instance's base prototype
  (own fields plus property descriptors, `$`-bookkeeping left out) and on `List<T>` (its items); Monaco's
  `Uri`, `System.Uri` and `System.Guid` have their own. Calling it and walking its result is what keeps
  a class instance serialising exactly as the round trip did.
- **Never filter keys by a leading `$`.** `$type` is only ever on arrays, which are rebuilt rather than
  copied, and a JSON schema's `$schema`, `$ref` and `$defs` are data that has to arrive.
- **A boxed value is `{ $boxed: true, v, ... }`** with functions around it; the round trip turned it
  into that object minus the functions. The copy unboxes it.
- **The copy must not allocate a C# array.** `new object[n]` comes back stamped with the very `$type`
  being removed — the fresh array is `new es5.Array<object>()`, and the fresh object is an empty
  `[ObjectLiteral]` type, which emits `{}`.
- **Monaco's view state is not plain either.** `saveViewState()` hands out `Position` instances inside
  `viewState.firstPosition`, which is why `EditorViewState.ToPlainObject` still copies rather than
  passing the object through.
- **`Object.keys`, `getOwnPropertyNames` and `getPrototypeOf` are on `Transpose.Core.Object`** — written
  fully qualified, since a bare `Object` is ambiguous with `System.Object` — and `es5.Map` and
  `window.structuredClone` are bound too, so nothing platform-level was declared for any of this.
- Where the copy and the round trip disagree it keeps what the text form lost: a `Date` stays a `Date`,
  a `Uint32Array` and an `ArrayBuffer` pass through as themselves, `NaN` and infinities are kept, and a
  shared reference or a cycle is copied with the same shape instead of throwing. Measured on 200 copies
  of 40 schemas it is also a little faster than the round trip, not dramatically — the win is the
  dropped text intermediate and the cases that no longer fail, not speed.

## Other Transpose gotchas

- **A void `Script.Write` in an expression-bodied lambda emits `return <js>`**, which is a syntax
  error. Nothing in the package does this any more, but the rule still holds for any `Script.*` call
  added later: use a block body.
- **Don't touch `monaco.*` before it has loaded.** `SetLanguage(LanguageDefinition)` is called while
  building components in `Main`, long before mount, so `RegisterLanguage` queues definitions and
  applies them once Monaco is ready. Any new global Monaco call needs the same treatment. `IsLoaded`
  reads `window.monaco` rather than a bare `monaco` for the same reason — an undeclared global throws
  a `ReferenceError`, while a missing property is just `undefined`.
- **Enums crossing into JS need `[Enum(Emit.Value)]`**, or Transpose emits the member name.
- **`ReadOnlyArray<T>` is the underlying array at runtime** (its `op_Implicit` is `return data`), so it
  crosses into Monaco with no copy — and it has an implicit conversion from `T[]`.
- **Resolve URLs with the browser**, not by string concatenation: `new URL(path, document.baseURI)`.
  Hand-assembling from `window.location.pathname` yields `/index.html/assets/...` when the app is
  served as a file rather than a directory.
- Verify emitted JS with `node --check bin/.../tps/Tesserae.Monaco.js` — a Transpose emit bug shows up
  there long before it shows up as a confusing runtime error. Reading the emitted JS for a changed
  method is worth the minute it takes: every rule above came out of doing that.
- **The `Transpose.Compiler` tool and the `Transpose.BCL` package have to be updated together.** The
  compiler emits calls into the `tps.js` runtime that `Transpose.BCL` ships, and a newer compiler can
  emit a call the pinned runtime does not have. Updating only the tool left the sample rendering a
  blank page: the compiler emitted `Transpose.anon(...)` for `CustomLanguageSample`'s anonymous
  Monarch objects, 26.7's runtime had no `anon`, and `Main` threw before appending anything to the
  body. `node --check` passes in that state — the JS is syntactically fine, the callee just does not
  exist — so the only symptom is an empty page plus one console error. Bump both, and note the two
  version lines move independently (BCL 26.8.x alongside Core 26.7.x is normal). **`Transpose.Core`
  drags the compiler with it too**: it carries the version of the Transpose that built it, so raising
  the `Transpose.Core` pin to 26.8.4176 failed the build outright with `TPS0008 ... references
  assemblies built by a newer Transpose`, naming the minimum compiler (26.8.4157). That one is a clean
  build error rather than a blank page, so it costs nothing to discover — but the fix is a tool update,
  not a package one.
- **The Web Animations DOM comes from `Transpose.Core.dom`** — `document.getAnimations()`, `Animation`,
  `KeyframeEffect`, `ComputedTimingProperties`. It was hand-declared here (`src/Interop/DomAnimations.cs`)
  until Core caught up; the declarations are gone and `HasAnimatingAncestor` uses the real ones. Two
  shapes to know when reading that code: `Animation.effect` is typed as `AnimationEffectReadOnly`, which
  has the timing but **no `target`** — the target lives on `KeyframeEffect`, which is what a CSS
  animation, a transition and a WAAPI animation all actually have, so a direct cast (never `as`) is how
  you reach it. And `playState` is a `LiteralType<string>`, whose `==`/`!=` against a plain string emit
  `===`/`!==` rather than a call. The emitted JS is byte-for-byte what the hand-rolled declarations
  produced, cast included.

## Building on the docs' own claims

Two things in the checklist below are easy to misread, both confirmed by measurement:

- **"Format Document" is not Shift+Alt+F everywhere.** Monaco takes VS Code's per-platform bindings:
  Shift+Alt+F on Windows, Shift+Option+F on macOS, **Ctrl+Shift+I on Linux**. Verification happens on
  Linux here, so Shift+Alt+F does nothing and looks exactly like a broken `OnFormat` — the keydown
  arrives with the right `key`/`code`/`keyCode` at a focused editor and no action runs. Confirm with
  the keybinding service (`editor._standaloneKeybindingService.getKeybindings()`) before suspecting
  the provider; `editor.getAction('editor.action.formatDocument').run()` exercises the same path
  without any keyboard involved. Ctrl+K Ctrl+F for format-selection is the same on all platforms.
- **Every editor owns its own suggest widget inside the shared overflow host.** So
  `document.querySelector('.suggest-widget')` returns whichever editor's widget comes first in the
  DOM, usually a hidden one with no rows. Any check for "the suggest list is open" has to scan
  `querySelectorAll` for the visible one, or it reports a working popup as broken.

## Monaco behaviour worth preserving

These were learned the hard way in Mosaik; don't simplify them away.

- **Providers are global per language, so each callback gates on its own model**
  (`if (editor.getModel() != model) return null;`) and every registration is disposed in
  `BeforeDispose`. Without the gate, two `csharp` editors answer each other's completions; without the
  disposal, each mount leaks a provider bound to a dead model.
- **Suggest/hover popups need the shared body-mounted overflow host** (`fixedOverflowWidgets` +
  `overflowWidgetsDomNode`), or they are clipped by any `overflow: hidden` ancestor — a modal, a panel,
  a split view. The sample's **Modal** page exists to catch regressions here.
- **The hover provider honours Monaco's cancellation token.** Monaco cancels a hover as soon as the
  pointer moves; resolving late flashes a stale tooltip over the wrong symbol.
- **A completion provider with no `triggerCharacters` auto-triggers on word characters only.** So a
  member list registered without `"."` appears one letter *after* the dot rather than at it, which
  reads as a language service that does not know about members rather than as a missing setting.
  `OnCompletion(handler, triggerCharacters: new[] { "." })` on `CodeEditor`/`DiffViewer` is where they
  go; Monaco still calls the provider for word characters as well, so a trigger character widens when
  the list opens rather than narrowing it.
- **Monaco 0.56 requires `insertText` and `range` on a completion item.** 0.52 tolerated their absence;
  0.56 throws from inside the suggest widget (`Cannot read properties of undefined (reading
  'replaceAll')`, then `'replace'` on accept). `OnCompletion` fills both in when unset.
- **An inline-completions provider must declare `disposeInlineCompletions`.** It was
  `freeInlineCompletions` in earlier versions; 0.56 renamed it and calls it **unguarded** —
  `this.provider.disposeInlineCompletions(...)`, with the list and a reason — as soon as a suggestion
  list stops being referenced. A provider without it throws `disposeInlineCompletions is not a function`
  once ghost text has been shown, which is *after* the feature has visibly worked, so it does not look
  like a registration problem. The other three (`handleItemDidShow`, `handlePartialAccept`,
  `handleRejection`) are called defensively and can stay absent. Whenever the `monaco-editor` pin moves,
  grep the bundle for the member names it calls on a provider rather than trusting the declaration.
- **A diff editor ignores a dimensionless `layout()`.** With `automaticLayout` off — which is how every
  component here is created, since `MonacoComponent` drives layout from a `ResizeObserver` — Monaco's
  diff widget measures the element it was told to observe, and that element is the widget's *own root*
  rather than the container it was created in. Monaco writes that root's height itself
  (`root.style.height = fullHeight + 'px'`), so `layout()` with no argument re-reads the value Monaco
  last wrote and nothing moves. Measured: a container going 720px → 674px left the diff at 720px
  through both an explicit `layout()` and a window resize, i.e. a diff editor never resized at all.
  `DiffViewer.Layout()` therefore overrides the base and passes
  `layout(new EditorDimension { width = …clientWidth, height = …clientHeight })`. A code editor
  observes its container and does not have the problem, so the base class's dimensionless call stays
  right for the other two — and `IEditor.layout` takes the dimension as an *optional* argument rather
  than being declared twice, because two declarations emitting one JavaScript name get a `$1` suffix on
  the second.
- **A percentage height inside a flex column resolves against the container, not the leftover space.**
  Monaco's container carries `height: 100%`, so a diff placed directly as a flex item beside other rows
  asks for the whole column and overflows it by exactly the height of its siblings. `EditorHistoryView`
  puts it in a growing wrapper for that reason, and gives the wrapper `MinHeight(0)` — a flex item's
  automatic minimum size is its content's, and Monaco's scroll layer is 16.7 million pixels tall.
- **Go to Definition is a hover gesture too, and that is a trap for a provider with side effects.**
  While the trigger modifier is held, Monaco's `editor.contrib.gotodefinitionatposition` contribution
  asks the definition provider on *every mouse move*, to underline the word as a link and preview its
  source inline. A provider that costs a round-trip, or that answers by opening something, therefore
  runs while the user is merely passing over the code. `CodeEditor.NavigateOnClickOnly()` disposes that
  contribution and answers the click itself, through the same `editor.action.revealDefinition` command
  F12 and the context menu use. Two things it depends on: the go-to-definition modifier is the
  *complement* of `multiCursorModifier` (multi-cursor on alt, Monaco's default, leaves ctrl/cmd for
  navigation — getting that backwards makes the click do nothing, which reads as a dead provider), and
  `getContribution` still returns the instance after it is disposed, so a check for "is it gone" reports
  the wrong thing. Verified by asserting on the provider's own side effect: a modifier-hover does not
  produce it, a modifier-click does, a plain click does not, and F12 still jumps.
- **A diff editor's two models are ours to dispose.** Monaco does not dispose models handed to
  `setModel`, so `DiffViewer` disposes them itself — the inline versions in Mosaik leak one pair per
  render.

## The sample gallery

`Tesserae.Monaco.Sample` is shaped like Tesserae's own sample gallery, and deliberately so — anyone who
has seen one can read the other. A sidebar lists the features by group; each is a page of its own with
a route (`#/view/Code%20Editor`), so a page can be linked to, opened in its own tab, or loaded straight
into a browser check.

**Adding a page is adding a class.** There is no list to keep in sync: `App.cs` finds every `ISample`
implementation by reflection and reads its `[SampleDetails]` for the group, the order inside it and the
sidebar icon. The class name is the page name and the route — `CompletionAndHoverSample` becomes
"Completion and Hover" — so name it after the feature and drop the `Sample` suffix mentally.

```csharp
[SampleDetails(Group = "Editors", Order = 4, Icon = UIcons.FileCode)]
public class MyFeatureSample : IComponent, ISample
{
    private readonly IComponent _content;

    public MyFeatureSample()
    {
        _content = SectionStack().Secondary()
           .SampleTitle(typeof(MyFeatureSample), UIcons.FileCode, "One line on what this shows")
           .FlatSection(...)   // Overview / Best Practices / Usage, as Card(...).SetTitle(...)
           .SeeAlso(typeof(CodeEditorSample));
    }

    public HTMLElement Render() => _content.Render();
}
```

Two things this depends on, both easy to break:

- **Reflection has to be emitted inline.** `tps.json` carries `"reflection": { "disabled": false,
  "target": "inline" }`. With the metadata in a separate `.meta.js` the discovery finds nothing and the
  sidebar comes up empty.
- **Pages are built lazily and torn down on navigation.** `Defer` builds the page when it is opened, so
  one visit creates one page's editors rather than every page's at startup — and leaving a page unmounts
  them. That is a feature: it exercises the components' disposal on every click.

There are 31 pages in four groups: **Editors** (the components themselves, plus the diff's own API and
`Colorize`), **Language services** (one page per provider — completion, signature help, inline
completion, formatting, diagnostics, code actions, navigation, inlay hints and lenses, folding, links
and colours, semantic tokens, a custom language, deferred grammars, and Monaco's bundled workers),
**Decorations and widgets**, and **Runtime and hosting** (options, events, actions and commands,
several documents, themes, a modal, remount, persisted history, the multi-editor shell, and the plain-object
copy's own test page). The sidebar sorts groups
alphabetically and pages by their `Order`.

Two consequences of a page being rebuilt on every visit, both of which cost a debugging round:

- **A page that names its models cannot just create them.** Monaco throws when a URI is claimed twice,
  so the second visit to a page calling `CreateModel(..., uri)` dies. `SamplesHelper.EnsureModel(...)`
  looks the URI up with `MonacoEditor.GetModel(...)` first and resets its text — which is what the
  Several Documents and Bundled Services pages use.
- **A page with no editor on it has to start the loader itself.** It is a component mounting that calls
  `LoadAsync()`, so on the Colorize page — highlighted markup and nothing else — `WhenLoaded(...)` would
  queue a callback that nothing ever runs. It calls `MonacoEditor.LoadAsync()` for that reason.

`MonacoEditor.ApplyTheme()` alone is not enough when the theme changes at runtime. The editor
background is baked into the theme *definition*, which is derived from the Tesserae colours in force
when `DefineThemes()` last ran — so applying without redefining leaves a dark editor painted light.
Call `DefineThemes()` then `ApplyTheme()`, which is what the sidebar's sun/moon button and the
Languages and Themes page both do.

The same applies to `AddTokenColors`, and it is easy to miss because it depends on which page was
opened first. Token colours are folded into the themes when those are *defined*, which happens once as
Monaco loads — so a colour registered from a page opened later never appears. `RegisterLanguage` covers
this itself for a `LanguageDefinition`'s own `TokenColors`; `AddTokenColors` leaves it to the host, and
the Semantic Tokens page calls `DefineThemes()` and `ApplyTheme()` from `WhenLoaded(...)` for it. The
symptom is a provider that runs correctly and changes nothing on screen.

## Verifying changes

`dotnet build` proves it compiles, not that it works. Monaco failures are almost all runtime, so drive
the sample in a browser (Chromium is preinstalled at `/opt/pw-browsers/`, and
`PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` is set — do not run `playwright install`):

```bash
dotnet build Tesserae.Monaco.Sample/Tesserae.Monaco.Sample.csproj
python3 -m http.server 5002 --directory Tesserae.Monaco.Sample/bin/Debug/netstandard2.0/tps
```

The gallery gives every feature its own page and its own route, so a check goes straight to what it is
measuring: `#/view/Code%20Editor`, `#/view/Completion%20and%20Hover`, `#/view/Diagnostics`, and so on —
the route is the sidebar label with spaces percent-encoded. Loading a page directly is also the cheapest
way to isolate a failure, since only that page's editors exist.

Then check, with the console clean throughout: an editor renders and highlights (Code Editor);
completion opens and **inserts** on accept, and hover shows documentation on a real mouse hover
(Completion and Hover); the Format Document keybinding applies the formatter — **Ctrl+Shift+I** on
Linux, see above — (Formatting); a TODO squiggles about a second after typing stops (Diagnostics); the
diff shows both panes (Diff Viewer); the custom `greet` language colours its keywords (Custom
Language); and the suggest popup is not clipped inside the modal (Modal).

The provider pages each have one thing that either happens or does not, which makes them cheap to check
in a loop: the parameter-hints widget shows the signature (Signature Help), ghost text is offered
(Inline Completion), the lightbulb offers the fix and accepting it deletes the line (Code Actions), Go
to Definition jumps and the outline lists both symbols (Navigation), the `: color` hints and the two
lenses render (Inlay Hints and Lenses), Fold all collapses a region the tokenizer knows nothing about
(Folding), two colour swatches appear (Links and Colours), `MAX_ITEMS` comes out bold blue (Semantic
Tokens), five decorations land and follow the text (Decorations), the badge, the corner label and the
view zone are all on screen (Widgets), the custom action wraps the selection (Actions and Commands),
switching documents switches the language (Several Documents), and detaching then re-attaching keeps
the text (Remount).

Two of those double as worker checks, which nothing else covers: the diff's decorations come from the
editor worker, and the `json` editor produces a marker once its content is invalid. **The sample's own
JSON is valid**, so an empty marker list there is the correct result rather than a broken worker — the
Diagnostics page has a "Break the JSON" button for exactly this, and then a marker owned by `json`
should arrive. Bundled Services covers the same ground from the other side, with a document that is
invalid to begin with: markers owned by `json` (against a schema) and by `typescript` (against a `.d.ts`
handed to the worker) should both be there without touching anything. Its schema is scoped by
`fileMatch` to that page's own model URI on purpose — `"*"` would validate every JSON document in the
app, and the Diagnostics page's valid JSON would start reporting errors.

**Plain Objects is a test page, and there is a script for it.** Every kind of value the package hands to
`MonacoEditor.ToPlainObject` — a C# `string[]`, an anonymous JSON schema with `$ref` keys, an
`[ObjectLiteral]`, a host's class instance, a boxed value, a shared reference and a cycle, a `Date`, a
`Uint32Array`, Monaco's `Uri`, a real `saveViewState()`, and the history records — is normalised there
and checked in the browser: `structuredClone` accepts the copy, the copy is a plain graph, it stringifies
to what the JSON round trip produced where the two can agree, and it shares no object with its source.
The page marks its result container with `data-status="done"`, `data-passed` and `data-failed`, so

```bash
NODE_PATH="$(npm root -g)" node scripts/check-plain-objects.mjs http://localhost:5002/
```

opens it headlessly, prints one line per row and exits non-zero on a red row or a console error. It was
proven against two mutants before being trusted: an identity `ToPlainObject` turns 13 rows red, and the
old JSON round trip fails exactly the rows that assert the new semantics (box, cycle, `Date`, typed
array, `NaN`) and no others. Adding a kind of value that crosses to a worker means adding a row here.

Navigating between pages is itself a check the old single-page sample could not make: every page is
built when it is opened and disposed when it is left, so a leak or a bad teardown shows up as a console
error while walking the sidebar. That is how the diff editor's `TextModel got disposed before
DiffEditorWidget model got reset` was found.

Note Playwright's Chromium refuses some ports (5060 is "unsafe"); 5000-5002 are fine.

Habits that each save a wasted round of debugging when driving these pages with Playwright:

- **Open the page you are measuring, then take `getEditors()[0]`.** One page holds one or two editors,
  so there is no ambiguity left to resolve — the trap this replaced was picking an editor out of nine on
  a single page by its content and finding a different section that seeded the same text. Where a page
  has two (Code Viewer, Auto Height, Diagnostics, Bundled Services, Editor Options) they are in DOM
  order, and the diff editor still reports its two inner editors. Note the count is only stable once the
  page you left has been torn down: sampling `getEditors().length` immediately after a navigation can
  still see the previous page's.
- **A popup's `offsetParent` is null, so it is not a visibility test.** Suggest, hover and
  parameter-hints widgets render into the shared body-mounted overflow host, which is `position:
  absolute; width: 0; height: 0` — so `offsetParent` is null even while the widget is on screen and
  populated. Filter on a non-zero `getBoundingClientRect()` instead. This cost a round of "signature
  help is broken" when the widget was in fact showing the right content.
- **Injected text renders its spaces as `\u00a0`.** Inlay hints and ghost text are injected text, so
  they appear in `.view-line` textContent rather than under a class of their own — and a match on
  `': number'` or `'value * 2'` fails against the non-breaking spaces Monaco actually wrote, while
  printing identically in a terminal. Normalise `\u00a0` before comparing. Ghost text is also split
  across several spans, so join them first. Both of these read as a dead provider.
- **Inlay hints and code lenses only render for an editor that is actually in view**, and hints often
  need a focus before Monaco asks the provider. Scroll the editor in and focus it before asserting, or a
  working provider reads as a dead one. Only the lines *in view* carry hints, too: the second half of the
  Inlay Hints document needs a `revealLine` before its `: number` hints exist at all.
- **Click a code lens with a real mouse, and before anything has scrolled.** The lens anchor sits inside
  the editor, so once the page has been scrolled the card furniture above it intercepts the click, and
  Playwright retries until it times out.
- **Inline completions are requested when the *user* types.** An edit applied from code is not that, so
  ghost text never appears for one unless the request is made explicitly, by triggering
  `editor.action.inlineSuggest.trigger` — which is what the Inline Completion page's button does after
  its edit.
- **Read the page only after the thing you are measuring has settled.** The diff's decorations arrive
  from the diff worker and the `greet` tokens after `RegisterLanguage` flushes; sampling immediately
  after load reports zero of either and looks like a real regression. Poll instead of sleeping once.

Build in **Release** at least once before shipping: Transpose selects `.min.js` resources only there,
so a Debug-only pass does not prove the minified resource set is wired up.

Serve the sample with `dotnet serve`. It is a long-running server that does not exit on its own, so
start it in the background and poll the port rather than waiting for the process to finish.

## Fixed: opening a modal with an editor in it froze the page

**Fixed.** The sample's "Inside a modal" section used to wedge the whole page - `requestAnimationFrame`
stopped firing, `document.timeline` stopped advancing, and every keystroke, click and screenshot hung
waiting for a frame that never came, until Chromium killed the tab. It read as a crash on *close*
because that is when a user notices, but the stall started at **open**, and the main thread stayed idle
and responsive throughout: `page.evaluate` answered instantly the whole time.

The cause is not this package's teardown, not Tesserae's `Modal`, and not a race in the usual sense:

- Monaco sizes its scroll layer, `.lines-content`, to **16,777,216 x 16,777,216 px** - measured on the
  stalled page.
- Chromium rasters a layer that is running a composited transform animation over its whole subtree
  rather than the part in view, and picks the raster scale from the animation.
- Tesserae's `tss-modal-animation` starts at `transform: scale(0)` - a **singular** matrix. With a
  16.7-million-pixel layer inside it, the raster work never converges and the renderer stops producing
  frames for the entire page.

Bisected in a blank page with a fresh browser per case, so none of it depends on the sample:

| Case | Frames |
|---|---|
| `scale(0)` -> `scale(1)` on a div containing a Monaco editor | **stall** |
| ...`scale(0.001)` start | **stall** |
| ...`scale(0.01)`, `0.05`, `0.5` start | fine |
| opacity-only, `translateY`, `scale(1)`->`1.05`->`1` | fine |
| `transform: scale(0)` **static**, no animation | fine |
| the animation with a *static clone* of the editor's DOM inside | fine |
| the animation over a plain div, a `will-change: transform` div, a `<canvas>` | fine |

The last two rows are what rule out "Monaco's CSS" and "big DOM": the identical markup, not driven by
Monaco, does not stall.

Fixed on both sides:

- **This package** waits for ancestor animations before creating the editor
  (`MonacoComponent.WaitForAncestorAnimationsAsync`). One frame is enough - the animation is out of the
  dangerous scale range by its second frame - but waiting for it to end also means Monaco's font
  measurement reads a container whose `getBoundingClientRect` is not scaled. Bounded on both sides: an
  animation that never ends (`endTime` of `Infinity` - a spinner, a shimmer) is ignored, and the loop
  gives up after a second, so a pathological ancestor delays the editor rather than withholding it. This
  is the fix that matters here, because the Tesserae pin cannot move (see above), so a fix in Tesserae's
  stylesheet does not reach this sample.
- **Tesserae** starts `tss-modal-animation` at `scale(0.05)` instead of `scale(0)`
  (`Tesserae/tps/assets/css/tss.modal.css`), which fixes it for every modal content with a large scroll
  layer, not just Monaco's. Verified independently: patching only the stylesheet, with the wait removed,
  also fixes the sample.

Verified with the wait in place and Tesserae's original `scale(0)` still in the CSS: three
open/interact/close rounds, ~60 frames during each open, the editor laying out at its real size, the
suggest popup open and unclipped inside the modal, the modal closing and its editor disposed, and a
clean console. Same in a Release build.

Two things worth keeping from the investigation:

- **A frozen page is not necessarily a busy main thread.** Ask `page.evaluate` for a
  `requestAnimationFrame` count and `document.timeline.currentTime` before assuming a loop in your own
  code: if script answers instantly while both are frozen, nothing on the main thread is looping and
  every main-thread hypothesis - observer feedback, DOM thrash, a dispose loop - is a dead end. That
  mistake cost this bug several rounds of ruling out innocent code.
- **`getBoundingClientRect()` reflects ancestor transforms.** During a modal's open animation the editor
  and every ancestor read `height: 0` even though Monaco had correctly written `height: 496px`. That is
  not a sizing bug - don't chase it. It stops being visible at all now that the editor is built after
  the animation.
