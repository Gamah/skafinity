// Builds the single self-contained .html: the page, its glue, and the WHOLE .NET wasm
// runtime (loader, the two js modules, dotnet.native.wasm and the four assemblies) inlined
// into one file with no fetches of its own.
//
// How it works — the runtime is already boot-config-embedded (dotnet.js ends in
// `dotnet.withConfig({...resources...})`), so there is no boot manifest to shim; the only
// thing left is where the bytes come from. `dotnet.withResourceLoader(fn)` answers that:
//   - for the `dotnetjs` type the loader MUST return a URL STRING (the runtime asserts on it
//     and then `import()`s it), so the two js modules become `data:text/javascript` URLs;
//   - for everything else it may return a `Promise<Response>`, which is returned as-is —
//     so the wasm comes from a synthesized Response over the inlined bytes. That path also
//     skips `fetch`, and SRI is only ever applied to fetch options, so no hash check runs
//     and `disableIntegrityCheck` never has to be touched.
// [SOURCE] read from the published web/_framework/dotnet.js, 2026-08-01 — this is an
// implementation detail of the loader, not a documented contract, so re-read it if a runtime
// bump makes this file fail.
//
// The generation workers are the reason this is not just "base64 everything into app.js":
// each Worker boots its own runtime instance, and re-parsing ~10 MB of base64 per worker is
// not acceptable. Instead the worker is a blob-URL module (loader + engine + worker glue,
// no assets) and the main thread posts it the decoded bytes WITHOUT a transfer list — a
// structured-clone copy, so the main thread keeps its own runtime alive.
//
// Every rewrite below is anchored on an exact source pattern and HARD-FAILS if it stops
// matching. A silently mis-rewritten bundle is a file that dies at boot for whoever was
// handed it, which is far worse than a build error here.

