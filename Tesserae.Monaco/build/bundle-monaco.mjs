/**
 * Bundles the **ESM** build of monaco-editor into the handful of self-contained browser scripts
 * that Tesserae.Monaco ships. Everything is resolved, minified and assembled here so the browser
 * only ever fetches plain IIFE scripts - no module graph, no import map, no runtime patching.
 *
 * Why a bundle step exists at all: Monaco's ESM tree cannot be loaded by a browser directly. It
 * contains ~133 bare `import './x.css'` statements, which browsers reject outright ("Expected a
 * JavaScript-or-Wasm module script but the server responded with a MIME type of text/css"), and it
 * is 1331 modules deep - a request per module even if the CSS problem did not exist. Monaco's own
 * docs describe the ESM build as "compatible with e.g. webpack" for exactly this reason. The
 * alternative, Monaco's prebuilt AMD dist, is deprecated upstream and slated for removal.
 *
 * So esbuild resolves the ESM graph, inlines the CSS, and emits:
 *
 *   monaco.js          the editor as an IIFE exposing `window.monaco`, with Monaco's stylesheet
 *                      and its MonacoEnvironment worker wiring folded in
 *   editor.worker.js   the core web worker
 *   {ts,json,css,html}.worker.js   the language-service workers
 *
 * Nothing here is committed: the output is generated from the pinned npm package on every build
 * (see the BundleMonaco target in Tesserae.Monaco.csproj) and is gitignored.
 *
 * Run with:  npm run bundle   (from the Tesserae.Monaco/ folder)
 */
import { build } from 'esbuild';
import { mkdir, rm, readFile, writeFile, readdir, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const pkgRoot = resolve(here, '..');
const monacoRoot = resolve(pkgRoot, 'node_modules/monaco-editor');
const esm = join(monacoRoot, 'esm/vs');
const outDir = resolve(pkgRoot, 'assets/js/monaco');

await rm(outDir, { recursive: true, force: true });
await mkdir(outDir, { recursive: true });

const common = {
  bundle: true,
  format: 'iife',
  target: 'es2020',
  minify: true,
  legalComments: 'none',
  logLevel: 'warning',
  // codicon.ttf is referenced from Monaco's CSS; inlining it keeps the payload to one CSS file
  // rather than a font that has to be served from a matching relative path.
  loader: { '.ttf': 'dataurl' },
};

// Worker file per Monaco language-service label. Baked into the bundle below rather than assembled
// at runtime, so the shipped script is self-contained.
const workerLabels = {
  typescript: 'ts.worker.js',
  javascript: 'ts.worker.js',
  json:       'json.worker.js',
  css:        'css.worker.js',
  scss:       'css.worker.js',
  less:       'css.worker.js',
  html:       'html.worker.js',
  handlebars: 'html.worker.js',
  razor:      'html.worker.js',
};

// The editor itself. globalName gives the IIFE a `window.monaco`, which is the same shape the
// AMD build produced - so the C# side only ever talks to `monaco.*`.
//
// write:false so the CSS esbuild extracts can be folded into the JS instead of shipped as a second
// file the host has to remember to load.
const editorBuild = await build({
  ...common,
  entryPoints: [join(esm, 'editor/editor.main.js')],
  outfile: join(outDir, 'monaco.js'),
  globalName: 'monaco',
  write: false,
});

const editorJs  = editorBuild.outputFiles.find((f) => f.path.endsWith('.js')).text;
const editorCss = editorBuild.outputFiles.find((f) => f.path.endsWith('.css'))?.text ?? '';

/**
 * Everything the editor needs in place before it evaluates, resolved from where this very script was
 * served rather than from anything the host has to pass in:
 *
 *   - Monaco's stylesheet, injected as a <style>. Shipping it inside the JS removes a second request,
 *     removes any chance of the editor painting unstyled, and means the host cannot forget it.
 *   - MonacoEnvironment.getWorker. Monaco reads this the first time it needs a worker and there is no
 *     way to supply it afterwards, so having the bundle set it up front removes an ordering
 *     constraint that used to be the caller's problem. Guarded by `||`, so a host that wants its own
 *     worker strategy can still set MonacoEnvironment before loading this file.
 *
 * document.currentScript is valid here because the prelude runs during this script's own evaluation.
 */
const prelude = `(function () {
  var src = (document.currentScript && document.currentScript.src) || '';
  var base = src ? src.replace(/\\/[^\\/]*$/, '') : '';

  if (!document.querySelector('style[data-tssm-monaco]')) {
    var style = document.createElement('style');
    style.setAttribute('data-tssm-monaco', '');
    style.textContent = ${JSON.stringify(editorCss)};
    document.head.appendChild(style);
  }

  var workers = ${JSON.stringify(workerLabels)};

  window.MonacoEnvironment = window.MonacoEnvironment || {
    getWorker: function (moduleId, label) {
      var url = base + '/' + (workers[label] || 'editor.worker.js');

      // The Worker constructor rejects a cross-origin script outright, so when the bundle is served
      // from another origin (a CDN) the URL goes through a same-origin blob that importScripts it -
      // the workaround Monaco's own docs prescribe. Only used when needed, since a strict CSP may
      // forbid blob workers.
      if (new URL(url, document.baseURI).origin !== window.location.origin) {
        url = URL.createObjectURL(new Blob(['importScripts(' + JSON.stringify(url) + ');'], { type: 'text/javascript' }));
      }

      return new Worker(url, { name: label });
    }
  };
})();
`;

await writeFile(join(outDir, 'monaco.js'), prelude + editorJs);

// Workers are classic (non-module) scripts: MonacoEnvironment.getWorkerUrl hands back a URL that
// Monaco loads as a plain Worker, so each one has to be a standalone IIFE with no imports left.
const workerEntries = {
  'editor.worker.js': 'editor/editor.worker.js',
  'ts.worker.js':     'language/typescript/ts.worker.js',
  'json.worker.js':   'language/json/json.worker.js',
  'css.worker.js':    'language/css/css.worker.js',
  'html.worker.js':   'language/html/html.worker.js',
};

for (const [outName, entry] of Object.entries(workerEntries)) {
  await build({ ...common, entryPoints: [join(esm, entry)], outfile: join(outDir, outName) });
}

// Monaco is MIT-licensed; ship its license text alongside the code it covers.
const version = JSON.parse(await readFile(join(monacoRoot, 'package.json'), 'utf8')).version;
const license = await readFile(join(monacoRoot, 'LICENSE'), 'utf8');

await writeFile(
  join(outDir, 'LICENSE.txt'),
  `monaco-editor ${version}\nhttps://github.com/microsoft/monaco-editor\n\n${license}`
);

// Record the bundled version so the C# side can report it and the build log is self-describing.
await writeFile(join(outDir, 'version.txt'), version + '\n');

const files = await readdir(outDir);
let total = 0;

for (const f of files) {
  total += (await stat(join(outDir, f))).size;
}

console.log(
  `monaco-editor ${version} bundled (ESM -> IIFE): ${files.length} files, ` +
  `${(total / 1024 / 1024).toFixed(1)} MB -> assets/js/monaco/`
);
