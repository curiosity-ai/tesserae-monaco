/**
 * Proves the packed Tesserae.Monaco nupkg loads in a consuming app, in every variant a consumer can
 * pick - which the sample cannot, since it takes the package by ProjectReference.
 *
 *   1. Packs Tesserae.Monaco (Release) as 0.0.0-consumer into a temporary feed, and drops any cached
 *      copy of that version so the restore takes the fresh one.
 *   2. Builds tests/PackageConsumer against it three times, each in its own temporary copy:
 *        release-module   Release + "outputBy": "Module" -> ModuleEntry + chunks/Tesserae.Monaco/*.mjs
 *        release-classic  Release without it             -> Tesserae.Monaco.min.js
 *        debug            Debug                           -> Tesserae.Monaco.js, one readable bundle
 *      and checks index.html loads the variant it should.
 *   3. Serves each output and drives it in Chromium: the editors mount and tokenize, the diff gets its
 *      worker decorations, completion lists the consumer's own items and inserts on accept, hover
 *      answers, the multi-editor opens two tabs, the history modal mounts its diff - with no page
 *      error, no console error and no failed request.
 *
 * Usage: node scripts/test-package-consumer.mjs [variant ...]
 * Needs: the Transpose compiler on PATH, node, and the `playwright` package (local or global npm).
 * Exits non-zero on any failure.
 */
import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync, createReadStream } from 'node:fs';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { homedir, tmpdir } from 'node:os';
import { extname, join, resolve } from 'node:path';

const ROOT = resolve(import.meta.dirname, '..');
const VERSION = '0.0.0-consumer'; // must match tests/PackageConsumer/PackageConsumer.csproj
const VARIANTS = {
  'release-module':  { config: 'Release', module: true,  entry: /<script type="module" src="Tesserae\.Monaco\.js">/ },
  'release-classic': { config: 'Release', module: false, entry: /<script src="Tesserae\.Monaco\.min\.js" defer>/ },
  'debug':           { config: 'Debug',   module: true,  entry: /<script src="Tesserae\.Monaco\.js" defer>/ },
};

const selected = process.argv.slice(2).length ? process.argv.slice(2) : Object.keys(VARIANTS);
for (const v of selected) if (!VARIANTS[v]) { console.error(`unknown variant ${v}`); process.exit(2); }

function loadPlaywright() {
  const require = createRequire(import.meta.url);
  try { return require('playwright'); } catch { }
  const globalRoot = execFileSync('npm', ['root', '-g'], { encoding: 'utf8' }).trim();
  return require(join(globalRoot, 'playwright'));
}
const { chromium } = loadPlaywright();

const run = (cmd, args, cwd) => execFileSync(cmd, args, { cwd, stdio: ['ignore', 'pipe', 'pipe'], encoding: 'utf8', maxBuffer: 64 << 20 });

const work = mkdtempSync(join(tmpdir(), 'tssm-consumer-'));
const feed = join(work, 'feed');
let failures = 0;
const check = (ok, label) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${label}`); if (!ok) failures++; };

try {
  console.log(`packing Tesserae.Monaco ${VERSION} into ${feed}`);
  run('dotnet', ['build', join(ROOT, 'Tesserae.Monaco/Tesserae.Monaco.csproj'), '-c', 'Release',
    `-p:PackageVersion=${VERSION}`, `-p:PackageOutputPath=${feed}`], ROOT);

  // tps resolves package references from the global packages folder itself, so a stale copy of this
  // version there would be compiled against instead of the one just packed.
  rmSync(join(process.env.NUGET_PACKAGES || join(homedir(), '.nuget/packages'), 'tesserae.monaco', VERSION),
    { recursive: true, force: true });

  for (const name of selected) {
    const variant = VARIANTS[name];
    console.log(`\n${name}`);
    const dir = join(work, name);
    cpSync(join(ROOT, 'tests/PackageConsumer'), dir, { recursive: true, filter: s => !/[\\/](bin|obj)$/.test(s) });
    writeFileSync(join(dir, 'nuget.config'),
      `<?xml version="1.0" encoding="utf-8"?>\n<configuration><packageSources>` +
      `<add key="local" value="${feed}" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" />` +
      `</packageSources></configuration>\n`);
    if (!variant.module) {
      const tps = join(dir, 'tps.json');
      writeFileSync(tps, readFileSync(tps, 'utf8').replace(/^\s*"outputBy".*\n/m, ''));
    }

    try { run('dotnet', ['build', '-c', variant.config], dir); }
    catch (e) { check(false, `build\n${e.stdout}`); continue; }

    const site = join(dir, 'bin', variant.config, 'netstandard2.0', 'tps');
    const html = readFileSync(join(site, 'index.html'), 'utf8');
    check(variant.entry.test(html), `index.html loads ${variant.entry.source.replace(/\\/g, '')}`);
    if (name === 'release-module') {
      const chunks = join(site, 'chunks/Tesserae.Monaco');
      check(existsSync(chunks) && readdirSync(chunks).some(f => f.endsWith('.mjs')), 'package chunks extracted to chunks/Tesserae.Monaco/');
      check(/<script type="module" src="tss\.js">/.test(html), 'Tesserae itself is loaded as a module');
    }
    check(existsSync(join(site, 'assets/js/monaco/monaco.js')), 'Monaco copied by buildTransitive targets');

    await drive(site, name === 'release-module');
  }
} finally {
  rmSync(work, { recursive: true, force: true });
}

