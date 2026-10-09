/**
 * Drives the gallery's Lifecycle page through several rounds of opening and closing every kind of
 * Monaco surface, and fails if anything is left behind or anything still open stops working.
 *
 * Each round, around a "survivor" editor that stays mounted the whole time:
 *
 *   1. Open the editors: a CodeEditor, a CodeViewer and a MultiEditor with two tabs. Check a
 *      keybinding and a validator marker, but complete nothing yet.
 *   2. Open a DiffViewer, wait for the diff worker's line changes, complete on its modified side.
 *   3. Close the diff, then the first completion in a MultiEditor tab.
 *   4. An editor in a modal: open, complete, close.
 *   5. The survivor's history (a diff inside a modal): open, close, then the first completion and
 *      hover in the CodeEditor and the first completion in the second tab.
 *   6. Close all, then check that the page is back to its baseline: editors, diff editors, models,
 *      editor DOM nodes and the shared popup host's children.
 *
 * The survivor - completion and hover in the editor that never closes - is checked after every
 * closing, and every step fails on a page error, a console error or a failed request.
 *
 * Why the first completions are held back: Monaco consults some page-wide state only the first time
 * an editor needs it, and an editor builds its suggest widget the first time completion is asked
 * for. Closing a diff used to leave Monaco's hover-delegate factory bound to the diff's disposed
 * instantiation service, which only threw in an editor building its suggest widget after that, and
 * only while the diff was the newest editor on the page. The survivor completes at page load, so it
 * could never have caught it; the held-back first completions do. Opening the diff on its own,
 * after the editors, is what makes "the newest editor" deterministic.
 *
 * Any page error, console error or failed request fails the run.
 *
 * Usage (after a Release build of the sample):
 *
 *   node scripts/lifecycle-check.mjs [siteDir] [--rounds N] [--headed]
 *
 * siteDir defaults to the Release output. The script serves it itself on a free port. Playwright
 * is imported as `playwright`; set PLAYWRIGHT_MODULE to a path to use an install elsewhere.
 */
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const flag = (name) => args.includes(name);
const option = (name, fallback) => {
  const at = args.indexOf(name);
  return at >= 0 && at + 1 < args.length ? args[at + 1] : fallback;
};

const rounds = Number(option('--rounds', '3'));
const positional = args.filter((arg, i) => !arg.startsWith('--') && !(i > 0 && args[i - 1] === '--rounds'));
const siteDir = resolve(positional[0] || join(here, '../Tesserae.Monaco.Sample/bin/Release/netstandard2.0/tps'));

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(join(process.env.PLAYWRIGHT_MODULE, 'index.mjs')).href : 'playwright');

// ---------------------------------------------------------------------------------------------
// A static server, so the check needs nothing but a built site.

const MIME = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.ttf': 'font/ttf',
  '.woff': 'font/woff', '.woff2': 'font/woff2', '.txt': 'text/plain',
};

const server = createServer(async (request, response) => {
  try {
    let path = decodeURIComponent(new URL(request.url, 'http://x').pathname);
    let file = join(siteDir, path);

    if (!file.startsWith(siteDir)) throw new Error('outside the site');
    if ((await stat(file)).isDirectory()) file = join(file, 'index.html');

    response.writeHead(200, { 'Content-Type': MIME[extname(file)] || 'application/octet-stream' });
    response.end(await readFile(file));
  } catch {
    response.writeHead(404);
    response.end();
  }
});

await new Promise((done) => server.listen(0, '127.0.0.1', done));
const origin = `http://127.0.0.1:${server.address().port}/`;

// ---------------------------------------------------------------------------------------------

const browser = await chromium.launch({ headless: !flag('--headed') });
const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
const problems = [];
let failures = 0;

page.on('pageerror', (error) => problems.push('page error: ' + error.message));
page.on('console', (message) => { if (message.type() === 'error') problems.push('console error: ' + message.text()); });
page.on('requestfailed', (request) => problems.push('request failed: ' + request.url()));
page.on('response', (response) => {
  if (response.status() >= 400 && !response.url().endsWith('/favicon.ico')) problems.push(`HTTP ${response.status()}: ${response.url()}`);
});

const log = (line) => console.log(line);

function check(condition, what, detail) {
  if (condition) {
    log('  ok    ' + what);
  } else {
    failures++;
    log('  FAIL  ' + what + (detail === undefined ? '' : ' - ' + JSON.stringify(detail)));
  }
}

function drainProblems(stage) {
  const found = problems.splice(0);
  check(found.length === 0, `no errors during ${stage}`, found);
}

async function waitFor(predicate, arg, what, timeout = 15000) {
  try {
    await page.waitForFunction(predicate, arg, { timeout, polling: 100 });
    log('  ok    ' + what);
    return true;
  } catch {
    check(false, 'timed out waiting for ' + what);
    return false;
  }
}

