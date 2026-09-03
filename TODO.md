# TODO — Monaco features not yet wrapped

Every tier below is **done** and verified in a browser. Kept as the record of what was added and how it
was checked; the open items are at the bottom.

Everything is built on the typed `[External]` interop rather than script strings, so the additions
extend `src/Interop/` and `src/Types/` rather than reaching back for `Script.Write`. Three things shape
the result more than the list did:

- The operations are implemented **once** and exposed twice. `EditorSurface` holds every operation
  against a live Monaco editor; `MonacoTextComponent<T>` is a fluent, pre-mount-buffering facade over it
  for `CodeEditor` and `CodeViewer`, and a diff editor's two sides are surfaces too — which is why
  decorating one pane of a diff needed no new code.
- The provider payloads are `[ObjectLiteral]` types with delegate fields, so a provider is ordinary
  checked C# - which is what turned two of the conversions into build errors instead of runtime ones.
- `ProviderHost` centralises what every language provider needs: the model gate (Monaco's registry is
  global per language, so two `csharp` editors would answer each other), disposal (registrations outlive
  the editor), and the shapes Monaco insists on. Each of the twenty providers is a few lines on top.

## Tier 0 — Fix what was already there

- [x] **A remounted component came back empty.** `MonacoComponent` now tears the editor down on leaving
      the DOM *and* re-arms, so being re-added rebuilds it and replays the configuration, text and view
      state. `Dispose()` is the one-way door. Verified: 24 editors → 23 on detach → 24 on re-attach, text
      and caret restored.
- [x] **`OnRendered` replaced where its neighbours accumulated.** Now accumulates on all three
      components, via the same `+=` the others use.

## Tier 1 — Decorations

- [x] `Decorate` / `ClearDecorations` / `CreateDecorations` over `createDecorationsCollection`, with
      `DecorationOptions` covering class names, glyph margin, hover messages, minimap and overview-ruler
      marks, injected text and z-order. `Decoration.Line/Range/Glyph/RulerMark/InlineNote` for the common
      cases, `Ranges.*` for building ranges.
- [x] **`TrackedRangeStickiness` is consumed** — it is `DecorationOptions.stickiness`, the API it was
      always the enum for.
- [x] View zones, content widgets and overlay widgets (`ViewZone`, `ContentWidget`, `OverlayWidget`).

## Tier 2 — Models and view state

- [x] `CodeModel`, created with a `monaco.Uri`, plus `SetModel` — several documents in one editor. The URI
      is what the bundled TypeScript and JSON services address a file by, so tier 6 depended on this.
- [x] `SaveViewState` / `RestoreViewState`. Verified: scroll offset and caret survive a document switch.
- [x] Non-destructive edits — `ApplyEdits`, `PushUndoStop`, `Undo`, `Redo`. Verified: insert then undo.
- [x] Model reads — `LineCount`, `VersionId`, `GetLineContent`, `GetValueInRange`, `GetOffsetAt`,
      `GetPositionAt`, `GetWordAt`, `FindMatches`.
- [x] Configurable indentation and EOL (`Indentation`, `EndOfLine`), replacing the hardcoded tab size.

## Tier 3 — Providers

- [x] The generic hook: `ProviderHost`, with gating and disposal handled centrally.
- [x] Signature help — verified: parameter hints widget shows the active parameter's documentation.
- [x] Code actions — verified: "Remove the TODO comment" appears in the quick-fix menu, attached to the
      marker the validator reported.
- [x] Inline completions — verified: ghost text after `return `. This closes the gap where the editor
      already switched Monaco's ghost-text UI on with no way to feed it.
- [x] Definition, declaration, type definition, implementation, references, document highlights —
      verified: caret jumps to the definition; the references peek reports "References (3)".
- [x] Document symbols and rename — verified: the outline picker lists the symbols; the rename input opens.
- [x] Inlay hints, code lenses, semantic tokens, folding ranges, selection ranges, document links,
      colours, on-type formatting, linked editing — all verified rendering except on-type formatting and
      linked editing, which are wired the same way but have no sample section.

## Tier 4 — Events

- [x] Focus and blur (text and widget), cursor position, selection, content (with the change list and
      version id), model, language, configuration, layout, content size, read-only-edit attempt, dispose,
      keyboard, mouse, context menu, paste, scroll.
- [x] `OnContentChanged` carries the typed event, and `EditorSurface.On(name, handler)` returns an
      `EventSubscription` for the cases that need to unsubscribe early.

## Tier 5 — Selection, commands and markers

