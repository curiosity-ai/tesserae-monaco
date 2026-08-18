# Lessons learned: Transpose and Tesserae

Notes for anyone — human or agent — working on a **Transpose** (C#-to-JavaScript) project, especially
one using **Tesserae** for its UI. Written while building [Tesserae.Monaco](README.md), but almost none
of it is Monaco-specific.

Transpose has few surprises in its *language* support and several in the seam between C# and JavaScript.
Nearly everything below is about that seam. Each entry gives the **symptom** first, because that is what
you will arrive with.

Items marked **(measured)** were established in this repository by reading emitted JavaScript or by
driving a browser, not inferred from documentation. Items without it are inherited repository knowledge
that this work relied on and did not contradict.

---

## Start here: symptom → cause

| Symptom | Cause | Section |
|---|---|---|
| `Cannot read properties of undefined (reading 'constructor')` | `as`/`is` against an `[External]` type | [1.3](#13-as-and-is-do-not-work-on-an-external-type) |
| `Cannot read properties of undefined (reading '$$name')` | a BCL generic over an `[External]` type | [1.4](#14-a-bcl-generic-over-an-external-type-cannot-be-constructed) |
| `DataCloneError` quoting a function body | a C# array or anonymous type sent to a worker | [2.1](#21-a-c-array-cannot-cross-into-a-web-worker) |
| `someMethod$1 is not a function`, or a call silently not matching | two declarations sharing a `[Name]` | [1.5](#15-two-declarations-cannot-share-a-name) |
| `this.Foo is not a function` inside a callback you passed to JS | `Script.Write` placeholder re-evaluated in a nested function | [3.1](#31-scriptwrite-placeholders-are-textual-substitution) |
| An async result silently vanishes; no error | `await` on an `IPromise` | [2.2](#22-never-await-an-ipromise) |
| A member access works in Debug and breaks in Release | reading a plain class's members from JavaScript | [2.4](#24-only-objectliteral-member-names-survive-minification) |
| Blank page, one console error, `node --check` passes | compiler newer than the pinned runtime | [4.1](#41-the-compiler-tool-and-the-bcl-must-move-together) |
| `tss.UI.VStack is not a function` (or any `X is not a function` on a package type) | the package ships chunked lazy modules | [4.2](#42-chunked-lazy-modules-turn-every-type-into-a-stub) |
| A JS enum arrives as a string like `"Warning"` | enum without `[Enum(Emit.Value)]` | [2.3](#23-enums-crossing-into-javascript-need-enumemitvalue) |
| A property is emitted camel-cased (`.alt` for `.Alt`) | `[External]` type without `[Convention(Notation.None)]` | [1.2](#12-conventionnotationnone-on-every-external-type) |
| Nothing renders and nothing errors | `Children(...)` called more than once on one stack | [5.2](#52-children-replaces-and-chaining-it-does-not-append) |
| A component removed from the DOM never comes back | `DomObserver.WhenMounted` fires once | [5.1](#51-the-mount-lifecycle-fires-once-re-arm-it-for-remounting) |

---

## 1. Declaring foreign APIs

### 1.1 Prefer `[External]` declarations to script strings

`[External]` types emit **nothing**. A call site compiles straight to the JavaScript it names, so
`MonacoApi.editor.create(container, options)` becomes `monaco.editor.create(container, options)` with no
runtime shim, no reflection and no cost. The win is that a typo or a wrong argument becomes a *build*
error rather than a runtime one.

```csharp
[External]
[Convention(Notation.None)]
[Name("monaco")]
public static class MonacoApi
{
    public static extern IEditorApi editor { get; }
}

[External]
[Convention(Notation.None)]
public interface IEditorApi
{
    IStandaloneCodeEditor create(HTMLElement container, EditorOptions options);
}
```

Adding members to such an interface costs nothing at runtime, so declare generously. Converting a
codebase of 243 `Script.Write` strings this way turned two latent runtime faults into compile errors
immediately — that is the actual payoff, not elegance. **(measured)**

The same trick declares browser globals:

```csharp
[External][Convention(Notation.None)][Name("JSON")]
internal static class JsJson
{
    public static extern string stringify(object value);
    public static extern object parse(string text);
}

[External][Convention(Notation.None)][Name("Uint32Array")]
public class Uint32Array
{
    public extern Uint32Array(uint[] source);
}
```

Both emit exactly `JSON.stringify(x)` and `new Uint32Array(...)`. **(measured)**

### 1.2 `[Convention(Notation.None)]` on every `[External]` type

Without it the compiler camel-cases members, and `monaco.KeyMod.Alt` is emitted as `monaco.KeyMod.alt`.
`[ObjectLiteral]` *fields* are already left alone, so those only need `[Name]` when the C# name has to
differ from the JavaScript one.

### 1.3 `as` and `is` do not work on an `[External]` type

**Nothing is emitted for an `[External]` type, so there is no metadata to test against.** `as` and `is`
compile to a runtime type test that reads `constructor` off that missing metadata and **throws** rather
than answering `false`:

```
TypeError: Cannot read properties of undefined (reading 'constructor')
    at Object.is (tps.js)
    at Object.as (tps.js)
    at TransposeR.as (tps.shim.js)
```

A single `Instance as IStandaloneCodeEditor` took out every editor's initialisation this way. A **direct
cast** emits nothing and is correct:

```csharp
var editor = (IStandaloneCodeEditor)Instance;   // fine
var editor = Instance as IStandaloneCodeEditor; // throws
```

Guard with a plain `null` check first. **(measured)**

### 1.4 A BCL generic over an `[External]` type cannot be constructed

Same root cause, different crater. `new List<IJsDisposable>()` fails inside the runtime's `genericName`
with `Cannot read properties of undefined (reading '$$name')`, because the list wants to record its
element type.

Hold something else. Storing release closures is usually both a fix and an improvement:

```csharp
private readonly List<Action> _releases = new List<Action>();

public void Add(IJsDisposable disposable)
{
    if (disposable is null) return;

    _releases.Add(() => disposable.dispose());
}
```

`[ObjectLiteral]` types **are** fine as generic arguments (`List<ThemeRule>` works) — this is specific
to `[External]`. **(measured)**

### 1.5 Two declarations cannot share a `[Name]`

The second is emitted with a `$1` suffix and quietly stops matching the JavaScript. So you cannot model
a JavaScript function that is overloaded on argument *type* with two C# overloads.

Two workarounds, both used here:

- Declare it once with the union typed as `object`, and put the typed overloads in ordinary C# above it:
  `void setSelection(object rangeOrSelection);`
- Give it a distinct C# name and let `[Name]` map it: `[Name("getOption")] double getNumberOption(int id);`

And beware optional parameters: rather than two `createModel` declarations, declare the longest form and
pass `null` for what you do not have — most JavaScript treats `null` and `undefined` alike here.

### 1.6 `[Name]` cannot express a dotted key

`[Name("editor.background")]` on an `[ObjectLiteral]` field emits `$o.editor.background = …`, a nested
access. For genuinely dotted keys — theme colours, `semanticHighlighting.enabled` — use
`Script.Set(obj, "a.b", value)`. Keep that the *only* place a JavaScript name survives as a string, and
wrap it:

```csharp
public static ThemeColors Set(this ThemeColors colors, string key, string color)
{
    Script.Set(colors, key, color);

    return colors;
}
```

### 1.7 `[ObjectLiteral]` emits only the fields you assign

This is the most useful property in the whole system. One type can serve both construction and a partial
update, where anything unmentioned must stay untouched: `new EditorOptions { readOnly = true }` is
exactly `{ readOnly: true }`.

It enables a pattern worth stealing. Instead of recording option changes as name/value pairs, record a
**mutator**, and run it twice:

```csharp
protected T Option(Action<EditorOptions> set)
{
    set(_constructionOptions);              // the initial value

    if (_live is object)
    {
        var patch = new EditorOptions();
        set(patch);                         // exactly the fields this setter touches
        _live.updateOptions(patch);
    }

    return Self;
}

public T FontSize(double px) => Option(o => o.fontSize = px);
```

Thirty-five typed option setters, one line each, no option names in strings, and the update path is
precise by construction.

---

## 2. Values crossing the boundary

### 2.1 A C# array cannot cross into a web worker

Transpose stamps a **`$type` property holding a function** onto typed arrays, for element-type
bookkeeping. `Array.isArray` is still true and the elements are intact, but `structuredClone` and
`postMessage` refuse the whole value. The failure surfaces from inside whatever library posted it, as a
`DataCloneError` that quotes a function body and names nothing useful.

```js
Object.getOwnPropertyNames(someCSharpArray)  // ["0", "length", "$type"]
typeof someCSharpArray.$type                 // "function"
```

`Script.ToArray(x)` strips it and is the cheap fix. For a whole object graph — a JSON schema handed to a
language service, say — a JSON round trip is the blunt instrument:

```csharp
public static object ToPlainObject(object value)
{
    return value is null ? null : JsJson.parse(JsJson.stringify(value));
}
```

Anonymous types have the same problem for a different reason: they are emitted as real classes unless
the project's `tps.json` sets `rules.anonymousType: "Plain"`. That is per project, so a *library* cannot
assume a *host* has set it — normalise anything you forward. **(measured)**

### 2.2 Never `await` an `IPromise`

Its awaiter is typed as handing back the resolved values as `object[]`, but the runtime adapter passes a
native promise straight through. So the awaited value is the single resolved value, reading `.Length`
off it yields `undefined`, and **the result silently vanishes with no error**. That is exactly what a
broken hover provider looked like: no exception, just no tooltip.

Use `.Then(onFulfilled, onRejected, null)` with `Action<object>` handlers. To bridge into a `Task`, use a
`TaskCompletionSource` — there is no `FromPromise` on `PromiseExtensions`:

```csharp
var done = new TaskCompletionSource<string>();

somePromise.Then(
    new Action<object>(v => done.TrySetResult(v as string ?? "")),
    new Action<object>(e => done.TrySetException(e as Exception ?? new Exception("failed: " + e))),
    null);

return done.Task;
```

The other direction is fine: awaiting a `Task` works, and the runtime's own adapter turns one into a
promise. **(measured)**

### 2.3 Enums crossing into JavaScript need `[Enum(Emit.Value)]`

Otherwise the member *name* is emitted and the receiving library sees `"Warning"` where it wanted `4`.

### 2.4 Only `[ObjectLiteral]` member names survive minification

A Release build minifies member names. So this is safe:

```csharp
[ObjectLiteral] public class Position { public int lineNumber; public int column; }
```

and this is a trap — `w.Id` may not exist in Release:

```csharp
Script.Write("(function(w){ return w.Id; })({0})", myPlainClassInstance);
```

Rules of thumb: anything a foreign library reads by name is `[ObjectLiteral]`; anything you must read
from a plain C# object inside JavaScript is passed as a **delegate** instead:

```csharp
Func<string> getId = () => Id;   // then call getId() from the JS side
```

Auto-properties do emit as same-named fields (`public int X { get; set; }` → `X: 0`), which is why this
appears to work in Debug. **(measured)**

### 2.5 `ReadOnlyArray<T>` is the array itself at runtime

Its `op_Implicit` returns the data, so it crosses with no copy and converts implicitly from `T[]` — a
good parameter type for a public API. It has no `.ToArray()`, though; if you need a `T[]`, take one.

---

## 3. If you still reach for `Script.*`

A mature Transpose codebase should have almost no `Script.Write`. Where it remains, one rule dominates.

### 3.1 `Script.Write` placeholders are textual substitution

`{0}` is replaced with the **emitted expression**, wherever it lands — it is not an evaluated argument.
So a method call whose placeholder sits inside a nested JavaScript `function` body is re-evaluated
*there*, against that function's `this`:

```csharp
// Emits `this.Gate(fn)` INSIDE provideX, where `this` is the provider object → TypeError
Script.Write("{ provideX: function(m, p) { return {0}(m, p); } }", Gate(fn));
```

Hoist anything non-trivial into a local first; a local emits as a bare identifier and is safe anywhere:

```csharp
var gated = Gate(fn);

Script.Write("{ provideX: function(m, p) { return {0}(m, p); } }", gated);
```

IIFE *arguments* are fine — `})({0}, {1})` evaluates in the enclosing scope, at the point you wrote it.
**(measured)**

### 3.2 A void `Script.Write` in an expression-bodied lambda emits `return <js>`

Which is a syntax error when the JavaScript is a statement. Use a block body for any `void` `Script.*`
call.

### 3.3 The useful primitives

`Script.Set(obj, key, value)` for a computed or dotted key, `Script.ToArray(x)` for the `$type` problem,
`Script.InstanceOf(x, typeof(T))` for a real JavaScript `instanceof`, `Script.Undefined`.

---

## 4. Toolchain and versioning

### 4.1 The compiler tool and the BCL must move together

The compiler emits calls into the `tps.js` runtime that the BCL package ships, so a newer compiler can
emit a call the pinned runtime does not have. Updating only the tool produced a blank page: the compiler
emitted `Transpose.anon(...)` for anonymous objects and the pinned runtime had no `anon`.

The signature of this class of failure: **blank page, exactly one console error, and `node --check`
passes** — the JavaScript is syntactically fine, the callee just does not exist.

Note the version lines move independently; `Transpose.BCL 26.8.x` alongside `Transpose.Core 26.7.x` is
normal, so "the numbers differ" is not itself the bug.

### 4.2 Chunked lazy modules turn every type into a stub

A package built by a sufficiently new compiler may ship its JavaScript as an ES module that only calls
`Transpose.Modules.register({...})`, mapping every type to a file under `chunks/`, and leaves the types
themselves as **stubs**. Reflection still works — `Assembly.GetTypes()`, `Type.Name`, `IsAssignableFrom`
all see the type — but *using* one requires a fetch, so it is reachable only through the async API
(`Transpose.Modules.load`, `Activator.CreateInstanceAsync`).

Synchronous use throws `X is not a function` and the page renders nothing. Diagnose it in seconds:

```bash
grep -c 'chunks/' path/to/output/tss.js     # 0 = classic script, hundreds = chunked
```

`"loader": { "type": "Global" }` in your `tps.json` does **not** help: that governs what *your* project
emits, not how a referenced package's shipped resource loads. Either pin back to a non-chunked build or
teach your entry point to await the module load before touching any package type. **(measured)**

### 4.3 Build Release once before shipping

`outputFormatting: "Both"` means resources are also emitted as `.min.js`, and Release selects those. A
Debug-only pass does not prove the minified set is wired up — nor does it exercise [2.4](#24-only-objectliteral-member-names-survive-minification).

---

## 5. Tesserae

### 5.1 The mount lifecycle fires once — re-arm it for remounting

A component that wraps a library needing a live DOM node creates it on mount:

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

`WhenMounted` and `WhenRemoved` each fire **once**. If teardown on `WhenRemoved` does not re-arm
`WhenMounted`, a component removed from the DOM and re-added renders an empty container ever after — and
never says why. Tearing down without re-arming leaks; re-arming without tearing down leaks harder. Do
both, and keep an explicit `Dispose()` as the one-way door:

```csharp
private void HandleRemoved()
{
    if (_disposed) return;

    Teardown();
    ArmMountObserver();   // a re-added container rebuilds and replays its configuration
}
```

Anything the user can change — text, scroll position, caret — should be captured on teardown and
restored on the next create, or a remount silently reverts it. **(measured)**

### 5.2 `Children(...)` replaces, and chaining it does not append

```csharp
var content = VStack().Children(a, b);

foreach (var extra in more) content.Children(extra);   // renders NOTHING extra, silently
```

Build one list and pass it once:

```csharp
var sections = new List<IComponent> { a, b };
sections.AddRange(more);

var content = VStack().Children(sections.ToArray());
```

This fails with no error and no console output, which makes it expensive to find. **(measured)**

### 5.3 A component that owns its own sizing

Implement `ISpecialCaseStyling` so the Tesserae sizing helpers style the container you control rather
than a wrapper:

```csharp
public HTMLElement StylingContainer      => _container;
public bool        PropagateToStackItemParent => false;   // sizing stays on the container
```

### 5.4 Resolving a theme token to a real colour

Tesserae's theme values are CSS variables, so a library wanting a concrete hex needs to resolve one:

```csharp
var hex = Color.FromString(Color.EvalVar(Theme.Secondary.Background)).ToHex();
```

`Theme.IsDark` is the light/dark switch. If you derive anything from the theme, expose a way to
recompute it — a host may switch themes at runtime.

### 5.5 Probe the API rather than guessing

The available helpers vary by version. In this one `Raw(HTMLElement)`, `Toggle`, `Label` and
`element.parentElement` exist and `Checkbox` does not. Rather than guess, write a throwaway file that
uses the candidates and read the compiler's errors — one build tells you exactly what exists:

```csharp
internal static class Probe
{
    internal static void Try()
    {
        IComponent a = Raw(DIV());
        IComponent b = Checkbox("x");   // error CS0103 → it does not exist
    }
}
```

Delete it afterwards. This is faster and more reliable than reading a decompiled assembly.

---

## 6. How to actually verify Transpose work

`dotnet build` proves it compiles. It does not prove any of the above.

1. **Read the emitted JavaScript** for anything you were unsure about. Every rule in this document came
   out of doing that. `node --check out/YourAssembly.js` catches emit bugs long before they become
   confusing runtime errors.
2. **Drive it in a browser.** Serve the build output and use Playwright. Most Transpose/interop failures
   are runtime-only and many are *silent*.
3. **A/B against a clean baseline** before believing you broke something. Build the unmodified commit in
   a second worktree and compare — that is how a blank page was traced to a dependency rather than to
   the change in progress.
4. **Bisect dependency versions by grepping build output**, not by reasoning about changelogs. One
   `grep -c` over the emitted JavaScript per candidate version settles it.
5. **Poll for the thing you are measuring; do not sleep once.** Worker-produced results — diagnostics, a
   computed diff — arrive well after the edit that caused them, so a read straight after typing sees the
   previous state. Prefer the library's own "it changed" event over a timeout.
6. **Be careful what "visible" means.** A popup rendered into a zero-sized host element has a null
   `offsetParent` while being perfectly on screen; filter on a non-zero `getBoundingClientRect()`
   instead. A wrong visibility test costs a round of debugging a feature that already worked.

---

## 7. The shape that worked

For a library wrapping a big JavaScript component, one arrangement paid off repeatedly:

- **Declare the foreign API** in one place (`src/Interop/`), and the payload types in another
  (`src/Types/`). Nothing else touches the boundary.
- **Implement each operation exactly once** against the declared interface, in a class that wraps a
  *live* instance. Expose that class.
- **Put buffering in a separate facade.** Callers configure a component long before it is mounted, so the
  facade records calls and replays them once the instance exists — and again after a remount. Separating
  "what the operation is" from "when it can run" keeps both simple, and lets sub-parts of a composite
  (a diff editor's two panes, say) reuse the operations with no extra code.
- **Distinguish standing configuration from transient acts.** An event subscription or an option should
  be replayed on remount; focusing or scrolling should be dropped if there is nothing to focus yet.
  Replaying the second kind at the wrong moment is a real bug.
- **Centralise whatever every callback needs** — a gate so global registrations answer only for their own
  instance, and a bag so every handle is released on teardown. Twenty callbacks then cost a few lines
  each instead of repeating the same bookkeeping twenty times.
