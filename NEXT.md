# NEXT — external voices, and whether the score fits MIDI

A brief for a future session, written at the end of the one that built the physical
models. **Delete this file when the work it describes is done** — whatever a later
session would get wrong without it belongs in `CLAUDE.md`, and everything else belongs
in git.

---

Explore replacing skafinity's synthesised voices with existing instrument code, and
render a song per genre with whatever you land on. Take as many turns as you need and
ask me whenever a call is mine.

You are on `explore/vst-voices`, which carries this brief on top of the physical-model
work — a delay-line string, 2-op FM, modal banks, and a cascaded amp
(`Engine/Synth/Models.cs`, `StringModel.cs`, `FmModel.cs`, `ModalModel.cs`, `Amp.cs`).
None of that is in `CLAUDE.md` yet. `--tone` is the pitched audition, `--levels` the mix
tool, `--render tag:n:genre` writes a WAV. `web/_framework` on this branch is stale
against five commits of engine changes, so a full publish is needed before any web or
dist test.

## Settle feasibility first and tell me what you find

It may kill the plugin idea outright, and that is an acceptable answer.

- A VST/CLAP plugin is native code. The web toy is wasm in a browser with no server and
  no audio assets, and "the entire experience is a URL" is the product. Say which of
  these you are proposing: **(a)** something that ships to BOTH targets, **(b)** an
  offline-only high-quality render path while the browser keeps the built-in voices, or
  **(c)** a throwaway experiment to learn timbre from and then reimplement. Do not let
  (c) arrive dressed as (a).
- **Licensing is the gate, not an afterthought.** Re-derive it from primary sources and
  date what you write down: VST3 SDK terms, CLAP's licence, and the licence of any DSP
  code you want to vendor. MIT-licensed **portable C or C#** that can compile into
  `Engine/**` is worth far more to this project than any plugin, because it keeps one
  build and two targets. Also check whether any plugin or host actually exists on this
  box before assuming one can be driven.
- Report the cost: wasm size, render speed against real time (we pre-render ~75 s songs
  far faster than real time today, and the look-ahead depends on it), and whether
  determinism survives.

## Does what we have fit in MIDI?

**That is an OPEN QUESTION** — work it out and answer it with numbers, not vibes. The
previous session assumed it did not; the reasoning behind that was weak, so treat it as
unsettled. Split it in two: the **score** (what note, when, how long, how hard, what
gesture) and the **instrument** (the `Patch`). A patch is not score data, so "MIDI can't
carry a synth patch per note" is probably not an objection worth anything.

Check these against the actual specs, re-derived and dated:

- **Resolution.** We author at 48 ticks/beat but positions become sample offsets, and
  some gestures are deliberately physical rather than metrical — the kit's
  push/lay-back in samples, the strum's ~4 ms per string. What PPQ or SMPTE division
  does an SMF need to carry that, and does variable-length delta-time encoding cope at
  that rate?
- **Per-note gestures.** Bend-in, scoop, bend-up and vibrato are per note, and the
  bend's windows are in SECONDS. Does MPE (a channel per note) or MIDI 2.0 (per-note
  controllers, per-note pitch bend) carry them, and what does that cost in channels
  given our polyphony?
- **Velocity.** Ours is continuous, MIDI 1.0 is 7-bit. Measure whether 128 steps is
  audibly lossy through `NoteGain` before calling it a problem.
- **Tempo.** `Timing` is an accumulator with per-section `TempoMul` and an ending
  ritard, and swing is a warp applied in `TickToSample`. Does a tempo map reproduce
  that exactly, or does the swing have to be baked into note positions instead?
- **The kit is NOT in `_events`** — `Drums/Kit.cs` renders straight into the buffer —
  so either add a drum event stream or say the kit stays ours.

If it fits, an export is a clean way to drive anything external and to open the project
up to other instruments; build it as a real export rather than a debug dump. If it only
nearly fits, say exactly where it tears, and keep the engine's own model as the source
of truth rather than degrading it to match.

## Finish with

Six songs, one per genre, rendered through whatever you chose, and an honest comparison
against this branch's current voices — **including where the new thing is worse**.
Audition before wiring, the way the drums and the pitched voices were. Ask before
vendoring anything, and ask about the (a)/(b)/(c) call above.
