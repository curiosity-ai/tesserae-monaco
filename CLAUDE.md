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

So `build/bundle-monaco.mjs` produces `monaco.js` (IIFE → `window.monaco`) and five `*.worker.js`
files. Everything that can be resolved ahead of time is: the module graph, minification, `codicon.ttf`
as a data URI, Monaco's stylesheet folded into the JS as a self-injecting `<style>`, and
`MonacoEnvironment.getWorker` baked in with the worker filenames. The browser fetches plain IIFE
scripts — no module graph, no import map, no runtime patching.

That means the C# side does nothing but load one script. In particular the bundle resolves its own
base URL from `document.currentScript.src`, so the workers follow wherever `monaco.js` is served from
and there is no second setting to keep in sync — and the old ordering constraint (MonacoEnvironment
had to exist *before* `monaco.js` evaluated) is gone, because the bundle sets it itself. The `||`
guard means a host can still install its own `MonacoEnvironment` beforehand if it wants a different
worker strategy.

Only `monaco.js` loads up front; the language workers are pulled in by Monaco on demand.

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
- **Never `await` an `IPromise`.** Its awaiter is typed as handing back the resolved values as
  `object[]`, but `Transpose.toPromise` passes a native promise straight through — so the awaited value
  is the single resolved value, `.Length` reads `undefined`, and the result silently vanishes. That is
  exactly how the hover provider looked when it broke: no error, just no tooltip. Use `IPromise.Then`;
  `MonacoEditor.AsPromise` covers the other direction. Awaiting a `Task` is fine.
- **Monaco's `resolveCompletionItem` takes `(item, token)`**, not `(model, position, item, token)` —
  the model and position from the original request are not repeated. Typing the provider is what
  surfaced that; the four-parameter version had been silently receiving the item as its `model`.

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
body and names nothing useful. `MonacoEditor.ToPlainObject` is the JSON round trip for a whole object
graph, which anonymous types need too.

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
  version lines move independently (BCL 26.8.x alongside Core 26.7.x is normal).

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

