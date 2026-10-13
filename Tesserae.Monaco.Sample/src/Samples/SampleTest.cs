using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tesserae;
using static Transpose.Core.dom;
using static Tesserae.UI;

namespace Tesserae.Monaco.Sample
{
    /// <summary>
    /// A test a gallery page runs on itself: a button, a summary line and one ok/FAIL line per check,
    /// plus the helpers every such test needs to read Monaco from inside the page. The page supplies
    /// the body; this owns the bookkeeping, the waits and the error capture.
    ///
    /// Stable hooks for a browser check: the summary carries <see cref="SUMMARY_CLASS"/> and reads
    /// <c>passed: N checks</c> or <c>FAILED: n of N checks</c>; each line carries
    /// <see cref="OK_CLASS"/> or <see cref="FAIL_CLASS"/>, and a failing line is also written to the
    /// console.
    /// </summary>
    public sealed class SampleTest : IComponent
    {
        public const string SUMMARY_CLASS = "tssm-test-summary";
        public const string OK_CLASS      = "tssm-test-ok";
        public const string FAIL_CLASS    = "tssm-test-fail";

        private const int POLL_MS    = 50;
        private const int TIMEOUT_MS = 15_000;
        private const int POPUP_MS   = 3_000;

        private readonly Func<SampleTest, Task> _body;
        private readonly Action                 _cleanup;
        private readonly Button                 _run;
        private readonly TextBlock              _summary = TextBlock("not run yet").SemiBold().Class(SUMMARY_CLASS);
        private readonly Stack                  _results = VStack().WS();
        private readonly List<string>           _errors  = new List<string>();
        private readonly IComponent             _content;

        private bool _running;
        private int  _checks;
        private int  _failures;

        /// <param name="label">The button's text.</param>
        /// <param name="body">The test. Throwing fails it with the exception's message.</param>
        /// <param name="cleanup">Runs after the body however it ended, to put the page back.</param>
        public SampleTest(string label, Func<SampleTest, Task> body, Action cleanup = null)
        {
            _body    = body;
            _cleanup = cleanup;
            _run     = Button(label).Primary().SetIcon(UIcons.Play).OnClick(() => RunAsync().FireAndForget());

            _content = VStack().WS().Children(
                HStack().WS().Wrap().Gap(8.px()).AlignItemsCenter().Children(_run, _summary),
                _results.PT(8));
        }

        public HTMLElement Render() => _content.Render();

        private async Task RunAsync()
        {
            if (_running) return;

            _running  = true;
            _checks   = 0;
            _failures = 0;
            _errors.Clear();
            _results.Clear();
            _run.Disabled();
            _summary.Text = "running...";

            window.addEventListener("error", OnPageError);
            window.addEventListener("unhandledrejection", OnUnhandledRejection);

            try
            {
                await _body(this);
            }
            catch (Exception e)
            {
                Check(false, "the test threw: " + e.Message);
            }
            finally
            {
                window.removeEventListener("error", OnPageError);
                window.removeEventListener("unhandledrejection", OnUnhandledRejection);

                try
                {
                    _cleanup?.Invoke();
                }
                catch (Exception e)
                {
                    Check(false, "cleaning up threw: " + e.Message);
                }

                _running = false;
                _run.Disabled(false);
                _summary.Text = _failures == 0
                    ? "passed: " + _checks + " checks"
                    : "FAILED: " + _failures + " of " + _checks + " checks";
            }
        }

        // -----------------------------------------------------------------------------------------
        // Reporting.

        public void Section(string title) => _results.Add(TextBlock(title).SemiBold().PT(8));

        public void Check(bool ok, string what, string[] detail) => Check(ok, what, "[" + string.Join(", ", detail) + "]");

        public void Check(bool ok, string what, string detail = null)
        {
            _checks++;

            if (!ok) _failures++;

            var line = (ok ? "ok    " : "FAIL  ") + what + (ok || detail is null ? "" : " - " + detail);

            _results.Add(TextBlock(line).Small().Class(ok ? OK_CLASS : FAIL_CLASS).Foreground(ok ? Theme.Secondary.Foreground : Theme.Danger.Background));

            if (!ok) console.error("sample test: " + line);
        }

        /// <summary>One check that no uncaught error or unhandled rejection arrived since the last one.</summary>
        public void CheckNoErrors(string during)
        {
            Check(_errors.Count == 0, "no errors during " + during, _errors.Count == 0 ? null : string.Join(" | ", _errors));
            _errors.Clear();
        }

        private void OnPageError(Event e) => _errors.Add("uncaught error: " + (((ErrorEvent)e).message ?? "(no message)"));

        private void OnUnhandledRejection(Event e) => _errors.Add("unhandled rejection");

        // -----------------------------------------------------------------------------------------
        // Waiting.

