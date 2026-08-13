# Embedding skafinity — `<skafinity-player>`

The whole toy is a custom element. Drop it in a page, give it nothing, and it works out what it
should look like from the page around it.

```html
<script type="module" src="https://example.com/skafinity/skafinity-element.js"></script>

<skafinity-player></skafinity-player>
```

Nothing is downloaded until somebody presses play — see [Lazy boot](#lazy-boot). Iframe embeds and a
compact "badge" layout are deliberately not part of this; the element is the whole surface.

---

## The files

| File | What it is |
|---|---|
| `web/skafinity-element.js` | The element: shadow root, UI, theming. Import this — it pulls the rest. |
| `web/player.js` | The headless transport (scheduling, look-ahead, the timeline). Usable on its own. |
| `web/palette.js` | The colour derivation. DOM-free; ported from `SkafinityTheme.cs`. |
| `web/encode.js` | The export: the browser's `AudioEncoder` plus the FLAC/Ogg-Opus muxing. DOM-free. |
| `web/engine.js`, `web/worker.js`, `web/queue.js`, `web/_framework/` | The engine and its plumbing. |

All of them must be served from the same directory — `player.js` resolves the worker and `engine.js`
resolves the runtime relative to their own module URLs. Copy `web/` as a unit (`make dist` produces
exactly that tree) rather than picking files out of it.

---

## Attributes

| Attribute | Values | Default | What it does |
|---|---|---|---|
| `seed` | `tag:n[:genre][:vibe]` (see `docs/seed-format.md`) | a fresh random station | The song to start on. Set it any time; the element re-seeds. A seed that will not parse is refused with a message under the box and changes nothing. |
| `theme` | `auto` \| `light` \| `dark` | `auto` | `auto` measures the host page (below). The other two are an instruction. |
| `accent` | any CSS colour | sniffed | Overrides the accent the whole palette derives from. |
| `controls` | `all`, or a space/comma list of `transport` `seed` `vibe` `playlist` | `all` | Which sections show. An unrecognised name is ignored. |
| `storage-key` | any string | the element's `id`, else `default` | Namespace for this instance's remembered volume/mix/position. |
| `storage` | `none` | — | Persist nothing; keep it all in memory for the session. |
| `preload` | `none` \| `auto` | `none` | `auto` downloads the engine as soon as the element connects. |
| `autoplay` | present/absent | absent | Best-effort — browsers block audio without a gesture, and the widget then just sits there showing play. |
| `volume` | `0`–`1.5` | remembered, else `0.8` | Master volume. |
| `shuffle` | `on` \| `off` | `off` | What **next** means: `off` walks this station (`n+1`), `on` jumps to a whole new station at song 0 each time. NOT "a different vibe every song" — that is what a seed with nothing pinned already does, and it needs no attribute. |
| `share-base` | a URL | — | The page that reproduces a seed on your site; the copy buttons hang `#<seed>` off it and say "copy link". Absent, they copy the seed alone. |

Properties: `seed` (get/set — the seed AS IT STANDS, rolled parts left out), `playing`, `position`, `themeInfo`, and methods `play()`, `pause()`,
`seek(seconds)`, `load()`, `refreshTheme()`.

`position` is `{n, time, duration, ratio, playing}`, measured off the audio clock. **`duration` is
`0` until the song is rendered** — songs differ in length, so there is no length to state before
then, and the widget's own bar goes inert rather than drawing against a guess.

Events (all `CustomEvent`, bubbling and composed): `song` (`{n, position, seed, resolvedSeed, vibe, tag, genre, genreName, genrePinned, vibePinned}` — `n` is the song's index in its own station, `position` its slot on the timeline, and the two differ only under `shuffle`),
`play`, `pause`, `position` (the `position` shape), `progress` (`{loaded, total, ratio, done}`),
`ready`, `error`, and `theme` (`{mode, accent, source}`).

The `position` event marks the **discontinuities** — a pause, a resume, a scrub, a new song. A host
drawing its own progress bar polls `el.position` on an animation frame instead; the widget does, and
that is why there is no per-frame event to subscribe to.

Pause is a pause: the transport cannot suspend the AudioContext (it is shared with the other widgets
on the page), so it carries the playhead itself and the next play re-schedules the PCM it is already
holding. Seeking within a song is the same move with a different offset — the whole song is in
memory, so a scrub costs nothing to fetch.

Static: `SkafinityPlayerElement.playerDefaults` — transport options every element on the page is
built with. A page that already runs Web Audio sets `audioContext` here (before the first widget
upgrades) so the widgets join its graph instead of opening another.

### The URL is not the element's to read or write

The element **never** touches `location` — two widgets on one page would fight over one address bar,
and an embed has no claim on the address of the page that took it in. That cuts both ways, so the
copy button cannot work out a shareable URL either: `location.href` inside an embed is the *host's*
page, which reproduces nothing unless that page wired the seed up. Tell it what does, and the button
becomes "copy link" instead of "copy seed":

```html
<skafinity-player share-base="https://example.com/tunes/"></skafinity-player>
```

Anything after a `#` in the base is replaced with the seed that is playing when the button is
pressed. Absent, it copies the bare seed, which reproduces the song in any skafinity.

**There are two copy buttons, because there are two things "share this" can mean.** The default one
hands over the song **fully resolved** — the genre and vibe written down even where this station
rolled them — so the recipient hears what is playing here. The second hands over the string **as it
stands**, so a station that is still rolling keeps rolling for them too.

Honouring an inbound link is the host's other half, and `web/app.js` is the worked example:

```js
const hash = location.hash.slice(1);
if (hash) el.setAttribute('seed', hash);            // before the definition loads
el.setAttribute('share-base', location.href.split('#')[0]);
```

The skafinity site deliberately does **not** write the seed back out as the song changes: a shuffled
station would rewrite the address every ~75 seconds, filling the back button with songs nobody chose
to go back to, and any address copied by hand is stale as soon as the next song starts. The link is
built on demand by the copy button instead. Nothing stops a host doing the opposite — a
`history.replaceState` on the `song` event is all it takes.

---

## Theming — what is sniffed, and where it gives up

On connect (and on any `theme`/`accent` change, and when the OS colour scheme flips) the element
inserts a throwaway `<button>` and `<a>` where it sits, measures them, throws them away, and derives
a palette:

- **Accent**, first hit wins: the `accent` attribute → `--ska-accent` → `--accent` → `--primary` →
  `--color-primary` → `--color-accent` → `--bs-primary` → the page's link colour → neutral gray.
- **Light or dark** from the first non-transparent `background-color` at or above the element.
  `prefers-color-scheme` is consulted **only** when the page paints no background anywhere. A dark
  widget on a light page is the failure this ordering exists to avoid.
- **Font and border-radius** are taken off the probed button, because a page that styles its buttons
  has said more about its house style than `<body>` has.
- **Everything else** is derived from that one accent using the factors in
  `sbox-library/Skafinity/Code/UI/SkafinityTheme.cs`, so the element and the in-game s&box panel are
  the same palette: surfaces at `Scale(0.09)`/`Scale(0.04)`, fills at `Scale(0.6)`, ink at
  `Mix(0.8)`/`Mix(0.72)`. Light mode is those same factors reflected — `1 - f`, with `Scale` and
  `Mix` swapped — rather than a second colour scheme. `test/palette.mjs` reads the factors out of the
  C# and fails if the two ever disagree.

**It is a guess and it is allowed to be wrong.** It cannot see a background image, a gradient, a
`color-mix()` it would have to evaluate, a canvas, or a colour that arrives after it looked. What it
gives you is a widget that is usually right and always overridable:

```html
<!-- an instruction beats a guess -->
<skafinity-player theme="dark" accent="#ff5c2a"></skafinity-player>
```

```css
/* or override any single derived token; the rest still derive */
skafinity-player { --ska-bg: #101014; --ska-text: #eee; }
```

`--ska-accent` `--ska-bg` `--ska-cell` `--ska-fill` `--ska-fill-soft` `--ska-accent-bg` `--ska-text`
`--ska-text-dim` `--ska-edge` `--ska-rule` `--ska-hover` `--ska-pick` `--ska-busy` `--ska-surface`
`--ska-font` `--ska-radius`.

If a host's own theme changes at runtime, call `el.refreshTheme()` — the skafinity site's own
light/dark switch does exactly that rather than setting `theme=`, which is how the sniff stays
honest on the flagship page.

### `::part()`

The shadow root keeps the host page's CSS out (and the widget's out of the page), so anything you
want to restyle has to be a part: `board` `transport` `transport-button` `play-button` `now-playing`
`buffer-state` `volume` `volume-slider` `seek` `seek-slider` `time-elapsed` `time-total`
`progress` `progress-bar` `seed-bar` `seed-input` `seed-go`
`seed-copy` `seed-copy-station` `vibe-controls` `genre-select` `reroll-button` `shuffle-button`
`tinker-button` `panel` `vibe` `vibe-actions` `vibe-roll` `vibe-random` `knob-slider`
`knob-select` `playlist` `playlist-row` `playlist-row-now` `jump` `jump-input` `jump-go`
`export-button` `button` `slider` `message`.

```css
skafinity-player::part(play-button) { border-radius: 50%; }
```

---

## Lazy boot

`web/_framework` is ~7.5 MB of AOT-compiled runtime. The element fetches **none** of it on page
view: the first `play()` (or `preload="auto"`, or an explicit `load()`) starts the download, and the
widget shows a progress bar with real megabytes while it happens.

**What the widget knows before it boots is the seed it was given**, and it shows it: a `seed`
attribute (or a `seed` property set from a link's hash) lands in the seed box straight away, and
`copy link` hands out a link that reproduces it — none of which needs the engine. Until then the
transport reports that seed *as* `seed` rather than inventing one from its placeholder tag, so
what is on screen and what a copy yields are the same string. Everything else about the song —
the genre, the vibe knobs, the playlist — arrives with the runtime.

The total is not knowable up front — the boot config carries names and hashes but no sizes — so it
is the sum of the `Content-Length`s seen so far. The runtime starts every asset download together,
so it settles within a round trip; until it does the bar is an honest indeterminate sweep rather
than a bar parked at zero.

---

## Cross-origin

Serving the widget from a different origin than the host page works, with one requirement: **the
files need CORS headers.** Module scripts are fetched with CORS, and so is the runtime.

```
Access-Control-Allow-Origin: *
```

The `Worker` constructor is the one place this cannot be worked around by a header, because it
refuses a cross-origin script URL outright. `player.js` handles it the same way
`tools/bundle-single.mjs` does: a same-origin blob module whose only statement is an absolute import
of the real worker. You do not have to do anything, but if the CORS header is missing the symptom is
specific — the widget boots, and then no song ever renders.

---

## Two widgets on one page

They share one `AudioContext` and **one pool of three generation workers**, because a per-instance
pool would mean three .NET runtimes per embed. The pool is fair: a widget cannot hold more than its
share of workers while another has work queued, and a widget that has just been asked to play with
nothing cached can take a slot off another widget's *look-ahead* (which is speculative — that song
is 80 seconds away) rather than waiting a whole render out.

Removing an element from the document tears its transport down: workers released, interval cleared,
audio stopped. The shared `AudioContext` is deliberately not closed — another widget may be on it,
and a closed one cannot be reopened.

---

## Using the transport without the UI

`web/player.js` is a plain class and knows nothing about the DOM:

```js
import { SkafinityPlayer } from './player.js';

const p = new SkafinityPlayer({ seed: 'gamah:0', storageKey: 'my-app' });
p.addEventListener('song', (e) => console.log('now playing', e.detail.seed));
await p.play();
```

Options: `seed`, `tag`, `volume`, `shuffle`, `storage` (a `Storage`, or `null` for none),
`storageKey`, `configUrl` (`null` to skip the house-mix fetch), `audioContext`, `createWorker`,
`pool`, `poolSize`, `engine` (an already-booted engine to use instead of booting one).
