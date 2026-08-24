/**
 * Bundles the **ESM** build of monaco-editor into the scripts Tesserae.Monaco ships: one module
 * entry, the chunks it pulls in on demand, and the language-service web workers.
 *
 * Why a bundle step exists at all: Monaco's ESM tree cannot be loaded by a browser directly. It
 * contains ~133 bare `import './x.css'` statements, which browsers reject outright ("Expected a
 * JavaScript-or-Wasm module script but the server responded with a MIME type of text/css"), and it
 * is 1331 modules deep - a request per module even if the CSS problem did not exist. Monaco's own
 * docs describe the ESM build as "compatible with e.g. webpack" for exactly this reason. The
 * alternative, Monaco's prebuilt AMD dist, is deprecated upstream and slated for removal.
 *
 * Why the output is a **module** rather than one IIFE: Monaco registers every one of its ~90
 * grammars and its four language-service modes behind a dynamic `import()` - see
 * `esm/vs/languages/definitions/csharp/register.js`, whose `loader` is `() => import('./csharp.js')`.
 * Bundled to an IIFE those dynamic imports are resolved at build time and inlined, so a page that
 * only ever shows C# still downloads Perl, Pascal and PowerQuery. Bundled to ESM with esbuild's
 * `splitting`, they stay real dynamic imports: the entry carries the editor, and each grammar and
 * mode becomes a chunk fetched the first time a document actually uses that language.
 *
 * So esbuild resolves the ESM graph, inlines the CSS (see the plugin below), and emits:
 *
 *   monaco.js          the module entry: installs MonacoEnvironment, evaluates the editor,
 *                      publishes `window.monaco`
 *   chunks/*.js        the editor's shared code plus one chunk per lazily-loaded grammar/mode
 *   editor.worker.js   the core web worker
 *   {ts,json,css,html}.worker.js   the language-service workers
 *
 * Nothing here is committed: the output is generated from the pinned npm package on every build
 * (see the BundleMonaco target in Tesserae.Monaco.csproj) and is gitignored.
 *
 * Run with:  npm run bundle   (from the Tesserae.Monaco/ folder)
 */
import { build, transform } from 'esbuild';
import { mkdir, rm, readFile, writeFile, readdir, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join, relative } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const pkgRoot = resolve(here, '..');
const monacoRoot = resolve(pkgRoot, 'node_modules/monaco-editor');
const esm = join(monacoRoot, 'esm/vs');
const outDir = resolve(pkgRoot, 'assets/js/monaco');

await rm(outDir, { recursive: true, force: true });
await mkdir(outDir, { recursive: true });

const common = {
  bundle: true,
  target: 'es2020',
  minify: true,
  legalComments: 'none',
  logLevel: 'warning',
};

/**
 * Turns every `import './x.css'` into a module that injects the stylesheet itself.
 *
 * esbuild's own CSS handling emits a `.css` file per entry point, which does not survive code
 * splitting - a chunk's styles would have nowhere to go, and a lazily-loaded grammar's styles would
 * never be requested at all. Injecting from JS keeps each chunk self-contained: the styles arrive
 * exactly when the code that needs them does, and there is no second file for a host to remember.
 *
 * `url(...)` references are inlined as data URIs on the way through - in practice that is Monaco's
 * `codicon.ttf`, which would otherwise have to be served from a path matching the CSS's own.
 */
const MIME_TYPES = {
  '.ttf':   'font/ttf',
  '.woff':  'font/woff',
  '.woff2': 'font/woff2',
  '.svg':   'image/svg+xml',
  '.png':   'image/png',
  '.gif':   'image/gif',
};

