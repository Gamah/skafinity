// Does the SINGLE-FILE artifact actually boot?
//
// smoke.mjs/page.mjs check the served bundle. This checks `dist/skafinity.html` — that the
// inlined runtime boots off `loadBootResource` with no fetches, on the same code path a
// browser takes, and renders a song.
//
// It exercises the file's WORKER bundle, which is where the real risk is: it is the copy that
// gets no DOM, receives its assets by postMessage, and does the `import()` of the two
// `data:text/javascript` runtime modules. Node can import data: URLs, so that path is genuinely
// executed here rather than approximated. What this CANNOT prove is the browser half — a real
// `new Worker(blobUrl, {type:'module'})`, AudioContext playback, and the DOM wiring in app.js.
//
//   node test/dist-single.mjs [path/to/skafinity.html]
import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

let failures = 0;
function check(name, cond, detail = '') {
  console.log(`${cond ? 'ok  ' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  if (!cond) failures++;
}

const file = process.argv[2] || new URL('../dist/skafinity.html', import.meta.url);
let html;
try { html = readFileSync(file, 'utf8'); }
catch { console.error(`dist-single: ${file} is missing — build it with 'make dist'.`); process.exit(1); }

check('the page is one file with no external references', !/(src|href)="(?!https?:)/.test(html),
  (html.match(/(src|href)="(?!https?:)[^"]*"/g) || []).join(', ') || 'none');
// A REFERENCE — a path in a string or an import — is the failure; a comment that cites where a
// [SOURCE] fact was read from is not, and engine.js carries one of those into the bundle.
check('the runtime bundle directory is not referenced', !/["'`(]\s*\.?\/?_framework\//.test(html),
  (html.match(/["'`(]\s*\.?\/?_framework\/[^"'`\s)]*/g) || []).join(', ') || 'none');

// The three payload constants are each one line (JSON.stringify emits no newlines), so pulling
// them out is a line match rather than a parse.
const line = (name) => {
  const m = html.split('\n').find((l) => l.startsWith(`const ${name} = `));
  if (!m) { check(`${name} is present in the page`, false); process.exit(1); }
  return m;
};
// The main-thread bundle needs a DOM and an AudioContext, so node can't RUN it — but it can
// parse it, which is what catches the real hazard in concatenating four sources into one module
// scope: a redeclaration (the minified loader's top-level names against app.js's own).
const tmp0 = mkdtempSync(join(tmpdir(), 'skafinity-dist-'));
const inline = html.match(/<script type="module">\n([\s\S]*?)\n<\/script>/);
check('the page carries one inline module script', !!inline);
if (inline) {
  const p = join(tmp0, 'main.mjs');
  writeFileSync(p, inline[1]);
  let err = '';
  try { execFileSync(process.execPath, ['--check', p], { encoding: 'utf8', stdio: 'pipe' }); }
  catch (e) { err = String(e.stderr || e).split('\n').slice(0, 6).join(' '); }
  check('the main-thread bundle parses as a module (no redeclarations)', !err, err);
}

const payload = ['__SKAF_B64', '__SKAF_JS', '__SKAF_WORKER_SRC', '__SKAF_CONFIG'].map(line).join('\n');
const { __SKAF_B64, __SKAF_JS, __SKAF_WORKER_SRC, __SKAF_CONFIG } =
  new Function(payload + '\nreturn { __SKAF_B64, __SKAF_JS, __SKAF_WORKER_SRC, __SKAF_CONFIG };')();

check('every wasm asset is inlined', Object.keys(__SKAF_B64).length >= 5, Object.keys(__SKAF_B64).join(', '));
check('both runtime js modules are inlined', Object.keys(__SKAF_JS).length === 2, Object.keys(__SKAF_JS).join(', '));
check('the house-mix config is inlined', !!(__SKAF_CONFIG && __SKAF_CONFIG.advanced),
  Object.keys(__SKAF_CONFIG.advanced || {}).length + ' keys');

const bin = {};
for (const k of Object.keys(__SKAF_B64)) {
  const s = atob(__SKAF_B64[k]), u = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) u[i] = s.charCodeAt(i);
  bin[k] = u;
}

// ── Stand in for a Worker global scope and run the page's own worker bundle ──
const listeners = [];
const outbox = [];
globalThis.self = globalThis;
globalThis.addEventListener = (t, f) => { if (t === 'message') listeners.push(f); };
globalThis.postMessage = (msg) => outbox.push(msg);
const deliver = (data) => {
  for (const f of listeners) f({ data });
  if (typeof globalThis.onmessage === 'function') globalThis.onmessage({ data });
};

// ONE node-only substitution, and it is worth stating exactly: the page hands the runtime its
// two js modules as `data:text/javascript` URLs, but emscripten's `ENVIRONMENT_IS_NODE` branch
// does `createRequire(import.meta.url)`, which node rejects for a data: URL. That branch does
// not exist in a browser (`ENVIRONMENT_IS_WEB`), so under node the same sources are handed over
// as `file:` URLs instead. Everything else — the concatenated bundle, the resource-loader
// wiring, the synthesized Responses for all five wasm assets, the postMessage handoff — is the
// shipped code. What stays unproven here is browser-only by construction.
// [SOURCE] web/_framework/dotnet.native.*.js, 2026-08-01.
const tmp = mkdtempSync(join(tmpdir(), 'skafinity-dist-'));
let seq = 0;
globalThis.__skafNodeUrl = (src) => {
  const p = join(tmp, `mod${seq++}.mjs`);
  writeFileSync(p, src);
  return pathToFileURL(p).href;
};
const DATA_URL_LINE = "const __skafDataUrl = (src) => 'data:text/javascript;charset=utf-8,' + encodeURIComponent(src);";
check('the page hands the runtime its js modules as data: URLs', __SKAF_WORKER_SRC.includes(DATA_URL_LINE));
const nodeWorkerSrc = __SKAF_WORKER_SRC.replace(DATA_URL_LINE,
  'const __skafDataUrl = (src) => globalThis.__skafNodeUrl(src);');

// Same reason the bundle itself is imported from a file: URL rather than a data: one — the
// loader's own node branch (`await import("module").then(m => m.createRequire(import.meta.url))`)
// is evaluated eagerly under node and rejects a non-file URL. The browser takes the blob-URL
// Worker path and never evaluates it.
await import(globalThis.__skafNodeUrl(nodeWorkerSrc));
check('the worker bundle parses and evaluates as a module', listeners.length > 0);

deliver({ type: '__skafinit', js: __SKAF_JS, bin });

// The default Config comes from a CHILD process: booting web/engine.js here to ask for it
// would mean two .NET runtimes in one realm, and the worker bundle deliberately exposes
// nothing (its `Skafinity` is sealed in a block, exactly as it is in the shipped file).
const cfg = JSON.parse(execFileSync(process.execPath, ['-e',
  "import('./web/engine.js').then(async (m) => console.log(JSON.stringify(Array.from((await m.default()).defaultConfig()))))"],
  { cwd: new URL('..', import.meta.url), encoding: 'utf8' }).trim());
check('a default config was obtained to render with', cfg.length > 0, `${cfg.length} values`);

// Boot + render. A cold .NET-wasm boot plus a ~75 s song takes a few seconds under node.
const t0 = Date.now();
deliver({ type: 'gen', id: 1, n: 0, mySeq: 0, seed: 'rotaliate:0', cfg });
const song = await new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error('timed out waiting for the worker')), 180000);
  const poll = setInterval(() => {
    if (!outbox.length) return;
    clearInterval(poll); clearTimeout(timer); resolve(outbox.shift());
  }, 50);
});

check('the inlined runtime booted and the worker answered', song.type === 'song',
  song.type === 'song' ? `${((Date.now() - t0) / 1000).toFixed(1)} s` : String(song.error));
if (song.type === 'song') {
  check('the song is stereo PCM at the engine sample rate',
    song.sampleRate > 0 && song.left.length === song.right.length && song.left.length > song.sampleRate * 10,
    `${song.sampleRate} Hz, ${(song.left.length / song.sampleRate).toFixed(1)} s`);
  let peak = 0;
  for (let i = 0; i < song.left.length; i += 97) peak = Math.max(peak, Math.abs(song.left[i]));
  check('it is audible (non-silent) and not clipped to nonsense', peak > 0.05 && peak <= 1.001, `peak ${peak.toFixed(3)}`);
}

console.log(failures ? `\n${failures} check(s) failed` : '\nall dist-single checks passed');
process.exit(failures ? 1 : 0);