- [x] `Trigger`, plus `GoToDefinition` / `ShowReferences` / `StartRename` / `ShowOutline` /
      `ShowQuickFixes` / `ShowParameterHints` / `Format` / `ShowFind` and the rest as named shorthands.
- [x] Selections, multi-cursor, selected text, `SelectAll`.
- [x] The full reveal family and scroll offsets, plus `GetContentHeight`.
- [x] Markers are readable and per-owner: `GetMarkers`, `OnMarkersChanged`, and an `owner` on the setters.
- [x] `AddAction`, `AddCommand`, `CreateContextKey`. Verified: a context-menu action with a Ctrl+Alt+B
      binding edits the document, and Ctrl+S is intercepted.

## Tier 6 — Bundled language services

- [x] `ConfigureJson` — verified end-to-end: the worker reports *Incorrect type. Expected "integer"* and
      *Property extra is not allowed* against a schema supplied from C#.
- [x] `ConfigureTypeScript` + `AddTypeScriptLib` — verified: the worker type-checks a call against the
      host's own `.d.ts` (*Argument of type 'number' is not assignable to parameter of type 'string'*).
- [x] `ConfigureCss`, `ConfigureHtml`.
- [x] Typed `LanguageConfiguration` (comments, brackets, auto-closing, surrounding pairs, word pattern),
      with the raw `Configuration` kept as the escape hatch.

## Tier 7 — Diff editor

- [x] `GetLineChanges`, `ChangeCount`, `IsIdentical`, `OnDiffUpdated` — verified: "2 changed block(s)".
- [x] `OriginalSide` / `ModifiedSide` as full surfaces, so either pane can be decorated or subscribed to.
- [x] `HideUnchangedRegions` — verified: 0 → 2 collapsed bands. And `ShowMoves`.
- [x] Per-side language, `OriginalEditable`, `RenderMarginRevertIcon`, `DiffWordWrap`,
      `RenderOverviewRuler`, `MaxComputationTime`, `SetDiffOption`.

## Tier 8 — Themes

- [x] `ThemeColors` for the full colour-id dictionary; `TokenColor.Background`; `AddTokenColors` for
      built-in languages and semantic token types; `DefineTheme` for a host's own theme; `LightBase` /
      `DarkBase` for the high-contrast variants.

## Tier 9 — Long tail

- [x] ~35 typed options over one computed-key mechanism serving both construction and `updateOptions`,
      plus `SetOption(name, value)` for anything unnamed. Verified: minimap, relative line numbers and
      rulers all take effect.
- [x] `Colorize` / `ColorizeAsync` / `ColorizeElement` — highlighted HTML with no editor instance.
- [x] `CreateWebWorker`, `SetLocale`, `ToPlainObject`.
- [x] `GetContentHeight` / `OnContentSizeChanged`, and auto-height now rides
      `onDidContentSizeChange` rather than guessing from decorations and line counts.
- [x] `WhenLoaded` — the general answer to "don't touch `monaco.*` before it loads", which every piece of
      global configuration now goes through.

## Tier 10 - Persisted history

- [x] `PersistHistory(...)` on `CodeEditor`/`CodeViewer`: a revision log plus the view state, scoped by
      a caller-supplied identifier and stamped with a UTC epoch-millisecond timestamp. Restoring is an
      edit over the full range between two undo stops, so it lands on Monaco's live undo stack.
      Verified: type, reload, and the text and the caret come back; undo after a restore reaches back
      past it.
- [x] `IndexedDbHistoryStore`, the default. Two object stores (`revisions` and `places`), an index per
      scope and per scope/document pair, pruning by count and by age. Verified: 30 rows prune to the
      newest 20, a 5-second age cap keeps exactly the 4 inside it, a second scope's key reads nothing.
- [x] The hooks for an external system: `EditorHistoryOptions.OnSaved` to be told,
      `DelegateHistoryStore` to be the store, `MirroredHistoryStore` to be both - writes to each, reads
      from the browser falling back to the server, which is what a second device needs.
      `EditorHistoryOptions.Clock` is the matching hook for a server that is the authority on time, and
      `ShouldRestore` the veto for one that is an authority on the document.
- [x] `EditorViewState.ToPlainObject` / `FromPlainObject`, which is what makes the view state storable
      and sendable at all.
