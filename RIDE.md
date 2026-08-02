# The ride — SOLVED (round 8); this file is now history

**Resolution, 2026-08-02:** the ride was solved by abandoning every noise-based approach below and
MEASURING a real cymbal (Virtuosity Drums, CC0), then keeping only the closed-form laws the
measurement collapsed into — τ·√f ≈ 39 for the ring, a mode forest at constant density with
beating near-pairs, each strike position a log-Gaussian spectral bump over one shared forest, and
splash/wash noise as the bed under the modes rather than the identity. The synthesis is
`RideModal`/`RenderRideModal` in `Kit.cs`; the verdict on round 8 was *"these are all honestly
great"*, every line. `DRUMS.md`'s "measured-cymbal method" section is the living recipe (the
crashes are next); what follows below is kept as the record of four failed noise generations and
why they failed. A fifth is not needed.

Branch `drums/kit-rework`. The original handoff:

The bell is the blocker and most of this document. The bow articulations are NOT settled either,
though — see "the ping, and why it matters here" below, which is the one piece of good news and is
probably where a fresh attempt should start.

`DRUMS.md` has the branch's wider context, but **this file is self-contained**: everything needed
to work on the ride is here.

## The one-paragraph version

skafinity synthesises its drum kit from scratch (`Engine/Drums/Kit.cs`). The kit is being reworked
on this branch, gated on an audition: candidates are rendered as short musical figures, handed to
the listener as a WAV, and only wired into the grooves once approved. Everything except the ride has
passed. The ride's bell has failed three times, and its bow articulation only started sounding like
a ride at all in the last round. **Your job is the ride.**

## The problem

`RenderRide` can play a bell. It does not sound like one. Three generations have now failed, and
they failed in three different ways, which is the useful part of this document.

**Generation 1 — sine partials.** Inharmonic sine partials layered over the noise wash. Recorded
in `Kit.cs`'s own history and deleted: *any* tonal layer, however quiet and however detuned, read
as a pitched "ding" rather than as a cymbal. Do not simply re-run this. If a tonal component comes
back it needs a reason why this time is different, not a smaller gain.

**Generation 2 — lightly band-passed noise.** Two band-pass filters at 2.6 and 5.25 kHz over the
wash, Q around 0.13. Verdict: *"hats in weird states."* Correct, and the cause is structural — a
noise band with a gentle resonance on it **is** a filtered hat. There is no setting of a filter
that turns a wash into a struck object.

**Generation 3 — high-Q modes, strike-excited.** Four inharmonic modes (1180 / 2360 / 3510 /
5290 Hz) at Q ≈ 0.012, excited by a 3.5 ms strike envelope rather than by the continuous noise
stream, ringing ~1 s under a wash pulled back to 0.16. This is the current state of
`RideTone.BellNarrow` / `BellWide` / `BellWashy`. Verdict: unprintable. Worse than generation 2,
not better.

## What the three failures have in common

Generation 3 fixed two things that were genuinely wrong — a resonator fed continuous noise is a
filter on a wash, and a `Q` that doubles as a level control cannot be tuned against anything (the
band-pass is normalised by Q now, in `BandPass.Next`, and that fix is worth keeping whatever
happens to the bell). It still failed. So the defect is **not** in the excitation and **not** in
the filter bank's gain staging.

What has never been addressed is that **a bell's modes are not a static filter bank at all.**
Three properties of a struck bell that none of the three generations has:

- **Per-mode decay.** A single `BandDecayFrac` is applied to every mode at once. On a real bell
  the low modes ring for seconds and the high ones are gone in tens of milliseconds, and that
  divergence over time is a large part of what identifies the sound. A stack that decays as one
  block is a chord, not a bell.
- **A pitch glide.** Struck metal's modes fall slightly in the first tens of milliseconds as the
  strike energy dissipates. Nothing here does that.
- **Beating between close modes.** Real bell partials come in near-pairs a few Hz apart and the
  audible warble is diagnostic. Four isolated modes cannot produce it.

There is also a live question nobody has answered: **is the sound wanted actually a bell?** The
engine's ride is a noise wash by design and that decision is documented and deliberate. It is
possible that the bell needs to be a genuinely different synthesis method — modal synthesis with
per-mode envelopes, or a short waveguide/Karplus-Strong-like resonator — sitting beside the noise
voice rather than being coaxed out of it. That would be a bigger change than a preset, and it is
allowed: `DRUMS.md` never promised the bell comes out of `RenderRide`.

## What the listener has actually said

Verbatim, because the wording is diagnostic and second-hand summaries of listening notes lose the
thing that matters.

| Candidate | Verdict |
|---|---|
| Bow, today's (`RideTone.Bow`) | never objected to, never praised — it is what ships |
| Ping, high-passed noise + noise stick | *"this is not a ping"* |
| Wash, shoulder on the bow (`RideTone.Wash`) | *"this passes as a wash"* — **approved** |
| Edge, crash-ride | no comment either way |
| Bells, generation 2 (light band-pass) | *"none of these sound like bells, they almost all sound like hats in weird states"* |
| Bells, generation 3 (high-Q, strike-excited) | *"everything sucks soooooooooooooooooooo bad"* — worse, not better |
| Ping + low modes (`PingLowA/B/Two`) | *"interesting"* |
| Ping + low modes, rung long | *"especially … sounds interesting and might be a good input for … fix the ride in general"* |
| Swell (a plain loop of ride hits, fixed spacing) | *"fine as long as it's derived from continuous hits in a repeatable way"* — **approved**, and it is |

