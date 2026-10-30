// The voice of the assistants on this phone (phase 6): an order written or said here can be answered here. The PC sends
// each sentence as it is ready (event "speech", the audio in api/assistants/audio) and they play one after another,
// with no gaps; "speechEnd" says the turn is over (or was cut). In the Android app the app plays them itself, so they
// keep sounding in the background or with the screen off. With «Seguir escuchando», when an answer ends the
// microphone opens again for that assistant.

let ctx;                     // { t, store, talk(assistant), visible() }
const s = {
  audio: null,               // the Web Audio context (made on a touch: browsers only allow it then)
  next: 0,                   // when the next piece starts (seconds of the context)
  chain: Promise.resolve(),  // the pieces are fetched in order
  turns: new Map(),          // turn → { assistant, sources: Set, pending: 0, ended: false }
};

const $ = id => document.getElementById(id);
const app = () => (window.KDesktopApp?.playSpeech ? window.KDesktopApp : null);

export function setupSpeech(deps) {
  ctx = deps;
  // A touch anywhere lets the page make sound later (send, the microphone…).
  document.addEventListener('pointerdown', prime, { capture: true, passive: true });
  const where = $('speakWhere');
  where.value = speakWhere();
  where.addEventListener('change', () => ctx.store.set('speakWhere', where.value));
  const follow = $('speakFollow');
  follow.checked = !!ctx.store.get('speakFollow', false);
  follow.addEventListener('change', () => ctx.store.set('speakFollow', follow.checked));
  // The app says when it has played the last piece of a turn.
  window.kdSpeechDone = turn => done(turn, false);
}

/** Where the answers to this phone's orders sound: phone, pc or both (the PC's own browser: on the PC). */
export function speakWhere() {
  if (ctx.local?.()) return 'pc';
  const value = ctx.store.get('speakWhere', 'phone');
  return ['phone', 'pc', 'both'].includes(value) ? value : 'phone';
}

/** The settings of the assistants: only with the module on; where they sound, not in the PC's own browser. */
export function speechSettings(enabled, local) {
  $('speechSettings').hidden = !enabled;
  $('speakWhereRow').hidden = local;
  $('speakWhere').value = speakWhere();
}

function prime() {
  if (app()) return;
  try {
    s.audio ??= new AudioContext();
    if (s.audio.state === 'suspended') s.audio.resume();
  } catch { /* no Web Audio */ }
}

function turnOf(turn, assistant) {
  let entry = s.turns.get(turn);
  if (!entry) {
    // A new answer: whatever another one was still saying stops.
    for (const [other, old] of s.turns) if (old.assistant === assistant) stopTurn(other);
    entry = { assistant, sources: new Set(), pending: 0, ended: false };
    s.turns.set(turn, entry);
  }
  return entry;
}

/** Event "speech": one more piece of an answer for this phone. */
export function onSpeech(data) {
  if (!data?.turn) return;
  const path = `api/assistants/audio?turn=${encodeURIComponent(data.turn)}&n=${data.n}`;
  const entry = turnOf(data.turn, data.assistant);
  const native = app();
  if (native) {
    native.playSpeech(path, data.turn);
    return;
  }
  entry.pending++;
  s.chain = s.chain.then(() => play(path, data.turn, entry)).catch(() => {}).finally(() => {
    entry.pending--;
    check(data.turn);
  });
}

async function play(path, turn, entry) {
  if (!s.turns.has(turn)) return;
  const answer = await fetch(path, { credentials: 'same-origin', headers: ctx.headers() });
  if (!answer.ok) return;
  const data = await answer.arrayBuffer();
  prime();
  if (!s.audio || !s.turns.has(turn)) return;
  const buffer = await s.audio.decodeAudioData(data);
  const source = s.audio.createBufferSource();
  source.buffer = buffer;
  source.connect(s.audio.destination);
  const at = Math.max(s.audio.currentTime + 0.03, s.next);
  source.start(at);
  s.next = at + buffer.duration;
  entry.sources.add(source);
  source.onended = () => {
    entry.sources.delete(source);
    check(turn);
  };
}

/** Event "speechEnd": the PC said all of it, or it was cut. */
export function onSpeechEnd(data) {
  if (!data?.turn) return;
  if (data.stopped) {
    stopTurn(data.turn);
    return;
  }
  const entry = s.turns.get(data.turn);
  const native = app();
  if (native) {
    native.endSpeech(data.turn);
    return;
  }
  if (!entry) return;
  entry.ended = true;
  check(data.turn);
}

/** Over when the PC said it ended and nothing of it is waiting or sounding. */
function check(turn) {
  const entry = s.turns.get(turn);
  if (entry?.ended && entry.pending === 0 && entry.sources.size === 0) done(turn, false);
}

function done(turn, stopped) {
  const entry = s.turns.get(turn);
  s.turns.delete(turn);
  if (!entry || stopped) return;
  // «Seguir escuchando»: the conversation goes on without touching anything.
  if (ctx.store.get('speakFollow', false) && ctx.visible()) setTimeout(() => ctx.talk(entry.assistant), 250);
}

function stopTurn(turn) {
  const entry = s.turns.get(turn);
  s.turns.delete(turn);
  app()?.stopSpeech();
  if (!entry) return;
  for (const source of entry.sources) {
    try { source.stop(); } catch { /* not started */ }
  }
  entry.sources.clear();
  if (s.audio) s.next = s.audio.currentTime;
}

/** Stop pressed on this phone: silence at once (the PC is told to stop too). */
export function stopSpeech(assistant) {
  for (const [turn, entry] of [...s.turns]) if (!assistant || entry.assistant === assistant) stopTurn(turn);
  app()?.stopSpeech();
}
