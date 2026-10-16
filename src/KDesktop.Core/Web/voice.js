// Voice orders from the phone: the order is recorded here and sent to the PC, which transcribes it with its own
// models and runs it like the orders said into its microphone. In a browser the microphone needs HTTPS (or
// localhost); the Android app records natively (window.KDesktopApp.startVoice), so plain http://IP works there.

const RATE = 16000;
const END_SILENCE = 900;     // ms of quiet after speaking: the order is over
const NOTHING_SAID = 6000;   // ms without speech: give up
const MAX_ORDER = 15000;     // ms, a little under what the PC accepts

let ctx;
const v = {
  state: 'idle',             // idle | listening | thinking
  native: false,
  chunks: [],
  started: 0, lastLoud: 0, heard: false, floor: 1, floorUntil: 0,
  stream: null, audio: null, node: null, carry: 0, timer: 0,
};

const $ = id => document.getElementById(id);
const nativeApp = () => window.KDesktopApp?.startVoice ? window.KDesktopApp : null;

export function setupVoice(deps) {
  ctx = deps;
  $('voiceButton').addEventListener('click', () => (v.state === 'idle' ? start() : stop()));
  $('voiceOrb').addEventListener('click', () => { if (v.state === 'listening') stop(); });
  $('voiceCancel').addEventListener('click', () => cancel());
  document.addEventListener('visibilitychange', () => { if (document.visibilityState !== 'visible') cancel(true); });

  // The Android app's recorder talks back through these.
  window.kdVoiceLevel = rms => onLevel(rms);
  window.kdVoiceData = base64 => {
    if (v.state !== 'thinking' || !v.native) return;
    const raw = atob(base64 || '');
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    send(bytes.buffer);
  };
  window.kdVoiceError = code => {
    if (v.state === 'idle') return;
    close();
    ctx.toast('error', ctx.t(code === 'denied' ? 'pwa.voice.denied' : 'pwa.voice.noMic'), '', 3500);
  };
}

/** The PC can understand voice orders (its models are downloaded): show the button. */
export function voiceAvailable(on) {
  $('voiceButton').hidden = !on;
  if (!on) cancel(true);
}

async function start() {
  navigator.vibrate?.(10);
  const app = nativeApp();
  if (!app && !navigator.mediaDevices?.getUserMedia) {
    // An older Android app, or a browser over plain http.
    if (window.KDesktopApp) ctx.toast('info', ctx.t('pwa.appUpdate.available'), ctx.t('pwa.appUpdate.where'), 5000);
    else ctx.toast('warning', ctx.t('pwa.voice.noMic'), ctx.t('pwa.voice.needsHttps'), 6000);
    return;
  }
  Object.assign(v, { state: 'listening', native: !!app, chunks: [], heard: false, floor: 1, carry: 0 });
  v.started = v.lastLoud = performance.now();
  v.floorUntil = v.started + 350;
  open();

  if (app) {
    const r = app.startVoice();
    if (r === 'error') window.kdVoiceError('mic');
  } else {
    try {
      v.stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      });
    } catch (e) {
      window.kdVoiceError(e?.name === 'NotAllowedError' ? 'denied' : 'mic');
      return;
    }
    if (v.state !== 'listening') return release();
    // A 16 kHz context resamples the microphone itself; some browsers refuse it: then resample here.
    let source;
    try {
      v.audio = new AudioContext({ sampleRate: RATE });
      source = v.audio.createMediaStreamSource(v.stream);
    } catch {
      v.audio?.close();
      v.audio = new AudioContext();
      source = v.audio.createMediaStreamSource(v.stream);
    }
    v.node = v.audio.createScriptProcessor(2048, 1, 1);
    v.node.onaudioprocess = e => {
      if (v.state !== 'listening') return;
      const samples = resample(e.inputBuffer.getChannelData(0), v.audio.sampleRate);
      v.chunks.push(samples);
      let sum = 0;
      for (const s of samples) sum += s * s;
      onLevel(Math.sqrt(sum / Math.max(1, samples.length)));
    };
    source.connect(v.node);
    v.node.connect(v.audio.destination); // silent: nothing is written to the output
  }
  v.timer = setInterval(watch, 100);
}

