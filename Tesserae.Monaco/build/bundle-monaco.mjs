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
 * The entry. Three things happen here beyond evaluating the editor.
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
 *
 * The third is `monaco.tesserae`: what this package adds to Monaco, published as one object beside
 * the API so the C# side can declare it. Today that is the markdown renderer service that makes the
 * type names inside a documentation code block clickable - see LINKED_CODE_BLOCKS below.
 */
const entryModule = `
import './monaco-environment.js';
import * as monaco from ${JSON.stringify(join(esm, 'editor/editor.main.js'))};
import { tesserae } from './monaco-tesserae.js';

['json', 'typescript', 'css', 'html'].forEach(function (name) {
  if (monaco[name] && !monaco.languages[name]) {
    try { monaco.languages[name] = monaco[name]; } catch (e) { /* frozen namespace: leave it */ }
  }
});

// esbuild's namespace object is a plain object and takes the property; a genuinely frozen module
// namespace would not, in which case a shallow copy carrying the extra member stands in for it.
var api = monaco;

try { api.tesserae = tesserae; } catch (e) { api = Object.assign({}, monaco, { tesserae: tesserae }); }

window.monaco = api;
`;

/**
 * Clickable type names inside a documentation code block.
 *
 * A hover, a completion's details pane and a parameter hint all show a symbol's signature as a fenced
 * ```csharp block, because that is the only way to get the editor's own grammar colours into a popup -
 * and the types in that signature are what a reader wants to click. Plain markdown cannot do both:
 * marked treats a fenced block as opaque text, so a link written inside it is printed literally, and
 * the colouring is a second pass (`codeBlockRenderer`) that knows tokens, not symbols. So producers
 * put the links in a row underneath (`[ReadOnlyNode](command:…) · [string](command:…)`).
 *
 * The seam is Monaco's `IMarkdownRendererService`: in 0.56 every popup renders its markdown through
 * its one `render(markdown, options, outElement)`, which sees the whole markdown string and hands the
 * code blocks to a pluggable renderer. It is a standalone service, so it can be replaced through the
 * same map `monaco.editor.create(element, options, override)` takes - honoured by
 * `StandaloneServices.initialize` on the **first** initialisation only, while the registered entry is
 * still a `SyncDescriptor`. That first initialisation is not the first editor, though: *every*
 * `monaco.*` call reaches the services (`defineTheme`, `createModel`, `setTheme`, a language
 * registration - each goes through `StandaloneServices.get`, which initialises with no overrides), and
 * the C# loader defines the themes the moment the bundle has loaded. Measured: passing the override
 * on `create` left `markdownRendererInstalled` false and the popups unchanged. So the entry calls
 * `StandaloneServices.initialize` itself, here, before anything else can - the same call `create`
 * makes with the same argument, only earlier. Its other effect, instantiating the registered editor
 * features, is what the first `monaco.*` call would have done a few milliseconds later anyway.
 *
 * The subclass does three things around the original `render`:
 *
 *   1. reads the `command:` links Monaco itself rendered - an anchor keeps its `data-href` only on a
 *      trusted string, and its text is the link text with markdown escapes already removed, so nothing
 *      here parses markdown or decides what is trusted;
 *   2. wraps the default code block renderer so that, once a block is tokenized, every whole identifier
 *      in it that equals a link's text becomes an anchor with the same `data-href`. Clicks then go
 *      through the same `actionHandler` the row's links use, so a registered command fires unchanged.
 *      The anchor sits *inside* the token span and inherits its colour: Monaco's encoded tokenizer
 *      merges adjacent tokens that share a colour (`MonarchModernTokensCollector.emit` compares
 *      metadata, not token type), so a type name painted in the default foreground shares its span with
 *      the punctuation around it, and matching whole spans would miss exactly the names this is for;
 *   3. once the blocks have landed - Monaco fires `asyncRenderCallback` after swapping them in - drops
 *      the row that held only links which were all placed in a block, `<hr>` above it included. A link
 *      whose text appears in no block stays, so nothing a producer wrote becomes unreachable.
 *
 * Everything else about the popup - measurement, scrolling, focus, disposal - stays Monaco's; the
 * service returns the same `{ element, dispose }` the original does. The two internals leaned on,
 * `_defaultCodeBlockRenderer` and its `renderCodeBlock`, are guarded, so a rename upstream degrades
 * to the original behaviour rather than breaking. `monaco.tesserae.linkTypesInCodeBlocks` is the
 * switch, read on every render so the C# side (`MonacoEditor.LinkTypesInCodeBlocks`) can flip it at
 * any time; off, the service is the original one. `markdownRendererInstalled` turns true when the
 * subclass is constructed, which Monaco does as the first editor is created - a diagnostic, not a
 * setting.
 */
