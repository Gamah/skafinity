using System;
using System.Collections.Generic;

namespace Skafinity;

// Config — every knob the composer and synth read. Nested in MusicGen so `MusicGen.Config`
// still resolves. Fields here are either vibe knobs (VibeCodec grids / GlobalFields) or
// house-mix values (VibeCodec.AdvancedFields); either way a new field must also be added to
// Cfg.To/Cfg.From in wasm/Exports.cs or it will not survive the JS boundary.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	public sealed class Config
	{
		// Genre — selects the instrument set + arrangement. 0 = Ska (bass/skank/organ/lead/
		// horns/drums), 1 = Rock (drums/bass/keys/lead-gtr/rhythm-gtr), 2 = Country, 3 = Metal,
		// 4 = Punk (lean power-pop), 5 = Pop (synth/dance). New genres append here.
		public int Genre = 0;

		// Output
		public int SampleRate = 44100;
		public float TargetSeconds = 80f; // (legacy) song length now follows the structure
		public int Bars = 64;             // fallback if TargetSeconds <= 0

		// Tempo — main is laid-back reggae-rock; Fast is an uptempo ska feel.
		public int BpmMin = 130, BpmMax = 185;
		public float FastChance = 0.30f;
		public int FastBpmMin = 150, FastBpmMax = 168;
		// Swing is not a Config value: it is per-genre character drawn per song (GenreProfile).

		// Mix (per-voice gain pre-master) — the six "volume" sliders are normalized:
		// same 0..1.5 range and the same 1.0 default (flat mix), tune from there.
		public float BassVol = 1.00f;
		public float SkankVol = 1.00f;
		public float OrganVol = 1.00f;
		public float MelodyVol = 1.00f;
		public float HornVol = 1.00f;
		// Per-part kit trims. The kit is balanced for EQUAL PEAK LEVEL in the file internally
		// (see *Balance consts below), so these knobs all share a 1.0 default — every piece
		// peaks at the same level out of the box and these are pure user trims around that.
		public float KickVol = 1.00f;
		public float SnareVol = 1.00f;
		public float TomVol = 1.00f;
		public float HatVol = 1.00f;
		public float CrashVol = 1.00f;
		public float DrumVol = 1.00f;         // master gain over the whole kit
		// Drum "tone" — toms↔cymbals bias for the PART. 0 = boom (fills/decoration lean to
		// toms, cymbals pulled back), 0.5 = neutral, 1 = bright (fills/decoration lean to
		// cymbals, toms pulled back). Drives both what's played and a gentle per-voice gain lean.
		public float DrumTone = 0.5f;
		// Drum "drive" — pull↔push timing feel (replaces the old DrumPush magnitude roll).
		// 0 = lay back behind the beat, 0.5 = dead-on, 1 = push ahead of the beat.
		public float DrumDrive = 0.5f;

		// Rock instruments (Genre 1). Bass + drums reuse the shared knobs above.
		// KEYS — the offbeat-chord comp (was labelled "rhythm guitar", but it always read as a
		// keyboard, so it's named for what it sounds like). Power-chord comping on every eighth.
		public float KeysVol = 1.00f;
		public float KeysCutoff = 1700f;      // Hz low-pass (darker wall)
		public float KeysDrive = 3.2f;        // distortion amount (tanh drive)
		public float KeysChug = 0.5f;         // 0 = ringing chords, 1 = tight palm-mute chug
		// RHYTHM GTR — twangy distorted power chords. Shares the lead guitar's voice but strums
		// chords at a lower base distortion than the lead.
		public float RhythmGtrVol = 1.00f;
		public float RhythmGtrCutoff = 2600f; // Hz low-pass on the rhythm guitar
		public float RhythmGtrDrive = 2.8f;   // distortion amount (tanh drive) — under the lead
		public float RhythmGtrChug = 0.5f;    // 0 = ringing chords, 1 = tight palm-mute chug
		// LEAD GTR — twangy, heavily distorted single-note lead.
		public float LeadGtrVol = 1.00f;
		public float LeadGtrCutoff = 2600f;   // Hz low-pass on the lead guitar
		public float LeadGtrDrive = 5.0f;     // distortion amount (tanh drive) — new floor of the DISTORTION knob
		public float LeadGtrBend = 0.30f;     // rock lead "bendiness" 0..1 — propensity to bend up into notes and scoop

		// Tone — low drives + filtering for warmth; detune for width.
		public float Detune = 14f;        // cents, unison spread
		public float BassCutoff = 380f;   // Hz low-pass on bass
		public float SkankCutoff = 3000f; // Hz low-pass on skank
		public float SkankHighpass = 500f;// Hz high-pass to thin the skank ("skank bite")
		public float SkankChop = 0.5f;    // skank chop length as a fraction of an eighth
		public float LeadCutoff = 3200f;  // Hz low-pass on lead
		public float OrganCutoff = 1400f; // Hz low-pass on the organ bubble
		public float OrganVibrato = 5.5f; // organ bubble vibrato depth
		public float HornCutoff = 3200f;  // Hz low-pass on the backing horns
		public float Resonance = 1.0f;    // SVF damping (lower = more resonant)
		public float BassDrive = 1.5f;
		public float SkankDrive = 1.3f;
		public float MelodyDrive = 1.3f;
		public float HornDrive = 1.4f;
		public float MasterDrive = 1.1f;
		public float MasterPeak = 0.95f;

		// Master room reverb — a touch of stereo space so the mix reads with depth
		// instead of dry/"16-bit". Wet = blend, Decay = tail length (0..1).
		public float MasterReverb = 0.5f;
		public float ReverbDecay = 0.5f;

		// Feel
		public float OctavePopChance = 0.30f;
		public float OrganBubbleChance = 0.55f;
		public float KickSyncChance = 0.25f;
		public float GhostSnareChance = 0.35f;
		public float FillChance = 0.6f;       // drum fill at phrase ends
		public float DrumBusy = 0.6f;         // 0..1 overall kit activity: 16th hats, ghosts, kick syncopation
		public float TripletChance = 0.06f;   // 0..0.2 chance of triplet/16th ornament in fills / lead runs (potent)
		public float BassTriplets = 0.06f;    // 0..0.1 bass-only 16th/triplet ornament rate (own knob)
		public float MelodyRestChance = 0.30f;
		public float MelodyLeapChance = 0.18f;
		public float MelodyVibrato = 5.0f;

		// STEREO WIDTH (vibe slider): master 0..1 stereo amount. Scales the lead/horn placement
		// AND the drum pan + double-tracking spread/decorrelation (see _widthScale). 1 = full
		// design width (the "100%" the rest of the mix is tuned for); 0 = mono.
		public float PanAmount = 1.0f;

		// Lead instrument weights (RNG picks one; ForceInstrument overrides:
		// -1 = RNG, 0=Trumpet 1=Sax 2=Organ 3=Trombone).
		public float TrumpetWeight = 1.0f;
		public float SaxWeight = 1.0f;
		public float OrganWeight = 0.8f;
		public float TromboneWeight = 0.4f;
		public int ForceInstrument = -1;

		// Backing horn section
		public float HornSectionChance = 0.5f;
		public float HornDensity = 0.35f;

		// ── Advanced peak-balance / level tuning (NOT vibe knobs) ──
		// These were hardcoded consts; they're Config fields so the house mix can be retuned at
		// runtime (web: config.json) without a rebuild. They're deliberately kept OUT of the vibe
		// seed and the per-genre slider grid (see VibeCodec.AdvancedFields) — they shape the
		// baseline mix, not a song's shareable identity. Edit these to re-balance, not the *Vol
		// defaults (which stay a flat 1.0 so the knobs read as pure user trims around this).
		//
		// KitPresence: drums are short transients fighting a sustained melodic bed; this baseline
		// boost lets the kit sit in the mix at DRUMS = 1.0 (parity with the other voice sliders).
		public float KitPresence = 2.0f;
		// Kit per-voice level balance. The pieces synthesise at very different raw levels (a kick
		// is huge, a hat is thin noise). Starting point was EQUAL PRE-MASTER PEAK (kick 0.40 /
		// snare 0.476 / tom 0.496 / hat 0.582 / crash 0.515), then hand-tuned by ear: equal peak
		// buried the sparse kick/snare/toms under the constant hats, so drums were pushed back up
		// and the hats backed off.
		public float KickBalance = 0.800f;
		public float SnareBalance = 0.900f;
		public float TomBalance = 0.780f;     // tuned by ear
		public float HatBalance = 0.407f;     // equal-peak baseline (0.582) backed off 30% by ear
		public float CrashBalance = 0.515f;
		// Melodic-voice peak balance — the instrument analog of the kit balances above (same
		// equal-peak-then-tune workflow). Bass measured the same peak across genres, so one value.
		public float BassBalance = 0.733f;
		public float SkankBalance = 1.223f;
		public float OrganBalance = 1.237f;
		public float MelodyBalance = 0.896f;  // ska horn lead
		public float HornBalance = 1.142f;    // backing horn section
		public float KeysBalance = 1.065f;    // rock offbeat keys
		public float RhythmGtrBalance = 1.053f;
		public float LeadGtrBalance = 0.896f;

		// ── Stereo double-tracking (width) ── (config-only; see VibeCodec.AdvancedFields)
		// Widen the non-drum voices the way a band double-tracks guitars (bass excepted — it
		// stays centred for a tight, mono-safe low end): each widened note is rendered
		// as TWO decorrelated "takes" panned apart — NOT one mono signal copied to both channels
		// (that just collapses to centre). The width comes entirely from the micro-differences
		// between the takes: a few cents of detune, a small constant timing offset + per-note
		// jitter, independent oscillator start phase, and per-note amplitude/cutoff variation.
		// Tunable at runtime (no rebuild). Drums are panned separately (see DrumPan).
		public float DoubleTrack = 1f;        // master enable: <0.5 = off (single centred take, as before)
		public float WidthBacking = 0.5f;     // pan spread (±) for backing voices — 50% L/R
		public float WidthLead = 1.0f;        // pan spread (±) for a doubled lead. INERT by default: the
		                                      // lead is monophonic and rendered as a single clean take
		                                      // (see RenderLeadNote) — doubling a solo line smeared pitch
		                                      // ("out of key"). Only takes effect if the lead path is
		                                      // switched back to lead:true. Chordal voices use WidthBacking.
		public float WidthDetune = 6f;        // cents BETWEEN the two takes (split ±half each)
		public float WidthDelayMs = 9f;       // constant timing offset of the 2nd take (ms)
		public float WidthJitterMs = 4f;      // extra random per-note timing jitter (ms, ± per take)
		public float WidthAmpVar = 0.08f;     // per-note amplitude variation between takes (0..1)
		public float WidthCutoffVar = 0.10f;  // per-note filter-cutoff variation between takes (0..1)
	}
}
