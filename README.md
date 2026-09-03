# Tesserae.Monaco

A [Tesserae](https://github.com/curiosity-ai/tesserae) (Transpose C#-to-JavaScript) wrapper around the
[Monaco](https://github.com/microsoft/monaco-editor) code editor — the editor that powers VS Code.

Drop a code editor, code viewer or diff view into a Tesserae app from C#, with no JavaScript. Monaco
ships inside the NuGet package, so there is no preload step and no CDN dependency.

```csharp
using Tesserae.Monaco;

var editor = MonacoEditor.Editor()
    .SetLanguage("csharp")
    .SetText("public class Hello { }")
    .OnHover(async ctx => $"**{ctx.Word}**");
```

| Project | What it is |
|---|---|
| [`Tesserae.Monaco/`](Tesserae.Monaco/) | The package — `CodeEditor`, `CodeViewer`, `DiffViewer`, and `MultiEditor`, the tabbed shell around them. See its [README](Tesserae.Monaco/README.md) for the full API. |
| [`Tesserae.Monaco.Sample/`](Tesserae.Monaco.Sample/) | A C# sample gallery — a sidebar of features, one page each, for trying things in a browser. [**Live**](https://curiosity-ai.github.io/tesserae-monaco/). |

The package depends on **Tesserae only**. It ships no language intelligence of its own — completion,
hover, signature help, quick fixes, navigation, symbols, formatting and diagnostics are all delegates you
supply, so the same components work against a server-side compiler, a client-side analyser, or a static
word list.

Beyond the providers: decorations and glyph-margin icons, content/overlay widgets and view zones,
multi-document hosting with per-document view state, the editor events, actions and keybindings, typed
options, and configuration for Monaco's bundled JSON/TypeScript/CSS/HTML services. Monaco itself is
reached through typed `[External]` declarations rather than script strings. See the package
[README](Tesserae.Monaco/README.md) for the full surface.

An editor's history can also be kept across reloads, in the browser's IndexedDB or wherever else you
say:

```csharp
MonacoEditor.Editor()
    .SetLanguage("csharp")
    .PersistHistory(new EditorHistoryOptions
    {
        Scope      = $"user:{userId}",       // the partition every entry is filed under
        DocumentId = "src/Program.cs",       // the document within it

        // Optional: IndexedDB in front of your own server, which is where the entries -
        // each stamped with a UTC timestamp - are mirrored to and read back from on a
        // second device.
        Store = MirroredHistoryStore.LocalFirst(new DelegateHistoryStore
        {
            Save = entry => Post("/api/history", entry.ToPlainObject())
        })
    });
```

What is kept is the text and Monaco's view state — caret, selections, scroll offset and folding.
Monaco's undo *stack* is not serialisable and cannot be, so a restored revision is applied as an
ordinary edit instead, which puts it on the live undo stack.

`editor.ShowHistory()` then browses it: the revisions on one side, a diff of the selected one against
what the editor holds now on the other, a search box that filters them by content, and a Revert that
goes through that same undoable edit. Each revision says where it came from — saved in this browser, or
arrived from outside it as a server checkpoint — and who made it, so one list can run in time across
every source that feeds it. It is composed from Tesserae — `SearchableList`, `Card`,
`Banner`, `SplitView` and this package's `DiffViewer` — so it ships no stylesheet and follows the
app's theme; `EditorHistoryView` is the same surface without the modal, for a panel or a page.

## Building

```bash
dotnet tool update --global Transpose.Compiler
dotnet tool update --global dotnet-serve
export PATH="$PATH:$HOME/.dotnet/tools"

dotnet build Tesserae.Monaco.slnx
```

node is required: the build bundles Monaco from the pinned npm package (nothing Monaco-related is
committed to this repo). To run the sample:

```bash
cd Tesserae.Monaco.Sample/bin/Debug/netstandard2.0/tps/
dotnet serve --port 5000
```

See [CLAUDE.md](CLAUDE.md) for how the Monaco bundle is produced and why, and for the Transpose and
Monaco behaviours worth knowing before changing anything.

## The published sample

The gallery is served from GitHub Pages at
**<https://curiosity-ai.github.io/tesserae-monaco/>**, rebuilt from `main` by
[`.github/workflows/pages.yml`](.github/workflows/pages.yml) whenever the package or the sample
changes. Every page has its own route, so a feature can be linked to directly —
[Code Editor](https://curiosity-ai.github.io/tesserae-monaco/#/view/Code%20Editor),
[Completion and Hover](https://curiosity-ai.github.io/tesserae-monaco/#/view/Completion%20and%20Hover),
[Diff Viewer](https://curiosity-ai.github.io/tesserae-monaco/#/view/Diff%20Viewer).

The Transpose output folder is already the whole site, so publishing is a copy. To reproduce it
locally:

```bash
dotnet build Tesserae.Monaco.Sample/Tesserae.Monaco.Sample.csproj -c Release
node scripts/stage-samples.mjs      # -> _site/
dotnet serve --directory _site --port 5000
```

## License

MIT — see [LICENSE](LICENSE). Monaco is MIT-licensed too; its license text ships with the bundle.