Two things to take from that table. The wash and the swell are done and should not be re-opened.
And **every failure has been on the STRUCK end of the ride** — the ping and the bell — which is
where a strike has to produce a defined point of contact, and where noise-through-a-filter has
nothing to offer.

## How to work here

`make` is not available on this host. Run the underlying commands:

```
DOTNET=~/.local/share/toolchains/dotnet10/dotnet

# the engine suite — must stay 483/483 with no digest failures        (~25 s)
$DOTNET run --project test/engine -c Release

# render the ride audition                                            (~5 s)
$DOTNET run --project test/engine -c Release -- --audition ride ~/audition.wav
```

That writes `~/audition.wav` and `~/audition.txt` (a numbered, timestamped script), outside the
repo so the listener can pull them off the VM. **Then hand both over and wait.** Do not wire
anything into the grooves on the strength of your own judgement of the audio — the whole branch is
built around the fact that nobody here can hear it.

Add candidates in `Audition.Ride` in `test/engine/Audition.cs`. Four rules that the rounds so far
have earned:

- **Play a figure, not a hit.** A bell is judged alternating with the bow, because that is how it
  is played and how the ear places it.
- **Keep it short.** The whole file wants to be a couple of minutes at most; a line is about a bar
  plus the beat it lands on.
- **One gain over the whole file, never per line.** A candidate being louder than the last is
  information. `Audition.Write` does this already — do not add a per-line normalise.
- **Sweep the thing in question and bracket it.** A candidate that is "fine" tells you much less
  than a pair that brackets it, and a sweep that comes back indistinguishable is itself a finding
  (that is how the hat's linear openness map was caught).

Note the default `--audition` set is currently the hats; the ride only renders when named. If the
ride becomes the active work, move it into the default set in `Audition.Run`.

## The constraints anyone picking this up must keep

- **The ten render digests must not move.** Phase 1 wires nothing into the grooves, so
  `dotnet run --project test/engine -c Release` must stay at 483/483 with no digest failures.
  Anything new is reachable only from the audition until it is deliberately wired in Phase 2.
  Watch the float arithmetic: a `float` field widened into a `double` decay is not the same number
  as the `double` literal it replaced, and that alone moved all ten digests once already.
- **No `Sandbox.*` and no web-isms in `Engine/`.** The same source compiles to wasm and to s&box.
- **`_drumGain <= 0` returns early inside every voice**, before `_time.DrumPush` and before any
  `noise.Next()`.
- **No tonal layer without an argument for why it is not generation 1.**

## Where everything is

| Thing | Where |
|---|---|
| The voice | `sbox-library/Skafinity/Code/Engine/Drums/Kit.cs` → `RenderRide`, `RideTone`, `BandPass` |
| The failed candidates | `RideTone.BellNarrow` / `BellWide` / `BellWashy` |
| The incumbent (bow rung longer — what the engine ships) | `RideTone.BellDurationOnly` |
| The bell's only caller | `RenderRide( start, bool bell, … )`, from `RenderDrumBar` in `Drums/Groove.cs` |
| The audition tool | `test/engine/Audition.cs`, `--audition [voice] [wavPath]` |

## How to audition a candidate

```
dotnet run --project test/engine -c Release -- --audition ride ~/bell.wav
```

Writes `~/bell.wav` and `~/bell.txt` (a numbered, timestamped script). Add lines in
`Audition.Ride`. Two rules the file's header explains and that the notes so far bear out:

- **Play a figure, not a hit.** A bell is judged alternating with the bow — that is how it is
  played and how the ear places it.
- **One gain over the whole file, never per line.** A candidate being louder than the last is
  information.

Rendering is a couple of seconds; the whole loop is edit → build → listen.

## The ping, and why it matters here

Round 3 of the audition found the first thing on this branch that read as a ride rather than as a
cymbal-shaped hat, and it was not a filter change. **The ping was given LOW MODES** — a ride is a
large piece of metal whose lowest modes sit in the hundreds of hertz, and every earlier attempt had
stick contact with nothing underneath it. `RideTone.PingLowTwo` (370 + 615 Hz under 3150 + 5400,
strike-excited) got the note "interesting"; the same tone with `dur: 0.85f, bandDecayFrac: 1.1`
— i.e. simply allowed to RING — got the strongest reaction the ride has had on this branch, and was
described as bell-adjacent.

**That is the lead, and it is the opposite end of the problem from where the bell attempts started.**
The bell generations all began from "what filter makes a bell" and worked down. This arrived at
something bell-like from "what does a large piece of metal sound like when it is struck and left to
ring", with no bell-specific machinery at all. A fourth generation that starts from `PingLowTwo`
ringing long and asks what has to be added — per-mode decay, a strike glide, close-mode beating —
is a different search from tuning `BellNarrow`'s stack, and the evidence so far favours it.

Corollary worth stating: if the bell falls out of the ping's own modes rung differently, then the
bell was never a second instrument, and `RideArt` may want to be a single voice with a strike
position rather than a table of presets.

## The state to come back to

The audition has settled the kick, the snare, the toms, the crashes and the hats; their approved
values are recorded in `KitNuance` in `Kit.cs` and are mostly **ranges**, because "all of these
work" is a finding about nuance rather than an undecided question. The ride's SWELL is settled —
a plain loop of ordinary ride hits at a fixed spacing, no envelope and no special case, so it
follows whatever the bow articulation ends up being.

Open: the bell, and the bow articulation it should be built alongside. Phase 2 wiring can proceed
around both — until they are solved a riding section keeps playing `RideTone.Bow` and
`BellDurationOnly`, which is what shipped before this branch and is not a regression.
