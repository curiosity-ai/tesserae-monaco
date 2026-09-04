/**
 * Drive the sample gallery's "Plain Objects" page headlessly and fail on any red row.
 *
 * The page is the test: every kind of value the package hands to MonacoEditor.ToPlainObject is
 * normalised there and checked in the browser - structuredClone accepts it, it is a plain graph,
 * it matches what the JSON round trip produced, it shares nothing with its source - and the page
 * marks its result container with data-status="done", data-passed and data-failed, and each row
 * with data-case and data-result. This script only reads those.
 *
 * Usage:
 *   dotnet build Tesserae.Monaco.Sample/Tesserae.Monaco.Sample.csproj
 *   python3 -m http.server 5002 --directory Tesserae.Monaco.Sample/bin/Debug/netstandard2.0/tps &
 *   node scripts/check-plain-objects.mjs http://localhost:5002/
 *
 * Needs the `playwright` package resolvable - either installed locally or, for a global install,
 * `NODE_PATH="$(npm root -g)"`. Chromium is what Playwright ships; do not run `playwright install`
 * where a browser is already provided (PLAYWRIGHT_BROWSERS_PATH).
 */
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { join } from 'node:path';

const BASE    = (process.argv[2] ?? 'http://localhost:5002/').replace(/\/?$/, '/');
const URL     = `${BASE}#/view/Plain%20Objects`;
const TIMEOUT = Number(process.env.PLAIN_OBJECTS_TIMEOUT ?? 60_000);

async function loadPlaywright() {
  try {
    return await import('playwright');
  } catch {
    const root = execSync('npm root -g', { encoding: 'utf8' }).trim();
    const require = createRequire(join(root, 'noop.js'));
    return require('playwright');
  }
}

const { chromium } = await loadPlaywright();
const browser = await chromium.launch();
const page = await browser.newPage();

const consoleErrors = [];
page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
page.on('pageerror', error => consoleErrors.push(`pageerror: ${error.message}`));

let exitCode = 0;

try {
  await page.goto(URL, { waitUntil: 'load' });
  await page.waitForSelector('#plain-objects-results[data-status="done"]', { timeout: TIMEOUT });

  const rows = await page.$$eval('#plain-objects-results [data-case]', elements =>
    elements.map(element => ({
      name: element.getAttribute('data-case'),
      result: element.getAttribute('data-result'),
      failure: element.getAttribute('data-failure'),
    })));

  const passed = Number(await page.getAttribute('#plain-objects-results', 'data-passed'));
  const failed = Number(await page.getAttribute('#plain-objects-results', 'data-failed'));

  for (const row of rows) {
    console.log(`${row.result === 'pass' ? 'PASS' : 'FAIL'}  ${row.name}${row.failure ? `\n      ${row.failure}` : ''}`);
  }

  console.log(`\n${passed} passed, ${failed} failed, ${rows.length} rows`);

  if (rows.length === 0) { console.error('No rows rendered - is the page there?'); exitCode = 1; }
  if (failed > 0 || rows.some(row => row.result !== 'pass')) exitCode = 1;

  if (consoleErrors.length) {
    console.error(`\n${consoleErrors.length} console error(s):`);
    for (const text of consoleErrors) console.error(`  ${text}`);
    exitCode = 1;
  }
} catch (error) {
  console.error(error.message);
  exitCode = 1;
} finally {
  await browser.close();
}

process.exit(exitCode);