- [x] **Browsing what is stored.** `editor.ShowHistory()` opens `EditorHistoryModal`; `EditorHistoryView`
      is the same surface as a plain `IComponent`, for a panel or a page. The revision list, a search box
      that filters by each revision's *content*, a diff of the selected revision against the editor's
      current text, side-by-side/inline, change navigation, the "contents are identical" notice, and a
      Revert that goes through the same undoable edit `Restore` does. Composed from Tesserae -
      `SearchableList`, `Card` + `ListItemText`, `Banner`, `SplitView` and this package's `DiffViewer` -
      so it ships no stylesheet and follows a runtime theme change. Verified in Debug and Release:
      typing produces rows; selecting an older revision shows its differences and the count; the notice
      appears for the revision that matches the editor and dismisses; the content search narrows the
      list and the empty state names which emptiness it is; inline collapses to one pane and the header
      names both documents; next/previous difference moves the caret; Revert changes the editor and undo
      reaches back past it; closing disposes the diff editor and both its models; reopening works.
- [x] **A revision says where it came from and who made it.** `EditorHistoryEntry.Origin`
      (`Local`/`Remote`/`Unknown`, stored as a string) and `.Author`, with
      `EditorHistoryOptions.Author` supplying the local one; the recorder stamps `Local` and
      `MirroredHistoryStore` stamps `Remote` on what it reads from behind it, so the common arrangement
      labels itself. `MirroredHistoryStore.ListAsync` now merges both stores newest-first, deduping on
      the timestamp, because browsing means seeing all of it and two sources interleave -
      `GetLatestAsync` is unchanged, so what a reload restores is still the local draft. A list row is a
      `Button` with its content replaced - the toolkit's clickable surface, so the hover, the pressed
      state, the pointer and the keyboard come with it - carrying the origin as a coloured glyph on the
      title's own line with the sentence in its tooltip, the time the way a person says it ("just now",
      "40 min ago", "06:21", "yesterday 09:12", "3 Sep, 11:32") with the exact stamp in the tooltip, and
      the author underneath. Verified: the demo's server checkpoints by two other authors interleave
      correctly with this browser's drafts; a row written before origins were recorded reads back as
      `Unknown` with its own glyph and sorts into place; IndexedDB round-trips `origin` and `author`;
      searching "alex" narrows to one person's revisions; hover, selection and the accent read in both
      themes.
- [x] **The author is a component the host supplies.** `RenderAuthor(Func<EditorHistoryEntry,
      IComponent>)` on the view and the modal, because what a revision records is an id and the name
      behind it is a network call: an `InlineLabel` built from a task draws a skeleton while the lookup
      runs, shows what the task sets, and takes its own slot out of the line when the task sets nothing.
      Setting it after the rows have loaded re-draws them, so `editor.ShowHistory().RenderAuthor(...)`
      is one line. Verified against the sample's stand-in directory: four skeletons appear ~50ms after a
      refresh and resolve into three names with their own colours.
- [x] **Revert moved to the line that names the revision** - beside "Before 2026-09-03, 11:32" rather
      than in the toolbar, so the button and the timestamp it acts on are one sentence; the toolbar is
      left with what looks at the comparison. The line count came off the rows at the same time: the
      diff beside the list says what changed.
- [x] **Fixed on the way: a diff editor never resized.** Monaco's diff widget measures its own root,
      whose height Monaco writes itself when `automaticLayout` is off, so the dimensionless `layout()`
      the base component called could not change anything - measured, a 720px -> 674px container left
      the diff at 720px through both a `layout()` call and a window resize. `DiffViewer.Layout()` now
      passes an explicit `EditorDimension`. See [CLAUDE.md](CLAUDE.md).

---

## Open

Nothing outstanding. The one entry that lived here is now fixed:

- [x] **Opening a modal containing an editor stalled rendering** — a compositing question, as suspected,
      and not a dispose one. Monaco's 16.7-million-pixel scroll layer inside an ancestor animating from
      `transform: scale(0)` makes Chromium's raster work unbounded and the renderer stops producing
      frames for the whole page. The editor is now built after ancestor animations finish, and Tesserae's
      modal keyframes no longer start at a zero scale. Measurements in [CLAUDE.md](CLAUDE.md).

The Tesserae blocker that stopped the sample rendering while this was first built is **resolved
upstream**: the pin is now **2026.8.69584**, which is not built as chunked lazy modules, so the
verification above ran against the repo's own pins rather than a throwaway worktree. That pin is load
bearing - see [CLAUDE.md](CLAUDE.md) for why 69630 and later render a blank page.

## Deliberately not done

- **Language intelligence.** Still no bundled completion, hover or formatting logic, no HTTP call, no
  `Mosaik.*` reference, no analyser. Every provider is a delegate the host supplies — twenty of them now
  rather than four, which widens the seam without crossing it.
- **Mosaik-specific languages**, **Monaco's AMD build**, and **`automaticLayout` by default** (now
  available as an opt-in typed setter, still off).