import { readFileSync, writeFileSync, readdirSync, mkdirSync } from 'node:fs';
import { join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const web = join(root, 'web');
const fw = join(web, '_framework');
const out = process.argv[2] || join(root, 'dist', 'skafinity.html');

const read = (p) => readFileSync(p, 'utf8');

// Replace exactly one occurrence of `pat`, or die naming what stopped matching.
function sub(src, pat, repl, what) {
  const hits = src.match(pat instanceof RegExp ? new RegExp(pat.source, pat.flags.includes('g') ? pat.flags : pat.flags + 'g') : new RegExp(escapeRe(pat), 'g'));
  if (!hits || hits.length !== 1)
    fail(`expected exactly 1 match for ${what} (got ${hits ? hits.length : 0}). ` +
         `The source moved — fix tools/bundle-single.mjs rather than shipping a broken bundle.`);
  return src.replace(pat, repl);
}
const escapeRe = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
function fail(msg) { console.error('bundle-single: ' + msg); process.exit(1); }

// Depth-0 `var` scan (quotes/brackets tracked; good enough for machine-minified output).
function hasTopLevelVar(src) {
  let depth = 0, quote = null;
  for (let i = 0; i < src.length; i++) {
    const c = src[i];
    if (quote) { if (c === '\\') i++; else if (c === quote) quote = null; continue; }
    if (c === '"' || c === "'" || c === '`') { quote = c; continue; }
    if (c === '/' && src[i + 1] === '/') { i = src.indexOf('\n', i); if (i < 0) break; continue; }
    if (c === '/' && src[i + 1] === '*') { i = src.indexOf('*/', i) + 1; continue; }
    if (c === '{' || c === '(' || c === '[') depth++;
    else if (c === '}' || c === ')' || c === ']') depth--;
    else if (depth === 0 && c === 'v' && /^var[\s(]/.test(src.slice(i, i + 4)) && !/[\w$.]/.test(src[i - 1] || ' '))
      return true;
  }
  return false;
}

// A `</script` anywhere in an inlined source would close the host <script> tag early.
function assertScriptSafe(name, text) {
  if (/<\/script/i.test(text)) fail(`${name} contains "</script" — it cannot be inlined verbatim.`);
}

// ── Inputs ──
for (const f of ['index.html', 'app.js', 'engine.js', 'worker.js', 'queue.js', 'palette.js',
                 'player.js', 'skafinity-element.js', 'style.css', 'config.json'])
  try { readFileSync(join(web, f)); } catch { fail(`web/${f} is missing.`); }
try { readFileSync(join(fw, 'dotnet.js')); } catch {
  fail(`web/_framework/dotnet.js is missing — build the bundle first ('make' with the .NET SDK, or 'make up').`);
}

const fwFiles = readdirSync(fw).filter((f) => !f.endsWith('.br') && !f.endsWith('.gz'));
const jsModules = fwFiles.filter((f) => f.endsWith('.js') && f !== 'dotnet.js');
const binaries = fwFiles.filter((f) => !f.endsWith('.js'));
if (jsModules.length === 0) fail('no dotnet.*.js runtime modules found in web/_framework.');
if (binaries.length === 0) fail('no wasm assets found in web/_framework.');
for (const f of fwFiles) if (!/\.(js|wasm|dat)$/.test(f))
  fail(`web/_framework/${f} is neither a js module nor a wasm/dat asset — the inliner does not know how to carry it.`);

// ── The runtime loader, de-exported so it can be concatenated into one module ──
// dotnet.js's single export line is the only site; `ft` is the `dotnet` builder object.
let loaderSrc = read(join(fw, 'dotnet.js'));
assertScriptSafe('_framework/dotnet.js', loaderSrc);
// The loader is minified down to one- and two-letter top-level names (`n`, `I`, `W`, …) and it
// declares a top-level `var`; app.js has top-level names of its own (`n` among them). Merging
// the two into one module scope is a redeclaration SyntaxError waiting to happen, so the loader
// goes inside an IIFE — which isolates `var` too, where a bare block would not — and returns
// the builder object instead of exporting it.
loaderSrc = sub(loaderSrc, /export\{(\w+) as default,(\w+) as dotnet,(\w+) as exit\};?\s*$/,
  'return $2;', 'dotnet.js export line');

// ── engine.js: drop its import of the loader, un-export its default ──
let engineSrc = read(join(web, 'engine.js'));
assertScriptSafe('engine.js', engineSrc);
engineSrc = sub(engineSrc, "import { dotnet } from './_framework/dotnet.js';", '', 'engine.js loader import');
engineSrc = sub(engineSrc, 'export default function Skafinity(opts)', 'Skafinity = function Skafinity(opts)',
  'engine.js default export');
// engine.js is block-scoped next to app.js; a top-level `var` there would hoist past the block.
if (hasTopLevelVar(engineSrc)) fail('engine.js declares a top-level `var` — use let/const so it stays inside its block.');

// ── worker.js: drop its import ──
let workerSrc = read(join(web, 'worker.js'));
assertScriptSafe('worker.js', workerSrc);
workerSrc = sub(workerSrc, "import Skafinity from './engine.js';", '', 'worker.js engine import');

// ── queue.js: un-export the class so it can sit in app.js's scope ──
// Main thread only (the worker renders, it does not schedule), so it goes in the main bundle.
let queueSrc = read(join(web, 'queue.js'));
assertScriptSafe('queue.js', queueSrc);
queueSrc = sub(queueSrc, 'export class GenQueue', 'class GenQueue', 'queue.js class export');
if (hasTopLevelVar(queueSrc)) fail('queue.js declares a top-level `var` — use let/const.');

// ── palette.js: un-export everything so it can sit in one scope with the rest ──
// A generic strip rather than a name list, because the palette is a bag of small pure functions and
// a list would be one more thing to forget. It still hard-fails if the shape changes: a file with
// no exports left is not a file this bundler understood.
let paletteSrc = read(join(web, 'palette.js'));
assertScriptSafe('palette.js', paletteSrc);
{
  const before = (paletteSrc.match(/^export /gm) || []).length;
  if (before < 8) fail(`palette.js has ${before} top-level exports — expected the pure-function bag. ` +
    `Fix tools/bundle-single.mjs rather than shipping a broken bundle.`);
  paletteSrc = paletteSrc.replace(/^export /gm, '');
  if (hasTopLevelVar(paletteSrc)) fail('palette.js declares a top-level `var` — use let/const.');
}

// ── player.js: drop its imports, inline the config fetch, build workers from the blob module ──
// The worker construction and the house-mix fetch moved here out of app.js when the transport was
// extracted; they are still the only two things in it that assume a server.
let playerSrc = read(join(web, 'player.js'));
assertScriptSafe('player.js', playerSrc);
playerSrc = sub(playerSrc, "import Skafinity from './engine.js';", '', 'player.js engine import');
playerSrc = sub(playerSrc, "import { GenQueue } from './queue.js';", '', 'player.js queue import');
playerSrc = sub(playerSrc, "const res = await fetch(url, { cache: 'no-store' });",
  'const res = await __skafConfigResponse();', 'player.js config.json fetch');
// Both branches of defaultCreateWorker: there is no worker.js to fetch here, same-origin or not.
playerSrc = sub(playerSrc, "return new Worker(url, { type: 'module' });",
  'return __skafMakeWorker();', 'player.js same-origin worker construction');
playerSrc = sub(playerSrc, "const w = new Worker(shim, { type: 'module' });",
  'const w = __skafMakeWorker();', 'player.js cross-origin worker construction');
playerSrc = playerSrc.replace(/^export /gm, '');
playerSrc = sub(playerSrc, 'default SkafinityPlayer;', '', 'player.js default export');
if (hasTopLevelVar(playerSrc)) fail('player.js declares a top-level `var` — use let/const.');

// ── skafinity-element.js: drop its imports, un-export ──
let elementSrc = read(join(web, 'skafinity-element.js'));
assertScriptSafe('skafinity-element.js', elementSrc);
elementSrc = sub(elementSrc, "import { SkafinityPlayer } from './player.js';", '', 'element player import');
elementSrc = sub(elementSrc,
  "import { derivePalette, chooseMode, pickAccent, parseColor, NEUTRAL_ACCENT } from './palette.js';",
  '', 'element palette import');
elementSrc = elementSrc.replace(/^export /gm, '');
elementSrc = sub(elementSrc, 'default SkafinityPlayerElement;', '', 'element default export');
if (hasTopLevelVar(elementSrc)) fail('skafinity-element.js declares a top-level `var` — use let/const.');

// ── app.js: the page host. Its one import is the element, which is already in scope here ──
let appSrc = read(join(web, 'app.js'));
assertScriptSafe('app.js', appSrc);
appSrc = sub(appSrc, "await import('./skafinity-element.js');", '', 'app.js element import');

// The house-mix config is a build input here, not a runtime fetch: the standalone file has
// no server to fetch it from, so it carries the canonical values it was built with.
// Re-serialized on one line: every `__SKAF_*` payload constant is a single line, which is what
// lets test/dist-single.mjs lift them back out of the page without parsing the whole bundle.
const configJson = JSON.stringify(JSON.parse(read(join(web, 'config.json'))));

// ── The shim both realms share ──
// `dotnet` here stands in for the real builder: it waits for the assets, installs the
// resource loader over them, and only then boots.
const shimSrc = `
const __skafDataUrl = (src) => 'data:text/javascript;charset=utf-8,' + encodeURIComponent(src);
function __skafDotnet(assetsPromise) {
  return { create: async () => {
    const A = await assetsPromise;
    const urls = {};
    for (const k of Object.keys(A.js)) urls[k] = __skafDataUrl(A.js[k]);
    __skafDotnetReal.withResourceLoader((type, name) => {
      if (type === 'dotnetjs') {
        if (!urls[name]) throw new Error('skafinity: no inlined js module ' + name);
        return urls[name];
      }
      const bytes = A.bin[name];
      if (!bytes) throw new Error('skafinity: no inlined asset ' + name);
      return Promise.resolve(new Response(bytes, { status: 200, headers: {
        'Content-Type': name.endsWith('.wasm') ? 'application/wasm' : 'application/octet-stream' } }));
    });
    return __skafDotnetReal.create();
  } };
}
`;

// The shim + the loader + engine.js, sealed in one block scope so only `Skafinity` reaches
// the surrounding module and the loader's minified one-letter names collide with nothing.
const runtimeBlock = (assetsExpr) => [
  'let Skafinity;',
  '{',
  'const __skafDotnetReal = (() => {',
  loaderSrc,
  '})();',
  shimSrc,
  `const dotnet = __skafDotnet(${assetsExpr});`,
  engineSrc,
  '}',
].join('\n');

// ── Worker module (blob) — no assets; they arrive on the first message ──
const workerBundle = [
  '// skafinity standalone — generation worker (assets arrive by postMessage)',
  'let __skafGiveAssets;',
  'const __skafAssets = new Promise((r) => { __skafGiveAssets = r; });',
  "self.addEventListener('message', (e) => { if (e.data && e.data.type === '__skafinit') __skafGiveAssets({ js: e.data.js, bin: e.data.bin }); });",
  runtimeBlock('__skafAssets'),
  workerSrc,
].join('\n');
assertScriptSafe('worker bundle', workerBundle);

// ── Assets, base64'd for the main thread ──
const b64 = {};
let rawBytes = 0;
for (const f of binaries) {
  const buf = readFileSync(join(fw, f));
  rawBytes += buf.length;
  b64[f] = buf.toString('base64');
}
const jsMod = {};
for (const f of jsModules) {
  const s = read(join(fw, f));
  assertScriptSafe(`_framework/${f}`, s);
  jsMod[f] = s;
}

const mainBundle = [
  `const __SKAF_B64 = ${JSON.stringify(b64)};`,
  `const __SKAF_JS = ${JSON.stringify(jsMod)};`,
  `const __SKAF_CONFIG = ${configJson};`,
  `const __SKAF_WORKER_SRC = ${JSON.stringify(workerBundle)};`,
  `function __skafConfigResponse() {
  return Promise.resolve(new Response(JSON.stringify(__SKAF_CONFIG), {
    status: 200, headers: { 'Content-Type': 'application/json' } }));
}
function __skafDecode(b64) {
  const s = atob(b64), u = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) u[i] = s.charCodeAt(i);
  return u;
}
let __skafBin = null;
function __skafBinAssets() {
  if (!__skafBin) { __skafBin = {}; for (const k of Object.keys(__SKAF_B64)) __skafBin[k] = __skafDecode(__SKAF_B64[k]); }
  return __skafBin;
}
const __skafAssets = Promise.resolve({ js: __SKAF_JS, get bin() { return __skafBinAssets(); } });
let __skafWorkerUrl = null;
function __skafMakeWorker() {
  if (!__skafWorkerUrl) __skafWorkerUrl = URL.createObjectURL(new Blob([__SKAF_WORKER_SRC], { type: 'text/javascript' }));
  const w = new Worker(__skafWorkerUrl, { type: 'module' });
  // No transfer list on purpose: the worker gets a COPY and this realm keeps its own runtime.
  w.postMessage({ type: '__skafinit', js: __SKAF_JS, bin: __skafBinAssets() });
  return w;
}`,
  runtimeBlock('__skafAssets'),
  queueSrc,
  paletteSrc,
  playerSrc,
  elementSrc,
  appSrc,
].join('\n');

// ── The page ──
let html = read(join(web, 'index.html'));
// The demo pages are siblings on the served site and do not exist next to a handed-over single
// file, so the two links become absolute. (test/dist-single.mjs asserts the artifact references
// nothing relative — a dead link is exactly what it is there to catch.)
const SITE = 'https://gamah.github.io/skafinity/';
for (const page of ['embed-light.html', 'embed-dark.html'])
  html = sub(html, `href="${page}"`, `href="${SITE}${page}"`, `index.html ${page} link`);
html = sub(html, /<link rel="stylesheet" href="style\.css" \/>/,
  () => '<style>\n' + read(join(web, 'style.css')) + '\n</style>', 'index.html stylesheet link');
html = sub(html, /<script type="module" src="app\.js"><\/script>/,
  () => '<script type="module">\n' + mainBundle + '\n</script>', 'index.html app.js script tag');

mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, html);
const size = Buffer.byteLength(html);
console.log(`${basename(out)}: ${(size / 1048576).toFixed(2)} MiB ` +
  `(${binaries.length} wasm assets, ${rawBytes} B raw, base64 +33%) — serve it over http.`);