const tesseraeModule = `
import { MarkdownRendererService } from ${JSON.stringify(join(esm, 'platform/markdown/browser/markdownRenderer.js'))};
import { SyncDescriptor } from ${JSON.stringify(join(esm, 'platform/instantiation/common/descriptors.js'))};
import { StandaloneServices } from ${JSON.stringify(join(esm, 'editor/standalone/browser/standaloneServices.js'))};

var LINK_CLASS = 'tssm-code-link';
var IDENTIFIER = /[\\p{L}_@][\\p{L}\\p{N}_]*/gu;
var WHOLE_IDENTIFIER = /^[\\p{L}_@][\\p{L}\\p{N}_]*$/u;
var SEPARATORS = /^[\\s\\u00a0\\u00b7\\u2022|,;\\/-]*$/;

function isIdentifier(text) {
  return WHOLE_IDENTIFIER.test(text);
}

// The command links as Monaco rendered them, keyed by their text. Only an identifier-shaped text can
// ever equal a token, and a text that appears twice is left alone rather than guessed at.
function collectCommandLinks(root) {
  var links = new Map();
  var anchors = root.querySelectorAll('a[data-href]');

  for (var i = 0; i < anchors.length; i++) {
    var anchor = anchors[i];
    var href = anchor.dataset.href;
    var text = anchor.textContent;

    if (!href || !/^command:/i.test(href) || !isIdentifier(text)) continue;

    if (links.has(text)) links.get(text).ambiguous = true;
    else links.set(text, { href: href, anchor: anchor, consumed: false, ambiguous: false });
  }

  return links;
}

function createLink(text, link) {
  var anchor = document.createElement('a');

  anchor.className = LINK_CLASS;
  anchor.setAttribute('href', '');
  anchor.dataset.href = link.href;
  anchor.style.color = 'inherit';
  anchor.textContent = text;

  return anchor;
}

// Splits one token span's text at its identifiers, wrapping those that name a link. Returns null when
// nothing in it does, so an untouched span keeps its single text node.
function splitIdentifiers(text, links) {
  var parts = null;
  var last = 0;
  var match;

  IDENTIFIER.lastIndex = 0;

  while ((match = IDENTIFIER.exec(text)) !== null) {
    var link = links.get(match[0]);

    if (!link || link.ambiguous) continue;

    parts = parts || [];

    if (match.index > last) parts.push(document.createTextNode(text.slice(last, match.index)));

    parts.push(createLink(match[0], link));
    link.consumed = true;
    last = match.index + match[0].length;
  }

  if (parts && last < text.length) parts.push(document.createTextNode(text.slice(last)));

  return parts;
}

function linkTokens(element, links) {
  if (!element || !element.querySelectorAll || links.size === 0) return;

  var spans = element.querySelectorAll('span');

  for (var i = 0; i < spans.length; i++) {
    var span = spans[i];

    // A token span holds one text node; the renderer's root span and the line container do not.
    if (span.childNodes.length !== 1 || span.firstChild.nodeType !== Node.TEXT_NODE) continue;

    var parts = splitIdentifiers(span.firstChild.nodeValue, links);

    if (parts) span.replaceChildren.apply(span, parts);
  }
}

function dropConsumedLinkRow(links) {
  var rows = new Set();

  links.forEach(function (link) {
    if (link.consumed && link.anchor.parentElement) rows.add(link.anchor.parentElement);
  });

  rows.forEach(function (row) {
    if (row.tagName !== 'P') return;

    for (var node = row.firstChild; node; node = node.nextSibling) {
      if (node.nodeType === Node.TEXT_NODE) {
        if (!SEPARATORS.test(node.nodeValue)) return;
      } else if (node.nodeType === Node.ELEMENT_NODE && node.tagName === 'A') {
        var link = links.get(node.textContent);

        if (!link || link.anchor !== node || !link.consumed) return;
      } else {
        return;
      }
    }

    var before = row.previousElementSibling;

    row.remove();

    if (before && before.tagName === 'HR') before.remove();
  });
}

export var tesserae = {
  linkTypesInCodeBlocks: true,
  markdownRendererInstalled: false,
  linkClass: LINK_CLASS
};

class LinkedCodeBlockMarkdownRendererService extends MarkdownRendererService {
  constructor(openerService) {
    super(openerService);
    tesserae.markdownRendererInstalled = true;
  }

  render(markdown, options, outElement) {
    // An untrusted string has its command links stripped to text, so there is nothing to place.
    if (!tesserae.linkTypesInCodeBlocks || !markdown || !markdown.isTrusted) return super.render(markdown, options, outElement);

    var self = this;
    var rendered = null;
    var links = null;
    var linksOf = function () { return links || (links = rendered ? collectCommandLinks(rendered.element) : new Map()); };
    var inner = options && options.codeBlockRenderer;
    var afterRender = options && options.asyncRenderCallback;
    var resolved = Object.assign({}, options);

    resolved.codeBlockRenderer = async function (alias, value) {
      var element = inner ? await inner(alias, value) : await self._renderDefaultCodeBlock(alias, value, resolved);

      // The await above is what guarantees super.render has returned and the row's anchors exist.
      linkTokens(element, linksOf());

      return element;
    };

    resolved.asyncRenderCallback = function () {
      if (links) dropConsumedLinkRow(links);
      if (afterRender) afterRender();
    };

    rendered = super.render(markdown, resolved, outElement);

    return rendered;
  }

  _renderDefaultCodeBlock(alias, value, options) {
    var renderer = this._defaultCodeBlockRenderer;

    if (renderer && typeof renderer.renderCodeBlock === 'function') return renderer.renderCodeBlock(alias, value, options);

    return Promise.resolve(document.createElement('span'));
  }
}

// The descriptor mirrors the original registration (a delayed singleton), so the instantiation service
// injects IOpenerService for the subclass exactly as it does for the base class. Installed now, before
// any monaco.* call can initialise the services without it - see the note above this module.
StandaloneServices.initialize({ markdownRendererService: new SyncDescriptor(LinkedCodeBlockMarkdownRendererService, [], true) });

// The linked token keeps its token colour (inline, so no hover rule of Monaco's outbids it) and says it
// is a link the way the editor's own ctrl-hover links do: a dotted underline that turns solid under the
// pointer. Injected like every other stylesheet in this bundle.
(function () {
  var style = document.createElement('style');

  style.setAttribute('data-tssm-monaco', '');
  style.textContent =
    '.rendered-markdown a.' + LINK_CLASS + '{cursor:pointer;text-decoration:underline dotted;text-decoration-thickness:1px;text-underline-offset:2px}' +
    '.rendered-markdown a.' + LINK_CLASS + ':hover{text-decoration-style:solid}';
  document.head.appendChild(style);
})();
`;

// Written next to the generated bundle rather than into the repo: they are build inputs with no
// life outside this script, and the whole folder is wiped and regenerated above.
const entryPath = join(outDir, 'monaco-entry.js');

await writeFile(join(outDir, 'monaco-environment.js'), environmentModule);
await writeFile(join(outDir, 'monaco-tesserae.js'), tesseraeModule);
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
await rm(join(outDir, 'monaco-tesserae.js'));

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