const cssInline = {
  name: 'css-inline',
  setup(builder) {
    builder.onLoad({ filter: /\.css$/ }, async (args) => {
      const minified = await transform(await readFile(args.path, 'utf8'), { loader: 'css', minify: true });

      let css = minified.code;

      for (const match of [...css.matchAll(/url\(\s*(['"]?)([^'")]+)\1\s*\)/g)]) {
        const reference = match[2];

        if (/^(data:|https?:|\/\/|#)/.test(reference)) continue;

        const file = resolve(dirname(args.path), reference.split(/[?#]/)[0]);
        const extension = file.slice(file.lastIndexOf('.'));

        try {
          const asset = await readFile(file);
          const mime = MIME_TYPES[extension] || 'application/octet-stream';

          css = css.split(match[0]).join(`url("data:${mime};base64,${asset.toString('base64')}")`);
        } catch {
          // A url() that does not resolve to a file on disk is left as it was written.
        }
      }

      return {
        contents: `(function(){var s=document.createElement("style");s.setAttribute("data-tssm-monaco","");s.textContent=${JSON.stringify(css)};document.head.appendChild(s)})();`,
        loader: 'js',
      };
    });
  },
};

// Worker file per Monaco language-service label. Baked into the entry below rather than assembled
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

/**
 * MonacoEnvironment has to exist before the editor evaluates - Monaco reads it the first time it
 * needs a worker and there is no way to supply it afterwards - so it lives in its own module that
 * the entry imports first. ES module imports evaluate in source order, which is what makes "first"
 * mean anything here.
 *
 * `import.meta.url` is the entry chunk's own URL, so the workers are found relative to wherever the
 * bundle is served from and there is no second setting to keep in sync. (`document.currentScript`,
 * which the previous IIFE used for this, is null inside a module.) Guarded by `||`, so a host that
 * wants its own worker strategy can still set MonacoEnvironment before loading this file.
 */
const environmentModule = `
var base = new URL('.', import.meta.url).href.replace(/\\/$/, '');
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
`;

/**
 * The entry. Two things happen here beyond evaluating the editor.
 *
 * `window.monaco` is what every consumer talks to - the same global the AMD build published, and
 * what the C# side's `[External]` declarations name.
 *
 * The alias loop puts the bundled language services where Monaco's own documentation says they are.
 * `editor.main.js` exports them as **top-level** names - `json`, `typescript`, `css`, `html` - so
 * they land on `monaco.json`, not `monaco.languages.json`. Every Monaco doc and sample still says
 * `monaco.languages.json.jsonDefaults`, and a host reaching for the documented path would get
 * "Cannot read properties of undefined". Aliasing costs nothing - `monaco.languages` is a plain
 * object here, not a frozen module namespace - and keeps the C# side written against the documented
 * shape rather than against this bundle's accident.
 *
 * Without it the workers still load and still validate; only the configuration API is unreachable,
 * which is a quiet failure worth not shipping.
 */
const entryModule = `
import './monaco-environment.js';
import * as monaco from ${JSON.stringify(join(esm, 'editor/editor.main.js'))};

['json', 'typescript', 'css', 'html'].forEach(function (name) {
  if (monaco[name] && !monaco.languages[name]) {
    try { monaco.languages[name] = monaco[name]; } catch (e) { /* frozen namespace: leave it */ }
  }
});

window.monaco = monaco;
`;

// Written next to the generated bundle rather than into the repo: they are build inputs with no
// life outside this script, and the whole folder is wiped and regenerated above.
const entryPath = join(outDir, 'monaco-entry.js');

await writeFile(join(outDir, 'monaco-environment.js'), environmentModule);
await writeFile(entryPath, entryModule);

await build({
  ...common,
  entryPoints: [entryPath],
  outdir: outDir,
  entryNames: 'monaco',
  chunkNames: 'chunks/[name]-[hash]',
  format: 'esm',
  splitting: true,
  plugins: [cssInline],
});

await rm(entryPath);
await rm(join(outDir, 'monaco-environment.js'));

// Workers are classic (non-module) scripts: MonacoEnvironment.getWorker hands back a URL that
// Monaco loads as a plain Worker, so each one has to be a standalone IIFE with no imports left.
const workerEntries = {
  'editor.worker.js': 'editor/editor.worker.js',
  'ts.worker.js':     'language/typescript/ts.worker.js',
  'json.worker.js':   'language/json/json.worker.js',
  'css.worker.js':    'language/css/css.worker.js',
  'html.worker.js':   'language/html/html.worker.js',
};

for (const [outName, entry] of Object.entries(workerEntries)) {
  await build({
    ...common,
    format: 'iife',
    entryPoints: [join(esm, entry)],
    outfile: join(outDir, outName),
    loader: { '.ttf': 'dataurl' },
  });
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

// The eager half of the report is what the entry pulls in with a static import; everything else is
// a chunk nothing fetches until a document uses that language. Reporting both is what makes a
// regression here - a stray static import dragging the grammars back in - visible in the build log.
const eager = new Set(['monaco.js']);
const pending = ['monaco.js'];

while (pending.length > 0) {
  const file = pending.pop();
  const source = await readFile(join(outDir, file), 'utf8');

  for (const match of source.matchAll(/from\s*"(\.[^"]*\.js)"/g)) {
    const target = relative(outDir, resolve(dirname(join(outDir, file)), match[1]));

    if (!eager.has(target)) {
      eager.add(target);
      pending.push(target);
    }
  }
}

const walk = async (directory, prefix = '') => {
  let found = [];

  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.isDirectory()) found = found.concat(await walk(join(directory, entry.name), prefix + entry.name + '/'));
    else found.push(prefix + entry.name);
  }

  return found;
};

const files = await walk(outDir);
let total = 0;
let eagerBytes = 0;

for (const file of files) {
  const size = (await stat(join(outDir, file))).size;

  total += size;

  if (eager.has(file)) eagerBytes += size;
}

const mb = (bytes) => (bytes / 1024 / 1024).toFixed(1) + ' MB';

console.log(
  `monaco-editor ${version} bundled (ESM, code-split): ${files.length} files, ${mb(total)} -> assets/js/monaco/\n` +
  `  ${eager.size} loaded up front (${mb(eagerBytes)}); the rest is fetched per language on demand.`
);