/** Everything the "back to baseline" check compares. */
const snapshot = () => page.evaluate(() => {
  const host = document.querySelector('[data-monaco-overflow-host="1"]');
  return {
    editors: monaco.editor.getEditors().length,
    diffEditors: monaco.editor.getDiffEditors().length,
    models: monaco.editor.getModels().length,
    editorNodes: [...document.querySelectorAll('.monaco-editor')].filter((e) => e !== host).length,
    diffNodes: document.querySelectorAll('.monaco-diff-editor').length,
    hostChildren: host ? host.children.length : 0,
  };
});

const editorCount = () => page.evaluate(() => monaco.editor.getEditors().length);

const waitForEditors = (count, what) =>
  waitFor((n) => monaco.editor.getEditors().length === n, count, `${count} editors (${what})`);

/** Index into getEditors() of the editor whose text contains a marker, resolved in the page each time. */
const FIND_EDITOR = `(marker) => monaco.editor.getEditors().find((e) => e.getModel() && e.getModel().getValue().includes(marker))`;

/**
 * Opens the suggest list at the start of the editor's last line, reads the visible list, closes it.
 * The last line is empty in every document here, so the provider's words all show.
 */
async function suggestions(marker) {
  const found = await page.evaluate(([finder, marker]) => {
    const editor = eval(finder)(marker);
    if (!editor) return false;
    const model = editor.getModel();
    editor.focus();
    editor.setPosition({ lineNumber: model.getLineCount(), column: 1 });
    editor.trigger('lifecycle', 'editor.action.triggerSuggest', {});
    return true;
  }, [FIND_EDITOR, marker]);

  if (!found) return null;

  let labels = [];
  for (let i = 0; i < 30 && labels.length === 0; i++) {
    await page.waitForTimeout(100);
    labels = await page.evaluate(() =>
      [...document.querySelectorAll('.suggest-widget')]
        .filter((w) => w.getBoundingClientRect().height > 0)
        .flatMap((w) => [...w.querySelectorAll('.monaco-list-row .label-name')].map((l) => l.textContent.trim())));
  }

  await page.keyboard.press('Escape');
  return labels;
}

/** Shows the hover on a word and returns the visible hover's text. */
async function hover(marker, word) {
  await page.evaluate(([finder, marker, word]) => {
    const editor = eval(finder)(marker);
    const model = editor.getModel();
    const match = model.findMatches(word, false, false, true, null, false)[0];
    editor.focus();
    editor.setPosition({ lineNumber: match.range.startLineNumber, column: match.range.startColumn + 1 });
    editor.trigger('lifecycle', 'editor.action.showHover', {});
  }, [FIND_EDITOR, marker, word]);

  let text = '';
  for (let i = 0; i < 30 && !text; i++) {
    await page.waitForTimeout(100);
    text = await page.evaluate(() =>
      [...document.querySelectorAll('.monaco-hover')]
        .filter((h) => h.getBoundingClientRect().height > 0)
        .map((h) => h.textContent).join(' '));
  }

  await page.keyboard.press('Escape');
  return text;
}

async function checkSurvivor(stage) {
  const labels = await suggestions('survivor.cs');
  check(labels && labels.includes('survivorOne') && labels.includes('survivorTwo'),
    `survivor completion after ${stage}`, labels);
  check(labels && !labels.some((l) => /^(alpha|diff|modal)/.test(l)),
    `survivor completion offers no other editor's words after ${stage}`, labels);

  const text = await hover('survivor.cs', 'survivorOne');
  check(text.includes('Documented by the survivor editor'), `survivor hover after ${stage}`, text);
  drainProblems(stage);
}

const logText = () => page.locator('.tssm-lifecycle-log').textContent();

async function closeModal() {
  await page.keyboard.press('Escape');
  await page.waitForTimeout(200);

  // Escape closes a Tesserae modal; fall back to the close button if this one did not.
  if (await page.locator('.tss-modal').count() > 0) {
    const close = page.locator('.tss-modal .tss-modal-close, .tss-modal button[aria-label="Close"]').first();
    if (await close.count() > 0) await close.click();
  }
}

// ---------------------------------------------------------------------------------------------