console.log(failures ? `\nFAILED: ${failures} check(s)` : '\npassed');
process.exit(failures ? 1 : 0);

async function drive(site, expectChunks) {
  const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
    '.json': 'application/json', '.ttf': 'font/ttf', '.woff': 'font/woff', '.woff2': 'font/woff2', '.svg': 'image/svg+xml' };
  const server = createServer((req, res) => {
    const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    const file = join(site, path.endsWith('/') ? path + 'index.html' : path);
    if (!file.startsWith(site) || !existsSync(file)) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'content-type': types[extname(file)] || 'application/octet-stream' });
    createReadStream(file).pipe(res);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const url = `http://127.0.0.1:${server.address().port}/`;

  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    const errors = [], requests = [];
    page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
    page.on('console', m => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
    page.on('requestfailed', r => errors.push(`request failed: ${r.url()}`));
    page.on('response', r => {
      const path = new URL(r.url()).pathname;
      if (r.status() >= 400 && path !== '/favicon.ico') errors.push(`${r.status()} ${path}`);
      requests.push(path);
    });
    const poll = async (fn, ms = 15000) => {
      const start = Date.now();
      while (Date.now() - start < ms) { const v = await page.evaluate(fn).catch(() => null); if (v) return v; await page.waitForTimeout(100); }
      return null;
    };

    await page.goto(url);
    const mounted = await poll(() => window.monaco && monaco.editor.getEditors().length === 3);
    check(mounted, 'editor and diff mount (3 Monaco editors)');
    if (!mounted) {
      // Nothing further can be read off a page with no editors.
      check(false, `page errors:\n    ${errors.join('\n    ') || '(none)'}`);
      return;
    }
    check(await poll(() => document.querySelectorAll('.monaco-editor .view-line').length > 0), 'editors draw lines');
    check(await poll(() => document.querySelectorAll('.monaco-diff-editor .line-insert, .monaco-diff-editor .char-insert').length > 0),
      'diff decorations arrive from the editor worker');

    await page.evaluate(() => {
      const e = monaco.editor.getEditors()[0];
      e.focus(); e.setPosition({ lineNumber: 5, column: 1 }); e.trigger('test', 'editor.action.triggerSuggest', {});
    });
    const labels = await poll(() => {
      const w = [...document.querySelectorAll('.suggest-widget')].find(w => w.offsetHeight > 0);
      return w && [...w.querySelectorAll('.monaco-list-row .label-name')].map(x => x.textContent).join(',');
    });
    check(labels === 'consumerOne,consumerTwo', `completion lists the consumer's items (${labels})`);
    await page.keyboard.press('Enter');
    check(await page.evaluate(() => monaco.editor.getEditors()[0].getModel().getLineContent(5)) === 'consumerOne', 'completion inserts on accept');

    await page.evaluate(() => {
      const e = monaco.editor.getEditors()[0];
      e.setPosition({ lineNumber: 3, column: 18 }); e.trigger('test', 'editor.action.showHover', {});
    });
    const hover = await poll(() => [...document.querySelectorAll('.monaco-hover')].find(h => h.getBoundingClientRect().height > 0)?.textContent.trim());
    check(hover === 'hover:Greet', `hover answers (${hover})`);

    await page.click('#open-btn');
    check(await poll(() => monaco.editor.getEditors().length === 5), 'multi-editor opens two tabs with live editors');

    await page.click('#history-btn');
    check(await poll(() => document.querySelectorAll('.tss-modal .monaco-diff-editor').length === 1), 'history modal mounts its diff');

    await page.waitForTimeout(500);
    const packageScripts = requests.filter(p => p.includes('Tesserae.Monaco'));
    if (expectChunks) {
      check(packageScripts.some(p => /^\/chunks\/Tesserae\.Monaco\/.+\.mjs$/.test(p)), `package chunks fetched on demand (${packageScripts.length - 1})`);
      check(!packageScripts.some(p => /\.min\.js$/.test(p)), 'no classic package bundle requested');
    }
    check(errors.length === 0, `clean console and network${errors.length ? '\n    ' + errors.join('\n    ') : ''}`);
  } finally {
    await browser.close();
    server.close();
  }
}
