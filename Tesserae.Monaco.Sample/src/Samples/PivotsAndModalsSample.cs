using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;
using static Tesserae.Monaco.Sample.SamplesHelper;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// Editors inside the two Tesserae containers that take their content out of view: a <c>Pivot</c>,
    /// whose tabs are either cached (hidden) or not (removed from the DOM), and a <c>Modal</c>, which is
    /// removed when it hides. Each case is drawn twice where it matters - an editor component created
    /// once, outside the container, and handed to it again every time - and the page tests what each one
    /// should and should not keep.
    /// </summary>
    [SampleDetails(Group = "Runtime and hosting", Order = 12, Icon = UIcons.FolderOpen)]
    public class PivotsAndModalsSample : IComponent, ISample
    {
        private const int ROUNDS = 3;

        private const string CODE  = "code";
        private const string OTHER = "other";

        /// <summary>How a host puts an editor in a container, which decides what survives it leaving.</summary>
        private enum Hosting
        {
            /// <summary>A cached pivot tab: hidden, never removed, so the very same editor comes back.</summary>
            CachedTab,

            /// <summary>A non-cached tab whose content is one component created outside: torn down, rebuilt from it.</summary>
            TabOutside,

            /// <summary>A non-cached tab whose content is a new component each time: a fresh editor.</summary>
            TabInside,

            /// <summary>One modal shown and hidden again and again, around an editor created outside it.</summary>
            ReusedModal,

            /// <summary>A new modal each time, around the same editor created outside it.</summary>
            FreshModal
        }

        private readonly IComponent _content;
        private readonly SampleTest _test;

        private readonly CodeEditor _cachedEditor;
        private readonly CodeEditor _outsideEditor;
        private readonly CodeEditor _reusedModalEditor;
        private readonly CodeEditor _freshModalEditor;
        private readonly Pivot      _cachedPivot;
        private readonly Pivot      _outsidePivot;
        private readonly Pivot      _insidePivot;
        private readonly Modal      _reusedModal;

        private int   _insideBuilds;
        private Modal _freshModal;

        public PivotsAndModalsSample()
        {
            _cachedEditor      = BuildEditor("cached");
            _outsideEditor     = BuildEditor("outside");
            _reusedModalEditor = BuildEditor("reused");
            _freshModalEditor  = BuildEditor("fresh");

            // The two cases the question is about: the editor is created here, once, and the tab's
            // content lambda hands back that same component every time it is asked.
            _cachedPivot = Pivot().WS().H(180.px())
               .Pivot(CODE,  PivotTitle("cached.cs"), () => _cachedEditor.WS().HS(), cached: true)
               .Pivot(OTHER, PivotTitle("Notes (cached)"), () => Notes(), cached: true);

            _outsidePivot = Pivot().WS().H(180.px())
               .Pivot(CODE,  PivotTitle("outside.cs"), () => _outsideEditor.WS().HS(), cached: false)
               .Pivot(OTHER, PivotTitle("Notes (outside)"), () => Notes(), cached: false);

            // The contrast: a new component every time the tab is shown, so nothing can come back.
            _insidePivot = Pivot().WS().H(180.px())
               .Pivot(CODE,  PivotTitle("inside.cs"), () => BuildEditor("inside", ++_insideBuilds).WS().HS(), cached: false)
               .Pivot(OTHER, PivotTitle("Notes (inside)"), () => Notes(), cached: false);

            _reusedModal = Modal("One modal, shown again").W(60.vw()).H(40.vh()).Content(_reusedModalEditor.WS().HS());

            _test = new SampleTest("Run pivot and modal test", RunAsync, cleanup: () =>
            {
                _reusedModal.Hide();
                _freshModal?.Hide();
                _cachedPivot.Select(CODE);
                _outsidePivot.Select(CODE);
                _insidePivot.Select(CODE);
            });

            _content = SectionStack().Secondary()
               .SampleTitle(typeof(PivotsAndModalsSample), UIcons.FolderOpen, "An editor in a tab or a modal that comes and goes")
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Tabs and modals take their content out of view, and how they do it decides what an editor inside them keeps. A cached Pivot tab is only hidden: the editor stays as it is, undo history included. A non-cached tab and a Modal take their content out of the DOM: the editor is torn down, and built again when the content comes back."),
                        TextBlock("Built again from what, depends on where the editor was created. A component created once, outside the tab or the modal, and handed back to it every time, remembers its text and the user's place in it, so it comes back as it was left - a new Monaco editor with the same document, caret and scroll, and its completion, hover and every other provider registered again. A component created inside the tab's content lambda is a new one each time, so it starts from scratch.").MT(8))).SetTitle("Overview")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Use a cached tab when the editor's undo history has to survive switching tabs: it is the one thing a rebuilt editor cannot carry, because Monaco's undo stack is not serialisable. Use a non-cached tab, or a modal, with the editor created outside when only the document and the caret need to survive and fewer live editors is worth more."),
                        TextBlock("Create the editor inside the content lambda only when a fresh one each time is what you want. Either way, call .Dispose() on an editor created outside once it is finished with for good: leaving the DOM tears it down and re-arms it, Dispose is the one-way door.").MT(8))).SetTitle("Best Practices")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        SampleSubTitle("Cached tab, editor created outside"),
                        TextBlock("Type something, switch to Notes and back: the same editor, caret and undo history."),
                        _cachedPivot.MT(8),
                        SampleSubTitle("Tab not cached, editor created outside"),
                        TextBlock("Type something, switch to Notes and back: a new editor, built from the same component, with your text and caret. Ctrl+Z has nothing to undo."),
                        _outsidePivot.MT(8),
                        SampleSubTitle("Tab not cached, editor created inside"),
                        TextBlock("Type something, switch to Notes and back: a new component, so the document starts over."),
                        _insidePivot.MT(8),
                        SampleSubTitle("Modals around an editor created outside"),
                        TextBlock("The first button shows the same modal every time; the second builds a new modal around the same editor. Either way the editor comes back with what you typed."),
                        HStack().WS().Wrap().Gap(8.px()).PT(8).Children(
                            Button("Show the same modal").SetIcon(UIcons.WindowMaximize).OnClick(() => _reusedModal.Show()),
                            Button("Show a new modal").SetIcon(UIcons.WindowMaximize).OnClick(ShowFreshModal))
                    )).SetTitle("Usage")))
               .FlatSection(VStack().Children(
                    Card(VStack().WS().Children(
                        TextBlock("Run pivot and modal test does " + ROUNDS + " rounds for each of the five cases: edit the document and move the caret, take the editor out of view (switch to the Notes tab, or hide the modal), bring it back, and check what came back - the same editor or a new one, the edit, the caret, whether undo reaches the edit, that the editor is laid out, that its completion and hover answer with its own words, and that exactly one editor holds its document. At the end every count is back to where it started, and an uncaught error at any step fails it."),
                        _test.PT(8),
                        SampleHint("Leave the page alone while it runs: it moves the focus, switches tabs and opens modals.")
                    )).SetTitle("Test")))
               .SeeAlso(typeof(LifecycleSample), typeof(RemountSample), typeof(ModalSample), typeof(MultiEditorSample));
        }

        private void ShowFreshModal()
        {
            _freshModal?.Hide();

            _freshModal = Modal("A new modal, the same editor").W(60.vw()).H(40.vh()).Content(_freshModalEditor.WS().HS());
            _freshModal.Show();
        }

        private static IComponent Notes() => TextBlock("Nothing here - this tab exists so the editor's tab can be switched away from.").Secondary().P(16);

        /// <summary>An editor whose document names it, with completion and hover words of its own.</summary>
        private static CodeEditor BuildEditor(string name, int build = 0)
        {
            var words  = new[] { name + "One", name + "Two" };
            var editor = MonacoEditor.Editor()
               .SetLanguage("csharp")
               .SetText(Marker(name) + (build > 0 ? " - build " + build : "") + "\nvar value = " + words[0] + ";\n");

            editor.OnCompletion(context => Task.FromResult(words.Select(word => new CompletionItem
            {
                label  = word,
                kind   = CompletionItemKind.Variable,
                detail = "from " + name
            }).ToArray()));

            editor.OnHover(context => Task.FromResult(context.Word is object && words.Contains(context.Word) ? "**" + context.Word + "**\n\nDocumented by " + name + "." : null));

            return editor;
        }

        private static string Marker(string name) => "// " + name + ".cs";

        // -----------------------------------------------------------------------------------------
        // The test.

        private async Task RunAsync(SampleTest t)
        {
            _cachedPivot.Select(CODE);
            _outsidePivot.Select(CODE);
            _insidePivot.Select(CODE);

            // The three tabs' editors; the modals are hidden.
            await t.WaitUntil(() => SampleTest.EditorCount() == 3, "the three tabs' editors mounted");
            await Task.Delay(300);

            var baseline = SampleTest.Snapshot();
            t.Check(true, "baseline: " + baseline);

            await CaseAsync(t, Hosting.CachedTab,   "cached",  "Cached tab, editor created outside");
            await CaseAsync(t, Hosting.TabOutside,  "outside", "Tab not cached, editor created outside");
            await CaseAsync(t, Hosting.TabInside,   "inside",  "Tab not cached, editor created inside");
            await CaseAsync(t, Hosting.ReusedModal, "reused",  "The same modal, shown again");
            await CaseAsync(t, Hosting.FreshModal,  "fresh",   "A new modal each time, the same editor");

            await t.WaitUntil(() => SampleTest.EditorCount() == 3, "back to the three tabs' editors");
            await Task.Delay(300);

            var after = SampleTest.Snapshot();
            t.Check(after == baseline, "back to the baseline", after);
            t.CheckNoErrors("the whole run");
        }

        private async Task CaseAsync(SampleTest t, Hosting hosting, string name, string title)
        {
            t.Section(title);

            var marker  = Marker(name);
            var isModal = hosting == Hosting.ReusedModal || hosting == Hosting.FreshModal;

            if (isModal)
            {
                Show(hosting);
            }

            await t.WaitUntil(() => IsShown(marker), name + ": the editor is on screen");

            for (var round = 1; round <= ROUNDS; round++)
            {
                var at     = name + ", round " + round + ": ";
                var editor = SampleTest.FindEditor(marker);

                if (editor is null)
                {
                    t.Check(false, at + "an editor holds " + marker);
                    return;
                }

                var before     = editor.getId();
                var edit       = "// edited in round " + round;
                var otherCount = SampleTest.EditorCount() - 1;

                // An edit of its own undo step, then the caret somewhere a fresh editor would not put it.
                editor.pushUndoStop();
                editor.executeEdits("sample-test", new[] { new TextEdit { range = new TextRange { startLineNumber = 1, startColumn = 1, endLineNumber = 1, endColumn = 1 }, text = edit + "\n" } });
                editor.pushUndoStop();
                editor.setPosition(new Position { lineNumber = 2, column = 4 });

                // Out of view.
                Hide(hosting);

                if (hosting == Hosting.CachedTab)
                {
                    await Task.Delay(300);
                    t.Check(MonacoEditor.GetEditors().Any(e => e.getId() == before), at + "hidden, the same editor is still alive");
                    t.Check(editor.getDomNode() is object && editor.getDomNode().offsetHeight == 0, at + "and it is not on screen");
                }
                else
                {
                    await t.WaitUntil(() => SampleTest.EditorsWith(marker).Length == 0 && SampleTest.EditorCount() == otherCount, at + "taken out of the DOM, its editor is torn down");
                }

                // Back.
                Show(hosting);
                await t.WaitUntil(() => IsShown(marker), at + "shown again, an editor is on screen");

                var back = SampleTest.FindEditor(marker);

                if (back is null)
                {
                    // Nothing came back: the remaining checks of this case would all fail on it, so the
                    // case ends here and the next one still runs.
                    t.Check(false, at + "an editor came back at all");
                    Hide(hosting);
                    return;
                }

                t.Check(SampleTest.EditorsWith(marker).Length == 1, at + "exactly one editor holds the document", SampleTest.EditorsWith(marker).Length.ToString());

                if (hosting == Hosting.CachedTab)
                {
                    t.Check(back.getId() == before, at + "it is the same editor", back.getId() + " vs " + before);
                }
                else
                {
                    t.Check(back.getId() != before, at + "it is a new editor", back.getId());
                }

                var text = back.getModel().getValue();

                if (hosting == Hosting.TabInside)
                {
                    t.Check(!text.Contains(edit), at + "a new component, so the edit is gone", FirstLine(text));
                }
                else
                {
                    t.Check(text.Contains(edit), at + "the edit is still there", FirstLine(text));

                    var position = back.getPosition();
                    t.Check(position.lineNumber == 2 && position.column == 4, at + "the caret is where it was left", position.lineNumber + ":" + position.column);
                }

                // Polled: Monaco draws its lines on the next animation frame after it is created.
                await t.WaitUntil(() => back.getDomNode().querySelectorAll(".view-line").length > 0 && back.getDomNode().offsetWidth > 0, at + "it is laid out and drawing lines");

                var words = await t.Suggestions(back);
                t.Check(words.Contains(name + "One") && words.Contains(name + "Two") && words.Length == 2, at + "completion offers its own words and nothing else", words);

                var hover = await t.HoverText(back, name + "One");
                t.Check(hover.Contains("Documented by " + name), at + "hover answers", hover);

                // Undo: a hidden editor kept its undo stack; a rebuilt one has nothing to undo, since
                // Monaco's undo stack does not survive the editor.
                if (hosting != Hosting.TabInside)
                {
                    back.trigger("sample-test", "undo", null);
                    var undone = !back.getModel().getValue().Contains(edit);

                    if (hosting == Hosting.CachedTab)
                    {
                        t.Check(undone, at + "undo removes the edit (the undo stack survived)");
                    }
                    else
                    {
                        t.Check(!undone, at + "undo has nothing to remove (a rebuilt editor starts a new undo stack)");
                    }
                }

                t.CheckNoErrors(at.TrimEnd(' ', ':'));
            }

            if (isModal)
            {
                Hide(hosting);
                await t.WaitUntil(() => SampleTest.EditorsWith(marker).Length == 0, name + ": hidden at the end");
            }
        }

        private void Show(Hosting hosting)
        {
            switch (hosting)
            {
                case Hosting.CachedTab:   _cachedPivot.Select(CODE);  break;
                case Hosting.TabOutside:  _outsidePivot.Select(CODE); break;
                case Hosting.TabInside:   _insidePivot.Select(CODE);  break;
                case Hosting.ReusedModal: _reusedModal.Show();        break;
                case Hosting.FreshModal:  ShowFreshModal();           break;
            }
        }

        private void Hide(Hosting hosting)
        {
            switch (hosting)
            {
                case Hosting.CachedTab:   _cachedPivot.Select(OTHER);  break;
                case Hosting.TabOutside:  _outsidePivot.Select(OTHER); break;
                case Hosting.TabInside:   _insidePivot.Select(OTHER);  break;
                case Hosting.ReusedModal: _reusedModal.Hide();         break;
                case Hosting.FreshModal:  _freshModal?.Hide();         break;
            }
        }

        /// <summary>One editor holds the document, and it is drawn at a real size.</summary>
        private static bool IsShown(string marker)
        {
            var editors = SampleTest.EditorsWith(marker);

            return editors.Length == 1 && editors[0].getDomNode() is object && editors[0].getDomNode().offsetHeight > 0;
        }

        private static string FirstLine(string text) => text.Split('\n')[0];

        public HTMLElement Render() => _content.Render();
    }
}