        /// <summary>Polls until the condition holds, as a check; a condition that throws counts as not yet.</summary>
        public async Task<bool> WaitUntil(Func<bool> condition, string what)
        {
            for (var waited = 0; waited < TIMEOUT_MS; waited += POLL_MS)
            {
                bool met;

                try
                {
                    met = condition();
                }
                catch
                {
                    met = false;
                }

                if (met)
                {
                    Check(true, what);
                    return true;
                }

                await Task.Delay(POLL_MS);
            }

            Check(false, "timed out waiting for " + what, Snapshot());
            return false;
        }

        // -----------------------------------------------------------------------------------------
        // Reading Monaco from inside the page.

        /// <summary>
        /// Opens the suggest list at the start of the editor's last line, which is empty in every
        /// document the tests use so the provider's words all show, reads the visible list and closes it.
        /// </summary>
        public async Task<string[]> Suggestions(IStandaloneCodeEditor editor)
        {
            if (editor is null) return new string[0];

            editor.focus();
            editor.setPosition(new Position { lineNumber = editor.getModel().getLineCount(), column = 1 });

            try
            {
                editor.trigger("sample-test", "editor.action.triggerSuggest", null);
            }
            catch (Exception e)
            {
                _errors.Add("triggerSuggest threw: " + e.Message);
            }

            var words = new string[0];

            for (var waited = 0; waited < POPUP_MS && words.Length == 0; waited += POLL_MS)
            {
                await Task.Delay(POLL_MS);
                words = VisibleTexts(".suggest-widget", ".monaco-list-row .label-name");
            }

            editor.trigger("sample-test", "hideSuggestWidget", null);

            return words;
        }

        /// <summary>Shows the hover on the first occurrence of a word and returns the visible hover's text.</summary>
        public async Task<string> HoverText(IStandaloneCodeEditor editor, string word)
        {
            if (editor is null) return "";

            var match = editor.getModel().findMatches(word, false, false, true, null, false, 1);

            if (match.Length == 0) return "";

            editor.focus();
            editor.setPosition(new Position { lineNumber = match[0].range.startLineNumber, column = match[0].range.startColumn + 1 });
            editor.trigger("sample-test", "editor.action.showHover", null);

            var text = "";

            for (var waited = 0; waited < POPUP_MS && text.Length == 0; waited += POLL_MS)
            {
                await Task.Delay(POLL_MS);
                text = string.Join(" ", VisibleTexts(".monaco-hover", null));
            }

            MonacoEditor.HideHovers();

            return text;
        }

        /// <summary>
        /// The text of every visible match. Every editor owns its own popups inside the shared overflow
        /// host, so the first match in the document is usually some other editor's hidden one, and
        /// Monaco hides a popup with <c>display: none</c>, which is a zero <c>offsetHeight</c>.
        /// </summary>
        public static string[] VisibleTexts(string popupSelector, string itemSelector)
        {
            var texts  = new List<string>();
            var popups = document.querySelectorAll(popupSelector);

            for (uint i = 0; i < popups.length; i++)
            {
                var popup = (HTMLElement)popups[i];

                if (popup.offsetHeight <= 0) continue;

                if (itemSelector is null)
                {
                    texts.Add(popup.textContent ?? "");
                    continue;
                }

                var items = popup.querySelectorAll(itemSelector);

                for (uint j = 0; j < items.length; j++) texts.Add((items[j].textContent ?? "").Trim());
            }

            return texts.ToArray();
        }

        /// <summary>The editors whose document contains a marker - a test's way of naming one editor.</summary>
        public static IStandaloneCodeEditor[] EditorsWith(string marker)
        {
            return MonacoEditor.GetEditors().Where(e => e.getModel() is object && e.getModel().getValue().Contains(marker)).ToArray();
        }

        public static IStandaloneCodeEditor FindEditor(string marker) => EditorsWith(marker).FirstOrDefault();

        public static int EditorCount() => MonacoEditor.GetEditors().Length;

        public static int ModelCount() => MonacoEditor.GetModels().Length;

        /// <summary>Diff editors, counted in the DOM because <c>monaco.editor.getDiffEditors</c> is not declared.</summary>
        public static int DiffNodeCount() => (int)document.querySelectorAll(".monaco-diff-editor").length;

        /// <summary>Everything "back to the baseline" compares, as one string so a mismatch reads at a glance.</summary>
        public static string Snapshot()
        {
            var host        = (HTMLElement)document.querySelector("[data-monaco-overflow-host=\"1\"]");
            var editorNodes = document.querySelectorAll(".monaco-editor");
            var nodes       = 0;

            for (uint i = 0; i < editorNodes.length; i++)
            {
                if (editorNodes[i] != host) nodes++;
            }

            return "editors " + EditorCount() +
                   ", diff editors " + DiffNodeCount() +
                   ", models " + ModelCount() +
                   ", editor nodes " + nodes +
                   ", popup host children " + (host is object ? (int)host.children.length : 0);
        }
    }
}
