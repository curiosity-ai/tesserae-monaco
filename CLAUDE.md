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
  src/MonacoEditor.cs          static factory: Editor() / Viewer() / Diff()
  src/MonacoEditor.Runtime.cs  loading, themes, custom languages, the overflow-widget host
  src/Components/              MonacoComponent (lifecycle base), CodeEditor, CodeViewer, DiffViewer
  src/Types/                   [ObjectLiteral] interop types, CodeDiagnostic, CodeContext, LanguageDefinition
  build/bundle-monaco.mjs      esbuild: Monaco's ESM build -> the scripts we ship
  buildTransitive/*.targets     copies the bundle into a consuming app's output
Tesserae.Monaco.Sample/        C# stub app, one section per feature
```

## Build

```bash
dotnet tool update --global Transpose.Compiler
dotnet tool update --global dotnet-serve
export PATH="$PATH:$HOME/.dotnet/tools"

dotnet build Tesserae.Monaco.sln
```

The first build runs `npm install && npm run bundle` in `Tesserae.Monaco/` via the `BundleMonaco`
MSBuild target, so node is a prerequisite. To see the sample:

```bash
cd Tesserae.Monaco.Sample/bin/Debug/netstandard2.0/tps/
dotnet serve --port 5000
```

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

So `build/bundle-monaco.mjs` produces `monaco.js` (IIFE → `window.monaco`), `monaco.css`, and five
`*.worker.js` files. Only `monaco.js` + `monaco.css` load up front; the language workers are pulled in
by Monaco on demand through `MonacoEnvironment.getWorker`, which `ConfigureWorkers` installs **before**
`monaco.js` evaluates (there is no way to supply it afterwards).

If you bump the `monaco-editor` pin, re-run the browser verification below — worker entry-point paths
and the CSS-import situation have both changed between minor versions.

## Monaco assets are NOT Transpose resources

This is the trap to avoid. `tps.json` deliberately declares only the four self-JS resources. Monaco is
packed into the nupkg under `monaco/` and copied into the consumer's output by
`buildTransitive/Tesserae.Monaco.targets`. Three reasons it cannot be a `resources` entry:

- Transpose emits a `<script>` tag for every `.js` resource. Eagerly injecting `monaco.js` and the
  workers both breaks them and costs megabytes on first paint.
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

## No language intelligence

The package ships **no** completion, hover or formatting logic — those are delegates the host supplies
(`OnCompletion`, `OnHover`, `OnFormat`, `ValidateAsYouType`). Keep it that way: it is what lets the
package depend on Tesserae alone, and what lets Mosaik keep its server-backed C# providers while using
these components. Don't add an HTTP call, a `Mosaik.*` reference, or a bundled analyser.

Equally, don't add a Mosaik-specific language. `PatternSyntaxEditor` stayed in Mosaik; only the general
mechanism came across, as `LanguageDefinition` + `MonacoEditor.RegisterLanguage`.

## Transpose gotchas hit while writing this

- **A void `Script.Write` in an expression-bodied lambda emits `return <js>`**, which is a syntax
  error. `_ => Script.Write("if (…) …")` produced `return if (…) …` and broke the whole module. Use a
  block body for void `Script.Write`.
- **Don't touch `monaco.*` before it has loaded.** `SetLanguage(LanguageDefinition)` is called while
  building components in `Main`, long before mount, so `RegisterLanguage` queues definitions and
  applies them once Monaco is ready. Any new global Monaco call needs the same treatment.
- **Enums crossing into JS need `[Enum(Emit.Value)]`**, or Transpose emits the member name.
- **`ReadOnlyArray<T>` is the underlying array at runtime**, so pass it straight to `Script.Write` —
  no `.ToArray()` copy — and it has an implicit conversion from `T[]`.
- **Resolve URLs with the browser**, not by string concatenation:
  `new URL(path, document.baseURI)`. Hand-assembling from `window.location.pathname` yields
  `/index.html/assets/...` when the app is served as a file rather than a directory.
- Verify emitted JS with `node --check bin/.../tps/Tesserae.Monaco.js` — a Transpose emit bug shows up
  there long before it shows up as a confusing runtime error.

## Monaco behaviour worth preserving

These were learned the hard way in Mosaik; don't simplify them away.

- **Providers are global per language, so each callback gates on its own model**
  (`if (editor.getModel() != model) return null;`) and every registration is disposed in
  `BeforeDispose`. Without the gate, two `csharp` editors answer each other's completions; without the
  disposal, each mount leaks a provider bound to a dead model.
- **Suggest/hover popups need the shared body-mounted overflow host** (`fixedOverflowWidgets` +
  `overflowWidgetsDomNode`), or they are clipped by any `overflow: hidden` ancestor — a modal, a panel,
  a split view. The sample's "Inside a modal" section exists to catch regressions here.
- **The hover provider honours Monaco's cancellation token.** Monaco cancels a hover as soon as the
  pointer moves; resolving late flashes a stale tooltip over the wrong symbol.
- **Monaco 0.56 requires `insertText` and `range` on a completion item.** 0.52 tolerated their absence;
  0.56 throws from inside the suggest widget (`Cannot read properties of undefined (reading
  'replaceAll')`, then `'replace'` on accept). `OnCompletion` fills both in when unset.
- **A diff editor's two models are ours to dispose.** Monaco does not dispose models handed to
  `setModel`, so `DiffViewer` disposes them itself — the inline versions in Mosaik leak one pair per
  render.

## Verifying changes

`dotnet build` proves it compiles, not that it works. Monaco failures are almost all runtime, so drive
the sample in a browser (Chromium is preinstalled at `/opt/pw-browsers/`, and
`PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` is set — do not run `playwright install`):

```bash
dotnet build Tesserae.Monaco.Sample/Tesserae.Monaco.Sample.csproj
python3 -m http.server 5002 --directory Tesserae.Monaco.Sample/bin/Debug/netstandard2.0/tps
```

Then check, with the console clean throughout: an editor renders and highlights; completion opens and
**inserts** on accept; hover shows documentation on a real mouse hover; Shift+Alt+F applies the
formatter; a TODO squiggles about a second after typing stops; the diff shows both panes; the custom
`greet` language colours its keywords; a `json` model produces a validation marker (this is the only
check that proves the bundled workers load); and the suggest popup is not clipped inside the modal.

Note Playwright's Chromium refuses some ports (5060 is "unsafe"); 5000-5002 are fine.

Build in **Release** at least once before shipping: Transpose selects `.min.js` resources only there,
so a Debug-only pass does not prove the minified resource set is wired up.
