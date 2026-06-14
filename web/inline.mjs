// Build a single self-contained skafinity.html from the dev assets + the SINGLE_FILE
// emscripten module. The dev files (web/app.js, web/worker.js) are left untouched: the
// only two lines that reference sibling files are rewritten here for the inlined build,
// and everything is base64-embedded so no escaping/`</script>` hazards.
//
//   node web/inline.mjs <emscripten-single-file.js> <out.html>
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const [emPath, outPath] = process.argv.slice(2);
if (!emPath || !outPath) { console.error('usage: inline.mjs <em.js> <out.html>'); process.exit(1); }

const read = (p) => readFileSync(p, 'utf8');
const b64 = (s) => Buffer.from(s, 'utf8').toString('base64');

const emjs = read(emPath);
const css = read(join(here, 'style.css'));
let html = read(join(here, 'index.html'));

// Rewrite the two sibling-file references in app.js / worker.js for the inlined build.
const WK_EM_PLACEHOLDER = '__SKA_EM_URL__';
let appjs = read(join(here, 'app.js'))
  .replace("import Skafinity from '../build/skafinity.js';",
           'const Skafinity = (await import(window.__SKA.emUrl)).default;')
  .replace("new Worker(new URL('./worker.js', import.meta.url), { type: 'module' })",
           'new Worker(window.__SKA.wkUrl, { type: \'module\' })');
let workerjs = read(join(here, 'worker.js'))
  .replace("import Skafinity from '../build/skafinity.js';",
           `const Skafinity = (await import('${WK_EM_PLACEHOLDER}')).default;`);

const bootstrap = `<script type="module">
  const dec = (s) => new TextDecoder().decode(Uint8Array.from(atob(s), c => c.charCodeAt(0)));
  const blobUrl = (src) => URL.createObjectURL(new Blob([src], { type: 'text/javascript' }));
  const EM = "${b64(emjs)}";
  const WK = "${b64(workerjs)}";
  const APP = "${b64(appjs)}";
  const emUrl = blobUrl(dec(EM));
  const wkUrl = blobUrl(dec(WK).replace('${WK_EM_PLACEHOLDER}', emUrl));
  window.__SKA = { emUrl, wkUrl };
  await import(blobUrl(dec(APP)));
</script>`;

html = html
  .replace('<link rel="stylesheet" href="style.css" />', `<style>\n${css}\n</style>`)
  .replace('<script type="module" src="app.js"></script>', bootstrap);

writeFileSync(outPath, html);
const kb = (Buffer.byteLength(html) / 1024).toFixed(0);
console.log(`wrote ${outPath} (${kb} KB, self-contained)`);