try {
  log(`Lifecycle check: ${rounds} round(s) against ${siteDir}`);

  await page.goto(origin + '#/view/Lifecycle');
  await waitFor(() => window.monaco && monaco.editor.getEditors().length === 1, null, 'the survivor editor');
  await page.waitForTimeout(500);

  const baseline = await snapshot();
  log('baseline ' + JSON.stringify(baseline));
  await checkSurvivor('page load');

  for (let round = 1; round <= rounds; round++) {
    log(`\nround ${round}`);

    // 1. The editors: alpha (1) + viewer (1) + two MultiEditor tabs (2). No completion in any of
    //    them yet - each one's first suggest list is saved for straight after a diff has closed.
    await page.getByRole('button', { name: 'Open editors' }).click();
    await waitForEditors(baseline.editors + 4, 'editors open');

    await page.evaluate(`(${FIND_EDITOR})('alpha.cs').focus()`);
    await page.keyboard.press('Control+Alt+KeyK');
    await page.waitForTimeout(200);
    check((await logText()).startsWith('alpha: Ctrl+Alt+K'), 'Ctrl+Alt+K runs the focused editor\'s handler', await logText());
    await waitFor(() => monaco.editor.getModelMarkers({}).some((m) => m.message === 'TODO found by alpha'), null, "alpha's TODO marker");
    drainProblems(`round ${round}: opening the editors`);

    // 2. The diff, opened last so it is the newest editor on the page when it closes.
    await page.getByRole('button', { name: 'Open diff' }).click();
    await waitForEditors(baseline.editors + 6, 'diff open');
    await waitFor(() => {
      const diff = monaco.editor.getDiffEditors()[0];
      return diff && (diff.getLineChanges() || []).length > 0;
    }, null, "the diff worker's line changes");
    const diffLabels = await suggestions('changed on the right');
    check(diffLabels && diffLabels.includes('diffOne'), 'diff modified side completion', diffLabels);
    drainProblems(`round ${round}: using the diff`);

    // 3. Close the diff, then the first completion in an editor that was open all along.
    await page.getByRole('button', { name: 'Close diff' }).click();
    await waitForEditors(baseline.editors + 4, 'diff closed');
    check(await page.evaluate(() => monaco.editor.getDiffEditors().length === 0), 'closing the diff leaves no diff editor');
    const oneLabels = await suggestions('// one.cs');
    check(oneLabels && oneLabels.includes('oneItem'), 'first completion in a MultiEditor tab after the diff closed', oneLabels);
    drainProblems(`round ${round}: first completion after closing the diff`);
    await checkSurvivor(`round ${round}: closing the diff`);

    // 4. An editor in a modal.
    await page.getByRole('button', { name: 'Editor in a modal' }).click();
    await waitForEditors(baseline.editors + 5, 'modal editor open');
    const modalLabels = await suggestions('modal.cs');
    check(modalLabels && modalLabels.includes('modalOne'), 'modal editor completion', modalLabels);
    await closeModal();
    await waitForEditors(baseline.editors + 4, 'modal editor closed');
    await checkSurvivor(`round ${round}: closing the modal`);

    // 5. The survivor's history - a diff inside a modal, and again the newest editor when it closes.
    await page.getByRole('button', { name: 'History of the survivor' }).click();
    await waitForEditors(baseline.editors + 6, 'history diff open');
    await waitFor(() => monaco.editor.getDiffEditors().length === 1, null, 'the history diff');
    await page.waitForTimeout(500);
    await closeModal();
    await waitForEditors(baseline.editors + 4, 'history closed');

    const alphaLabels = await suggestions('alpha.cs');
    check(alphaLabels && alphaLabels.includes('alphaOne') && !alphaLabels.includes('survivorOne'),
      'first completion in alpha after the history closed, and only its own words', alphaLabels);
    const alphaHover = await hover('alpha.cs', 'alphaOne');
    check(alphaHover.includes('Documented by the alpha editor'), 'alpha hover', alphaHover);
    const twoLabels = await suggestions('// two.cs');
    check(twoLabels && twoLabels.includes('twoItem'), 'first completion in the second tab after the history closed', twoLabels);
    drainProblems(`round ${round}: first completion after closing the history`);
    await checkSurvivor(`round ${round}: closing the history`);

    // 6. Close all, and back to the baseline.
    await page.getByRole('button', { name: 'Close all' }).click();
    await waitForEditors(baseline.editors, 'everything closed');
    await page.waitForTimeout(500);

    const after = await snapshot();
    for (const key of Object.keys(baseline)) {
      check(after[key] === baseline[key], `round ${round}: ${key} back to baseline (${baseline[key]})`, after[key]);
    }

    await checkSurvivor(`round ${round}: closing all`);
  }

  // Leaving the page disposes the survivor too: nothing should be left at all.
  await page.goto(origin + '#/view/Colorize');
  await waitFor(() => monaco.editor.getEditors().length === 0, null, 'no editors after leaving the page');
  await page.waitForTimeout(500);
  const left = await snapshot();
  check(left.editors === 0 && left.diffEditors === 0, 'leaving the page leaves no editors', left);
  drainProblems('leaving the page');
} catch (error) {
  failures++;
  log('  FAIL  ' + (error.stack || error));
} finally {
  await browser.close();
  server.close();
}

log(failures === 0 ? '\nlifecycle check passed' : `\nlifecycle check FAILED (${failures})`);
process.exit(failures === 0 ? 0 : 1);