/** Speech or quiet: a floor measured in the first moments, speech well above it. */
function onLevel(rms) {
  if (v.state !== 'listening') return;
  const now = performance.now();
  if (now < v.floorUntil) v.floor = Math.min(v.floor, rms);
  // Capped: speaking right after the tap must not raise the floor to the voice itself.
  const threshold = Math.max(0.015, Math.min(v.floor, 0.01) * 3.5);
  if (rms > threshold) {
    v.heard = true;
    v.lastLoud = now;
  }
  $('voiceRing').style.setProperty('--level', Math.min(1, rms * 9).toFixed(3));
}

function watch() {
  if (v.state !== 'listening') return;
  const now = performance.now();
  if (v.heard && now - v.lastLoud > END_SILENCE) stop();
  else if (!v.heard && now - v.started > NOTHING_SAID) {
    cancel();
    ctx.toast('info', ctx.t('pwa.voice.nothing'), '', 2200);
  } else if (now - v.started > MAX_ORDER) stop();
}

/** Done speaking: send the order. */
function stop() {
  if (v.state !== 'listening') return;
  navigator.vibrate?.(8);
  clearInterval(v.timer);
  v.state = 'thinking';
  $('voiceSheet').classList.add('thinking');
  $('voiceText').textContent = ctx.t('pwa.voice.thinking');
  if (v.native) {
    nativeApp()?.stopVoice(); // answers with kdVoiceData
    return;
  }
  release();
  const length = v.chunks.reduce((n, c) => n + c.length, 0);
  const pcm = new Int16Array(length);
  let at = 0;
  for (const c of v.chunks) for (const s of c) pcm[at++] = Math.max(-32768, Math.min(32767, Math.round(s * 32767)));
  send(pcm.buffer);
}

function cancel(quiet) {
  if (v.state === 'idle') return;
  if (v.native) nativeApp()?.cancelVoice();
  close();
  if (!quiet) navigator.vibrate?.(4);
}

async function send(buffer) {
  try {
    const r = await ctx.post('voice', buffer);
    close();
    const kind = { ok: 'ok', warning: 'warning', error: 'error' }[r.status] ?? 'info';
    if (kind === 'warning') navigator.vibrate?.([20, 40, 20]);
    // The PC's own message already quotes what it heard («abre las notas» → Abrir notas).
    ctx.toast(kind, r.message || ctx.t('pwa.voice.nothing'), '', kind === 'ok' ? 3000 : 4500);
  } catch (e) {
    close();
    if (!e?.unpaired) ctx.toast('error', ctx.t('pwa.offline'), '', 3000);
  }
}

function open() {
  const sheet = $('voiceSheet');
  sheet.classList.remove('out', 'thinking');
  sheet.hidden = false;
  $('voiceText').textContent = ctx.t('pwa.voice.listening');
  $('voiceRing').style.setProperty('--level', '0');
  $('voiceButton').classList.add('on');
}

function close() {
  clearInterval(v.timer);
  release();
  v.state = 'idle';
  v.chunks = [];
  $('voiceButton').classList.remove('on');
  const sheet = $('voiceSheet');
  if (sheet.hidden) return;
  sheet.classList.add('out');
  sheet.addEventListener('animationend', () => { if (v.state === 'idle') sheet.hidden = true; }, { once: true });
}

function release() {
  v.node?.disconnect();
  v.node = null;
  v.audio?.close().catch(() => {});
  v.audio = null;
  v.stream?.getTracks().forEach(track => track.stop());
  v.stream = null;
}

/** To 16 kHz, averaging (keeps the fraction left over between chunks). */
function resample(input, rate) {
  if (rate === RATE) return Float32Array.from(input);
  const step = rate / RATE;
  const out = [];
  let pos = v.carry;
  while (pos + step <= input.length) {
    let sum = 0, n = 0;
    for (let i = Math.max(0, Math.floor(pos)); i < Math.floor(pos + step); i++) { sum += input[i]; n++; }
    out.push(n ? sum / n : 0);
    pos += step;
  }
  v.carry = pos - input.length;
  return Float32Array.from(out);
}