There are 28 pages in four groups: **Editors** (the components themselves, plus the diff's own API and
`Colorize`), **Language services** (one page per provider — completion, signature help, inline
completion, formatting, diagnostics, code actions, navigation, inlay hints and lenses, folding, links
and colours, semantic tokens, a custom language, and Monaco's bundled workers), **Decorations and
widgets**, and **Runtime and hosting** (options, events, actions and commands, several documents,
themes, a modal, remount). The sidebar sorts groups alphabetically and pages by their `Order`.

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

## Open bug: closing a modal that contains an editor hangs the page

**Not fixed.** Reproduce on the sample's **Modal** page: open the modal, then close it. The main
thread locks up hard enough that Chromium kills the tab (Playwright reports `Target page, context or
browser has been closed`). Opening is fine, and the modal can stay open indefinitely.

What is established, each from a clean single-browser run:

| Case | Result |
|---|---|
| Plain Tesserae modal, no editor - open then close | fine (`modals: 1 -> 0`) |
| Modal with editor - open, held open 5s+ | fine, stays responsive |
| Modal with editor - close via Escape | hang, tab killed |
| Modal with editor - close by detaching the layer from the DOM | hang |

So it is the editor's teardown - not Tesserae's `Modal`, not Escape or focus handling, and not the
open path.

Ruled out by measurement, not inspection:

- **ResizeObserver / MutationObserver feedback.** Both constructors were wrapped to count callbacks;
  neither reached 400 before the hang.
- **DOM thrash.** Counters on `removeChild` / `appendChild` / `insertBefore` / `remove` /
  `createElement` / `setAttribute` / `querySelectorAll` / `getBoundingClientRect` - none reached 3000.
- **`monaco.editor.dispose()`.** Skipping it entirely still hangs.
- **`ref dynamic` mis-emission** in `DisposeProvider`. The emitted JS is correct
  (`provider.v.dispose()`).
- **Tesserae's `DomObserver`.** `CheckUnmounted` is bounded; nothing loops.

Approaches already tried that did **not** fix it - don't repeat them:

1. Replacing the hand-rolled `ResizeObserver -> layout()` with Monaco's own `automaticLayout`. (Worth
   doing on its own merits - a hand-rolled observer calling `layout()` does loop on its own output,
   because Monaco's layout writes sizes back into the subtree being observed - but it is not this bug.)
2. Deferring the provider disposal to a macrotask via `setTimeout(..., 0)`.
3. Removing per-editor provider registration altogether, in favour of one registration per language
   dispatching through a model-keyed `WeakMap`. Architecturally nicer, still hangs.

A four-way bisect appeared to show "skip provider disposal -> survives", which is what motivated 2
and 3. **That result was noise**: all four cases shared one browser, so a crashed page poisoned the
later ones, and the run's own "skip both -> hang" row contradicted it. Give each case its own browser
instance.

The combination never cleanly tested is **no provider teardown AND no `editor.dispose()`**, each in a
fresh browser, repeated. If that still hangs, the loop is in the Modal-close path interacting with
Monaco's DOM rather than in this package's dispose, and the investigation moves to the Tesserae side.

One measurement trap worth knowing: `getBoundingClientRect()` reflects ancestor transforms, so during
the modal's open animation the editor and every ancestor read `height: 0` even though Monaco had
correctly written `height: 496px`. That is not a sizing bug - don't chase it.

### It is a rendering stall, not a main-thread lockup

Measured while re-verifying the sample. This reframes everything above, and explains why every
main-thread hypothesis in the "ruled out" list came back clean: **the loop is not on the main thread.**

With the modal open, the main thread still answers `page.evaluate` instantly, while:

- `requestAnimationFrame` never fires - **0 ticks in 2 wall-clock seconds** (~120 before opening),
- `document.timeline.currentTime` does not advance at all,
- the modal's own `tss-modal-animate` animation sits at `playState: "running", currentTime: 0`, so the
  layer stays at the `0%` keyframe (`transform: scale(0)`) and the editor reads 0x0 forever - the
  "measurement trap" above, except it never resolves,
- every synthetic keystroke, mouse move and `page.screenshot` times out, because each waits on a frame
  the renderer never produces. This is almost certainly what "Chromium kills the tab" looked like.

So the table row "open, held open 5s+ - fine, stays responsive" is not wrong, it just measured the
wrong thing: the page answers script while rendering nothing. The stall therefore begins at **open**,
which makes the close path a symptom rather than the cause, and moves the investigation to compositing
the animated modal layer with Monaco's layers inside it.

Suppressing the animation removes it completely. With
`*, *::before, *::after { animation: none !important; transition: none !important; }` injected before
opening, rAF runs at ~60fps, the editor lays out at its real 956x556, the suggest popup opens and is
unclipped, and input and screenshots work - so the editor inside the modal is otherwise entirely
healthy. That is also how to verify the Modal page at all; without it the check cannot
get a frame to measure and every assertion times out for the wrong reason.

Controls, each in its own fresh browser, all of which keep producing frames - so it is neither the
animation alone nor Monaco alone, but the two composited together:

| Case | Frames |
|---|---|
| Blank page, `tss-modal-animation` on a plain div | fine (~88 ticks/1.5s, animation completes) |
| Sample page with Monaco loaded, same animation on a plain div, modal never opened | fine |
| Blank page, a bare `monaco.editor.create`, same animation on a plain div | fine |
| Sample page, modal with an editor opened, animation live | **0 ticks, timeline frozen** |

Next step, given the above: keep `editor.dispose()` and the provider teardown out of it for now and
look at why the animated layer never gets a frame - e.g. whether Monaco's `will-change`/`transform`
layers inside an animating ancestor are what wedges the compositor, and whether Tesserae's 0.3s
`tss-modal-animation` can be applied to a wrapper that does not contain the editor's own layers.
