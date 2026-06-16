// Generation worker — owns its own WASM instance so rendering never blocks the UI or
// audio thread (mirrors MusicController.FillAhead running on a worker). It only renders;
// scheduling/crossfade live on the main thread.
import Skafinity from './engine.js';

let mod = null;
const ready = Skafinity().then((m) => { mod = m; });

self.onmessage = async (e) => {
  await ready;
  const msg = e.data;
  if (msg.type !== 'gen') return;
  const { id, seed, cfg, n, mySeq } = msg;
  try {
    const song = mod.generateSong(seed, cfg);
    const left = song.left, right = song.right; // Float32Array copies (off-heap)
    self.postMessage(
      { type: 'song', id, n, mySeq, sampleRate: song.sampleRate, left, right, info: song.info },
      [left.buffer, right.buffer]
    );
  } catch (err) {
    self.postMessage({ type: 'error', id, n, mySeq, error: String(err) });
  }
};
