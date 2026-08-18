# Lessons learned: Tesserae

Notes for anyone — human or agent — building with **Tesserae**, particularly a component that wraps a
substantial JavaScript library. Written while building [Tesserae.Monaco](README.md).

Tesserae compiles through Transpose, so most of what will actually bite you lives in
**[TRANSPOSE.md](TRANSPOSE.md)** — the C#-to-JavaScript seam, the interop rules, the toolchain hazards.
This file is the framework-specific half: the component lifecycle, composition, sizing, theming, and the
arrangement that worked for wrapping a large third-party component.

Items marked **(measured)** were established in this repository by driving a browser or reading emitted
JavaScript, not inferred from documentation.

---

## Start here: symptom → cause

| Symptom | Cause | Section |
|---|---|---|
| A component removed from the DOM never comes back | `DomObserver.WhenMounted` fires once | [1](#1-the-mount-lifecycle-fires-once-re-arm-it-for-remounting) |
| Nothing renders, no error, no console output | `Children(...)` called more than once on one stack | [2](#2-children-replaces-and-chaining-it-does-not-append) |
| Your component ignores `.WS()` / `.H(...)`, or sizes a wrapper instead | not implementing `ISpecialCaseStyling` | [3](#3-a-component-that-owns-its-own-sizing) |
| A library wants `#rrggbb` and gets `var(--…)` or `rgb(…)` | a Tesserae theme value is a CSS variable | [4](#4-resolving-a-theme-token-to-a-real-colour) |
| `Checkbox`/`Xyz` does not exist in the current context | the helper does not exist in this version | [5](#5-probe-the-api-rather-than-guessing) |
| `tss.UI.VStack is not a function`, blank page | Tesserae shipped as chunked lazy modules | [6](#6-when-tesserae-itself-is-the-problem) |
| The user's text or caret silently reverts | state not captured across a teardown | [1](#1-the-mount-lifecycle-fires-once-re-arm-it-for-remounting) |
| A popup you can plainly see reports as invisible | it renders into a zero-sized overflow host | [8](#8-verifying-ui-work-in-a-browser) |

---

## 1. The mount lifecycle fires once — re-arm it for remounting

A component wrapping a library that needs a live, measurable DOM node cannot create it in the
constructor. Create it on mount:

```csharp
public HTMLElement Render()
{
    if (!_mountRequested)
    {
        _mountRequested = true;
        DomObserver.WhenMounted(_container, () => MountAsync().FireAndForget());
    }

    return _container;
}
```

`DomObserver.WhenMounted` and `DomObserver.WhenRemoved` each fire **once**. That produces a trap with no
error message: if teardown on `WhenRemoved` does not re-arm `WhenMounted`, a component removed from the
DOM and re-added renders an **empty container ever after** — and never says why. Tearing down without
re-arming leaks the wrapped instance; re-arming without tearing down leaks harder. Do both, and keep an
explicit `Dispose()` as the one-way door:

```csharp
private void HandleRemoved()
{
    if (_disposed) return;

    Teardown();           // dispose the wrapped instance, release every subscription
    ArmMountObserver();   // a re-added container rebuilds and replays its configuration
}

public void Dispose()     // the deliberate, final release
{
    if (_disposed) return;

    _disposed = true;
    Teardown();
}
```

Two consequences worth designing for:

- **Guard against a duplicate create.** A second mount signal for an instance that already exists
  produces two of them. `if (Instance != null) return;` before creating.
- **Capture what the user can change.** Text, scroll offset, caret, selection — read them back during
  teardown and restore them after the next create, or a remount silently reverts the user's work. Anything
  configured *before* the first mount has to be replayed too, which is what
  [§7](#7-wrapping-a-javascript-component-as-a-tesserae-component) is about.

Verified by detaching and re-attaching a live editor: instance count 24 → 23 → 24, with text and caret
intact. **(measured)**

---

## 2. `Children(...)` replaces, and chaining it does not append

```csharp
var content = VStack().Children(a, b);

foreach (var extra in more) content.Children(extra);   // adds NOTHING, silently
```

Build one list and pass it once:

```csharp
var sections = new List<IComponent> { a, b };
sections.AddRange(more);

var content = VStack().Children(sections.ToArray());
```

This fails with no exception and nothing in the console — the extra components are constructed, they
simply never reach the DOM — which makes it expensive to find. If a section of your page is missing and
everything else works, check this first. **(measured)**

---

## 3. A component that owns its own sizing

A component wrapping a library that measures itself needs the Tesserae sizing helpers to style the
element *it* controls, not a wrapper. Implement `ISpecialCaseStyling`:

```csharp
public abstract class MyComponent : IComponent, ISpecialCaseStyling
{
    private readonly HTMLElement _container;

    /// The element the sizing helpers style directly.
    public HTMLElement StylingContainer => _container;

    /// Sizing stays on the container rather than propagating to a stack item.
    public bool PropagateToStackItemParent => false;
}
```

Give the container a definite size (`width: 100%; height: 100%; overflow: hidden; position: relative`) and
let the host size it with `.WS()`, `.H(220.px())` and friends. A library that measures a zero-height
parent will render nothing and blame you.

---

## 4. Resolving a theme token to a real colour

Tesserae's theme values are CSS variables, so a library wanting a concrete hex string needs one resolved:

```csharp
var hex = Color.FromString(Color.EvalVar(Theme.Secondary.Background)).ToHex();
```

`Theme.IsDark` is the light/dark switch. If you derive anything from the theme, expose a way to recompute
it — a host may switch themes at runtime, and whatever you baked in at load time will not follow:

```csharp
MonacoEditor.DefineThemes();   // rebuild from the current Tesserae theme
MonacoEditor.ApplyTheme();     // and apply it
```

---

## 5. Probe the API rather than guessing

The available helpers vary by version. In the version used here `Raw(HTMLElement)`, `Toggle`, `Label` and
`element.parentElement` exist and `Checkbox` does not. Rather than guess or decompile, write a throwaway
file that uses every candidate and read the compiler's errors — one build tells you exactly what exists:

```csharp
internal static class Probe
{
    internal static void Try()
    {
        IComponent a = Raw(DIV());
        IComponent b = Checkbox("x");   // error CS0103 → it does not exist
        IComponent c = Toggle("y");     // no error → it does
    }
}
```

Delete it afterwards. `Raw(HTMLElement)` in particular is worth knowing: it wraps a DOM element you
manage yourself as an `IComponent`, which is the escape hatch when you need to move an element between
containers by hand. **(measured)**

---

## 6. When Tesserae itself is the problem

Tesserae ships its compiled JavaScript as a package resource, extracted into your build output. **How that
resource loads is not governed by your project's settings** — `"loader": { "type": "Global" }` in your
`tps.json` controls what *you* emit, not what a referenced package ships.

That matters because a Tesserae build produced by a sufficiently new compiler is **chunked**: its `tss.js`
only registers a manifest and leaves every type, including `UI`, as a stub. A synchronous `UI.VStack(...)`
in `Main` then throws `tss.UI.VStack is not a function` before anything reaches the body, so the page is
blank with exactly one console error. The mechanism, and what to do about it, is
[TRANSPOSE.md §4.2](TRANSPOSE.md#42-chunked-lazy-modules-turn-every-type-into-a-stub).

Diagnose it in one command against your own build output:

```bash
grep -c 'chunks/' bin/Debug/netstandard2.0/tps/tss.js   # 0 = fine, hundreds = chunked
```

The lesson that generalises: **when a page goes blank after a dependency bump, check the dependency's
shipped JavaScript before auditing your own code.** Building the unmodified commit in a second worktree
and seeing it fail identically is the fastest way to be certain. This repository pins Tesserae
deliberately for exactly this reason — see [CLAUDE.md](CLAUDE.md) for the pinned version and the measured
version table. **(measured)**

---

## 7. Wrapping a JavaScript component as a Tesserae component

For a library wrapping a big JavaScript component, one arrangement paid off repeatedly. The forces are
that the wrapped instance does not exist until mount, that callers configure the component long before
that, and that a composite (a diff view's two panes, say) wants the same operations as the whole.

- **Declare the foreign API in one place** and the payload types in another. Nothing else touches the
  boundary. See [TRANSPOSE.md §1](TRANSPOSE.md#1-declaring-foreign-apis).
- **Implement each operation exactly once**, against the declared interface, in a class that wraps a
  *live* instance. Expose that class — it is what a sub-part of a composite reuses, at no extra cost. In
  this package it is `EditorSurface`, and it is why decorating one pane of a diff editor needed no new
  code.
- **Put buffering in a separate facade.** The facade records calls made before mount and replays them once
  the instance exists — and again after a remount. Separating *what the operation is* from *when it can
  run* keeps both simple.
- **Distinguish standing configuration from transient acts.** An event subscription, an action, an option
  should be recorded and replayed on remount. Focusing, revealing, scrolling should be **dropped** if
  there is nothing to focus yet — replaying those later is a real bug, not a nicety.

  ```csharp
  protected T Configure(Action<Surface> op)   // recorded; replayed on remount
  {
      _configured.Add(op);
      if (_surface is object) op(_surface);
      return Self;
  }

  protected T Live(Action<Surface> op)        // dropped if there is no instance yet
  {
      if (op is object && _surface is object) op(_surface);
      return Self;
  }
  ```

- **Centralise what every callback needs.** If the wrapped library has a *global* registry — one where
  every instance is asked about every document — each callback needs a gate so it answers only for its
  own, and every registration handle needs holding so teardown can release it. Do that once and twenty
  callbacks cost a few lines each instead of repeating the bookkeeping twenty times.
- **Use a self-type for the fluent API.** `MonacoTextComponent<T> where T : MonacoTextComponent<T>` with a
  `protected abstract T Self { get; }` lets a shared setter return the derived type, so a caller can chain
  shared and specific calls freely. Transpose handles this fine. **(measured)**

---

## 8. Verifying UI work in a browser

`dotnet build` proves it compiles. For a UI component that is nearly meaningless — serve the output and
drive it with Playwright. Four traps, each of which cost a wasted round of debugging:

1. **A popup's `offsetParent` is not a visibility test.** Widgets that must escape a clipping ancestor
   render into a shared, body-mounted host that is `position: absolute; width: 0; height: 0` — so
   `offsetParent` is `null` while the widget is plainly on screen and populated. Filter on a non-zero
   `getBoundingClientRect()` instead. This cost a round of "signature help is broken" when it was showing
   the right content.
2. **Poll for what you are measuring; do not sleep once.** Results computed on a worker — diagnostics, a
   computed diff — arrive well after the edit that caused them, so a read straight after typing sees the
   previous state. Prefer the library's own "it changed" event over a timeout.
3. **Some things only render when actually in view, and some need focus.** Annotations rendered per
   visible range read as a dead provider if the component is scrolled off the page. Scroll it in and focus
   it before asserting.
4. **Address components by DOM order, not by content.** A "get all instances" API returns creation order
   and may include inner instances of composites, and sample pages often seed the same text twice.
   Sorting on `compareDocumentPosition` gives a stable index.

And when a CSS animation and a heavily-composited child are combined, the compositor can stall: the main
thread answers scripts instantly while `requestAnimationFrame` never fires, so every synthetic input and
screenshot times out for a reason that has nothing to do with your code. Injecting
`*, *::before, *::after { animation: none !important; transition: none !important; }` before the
interaction both proves that diagnosis and unblocks the check. **(measured)**
