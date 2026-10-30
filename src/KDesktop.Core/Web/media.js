// Multimedia: what plays on the PC (the programs that tell Windows what they play: cover, title, progress and their
// own buttons, one program or another with chips) and the volume of each program, like the volume mixer.

let ctx;                     // { api, t, svg, toast, online }
const media = {
  list: [],                  // the programs with something to play (api/media)
  chosen: '',                // appId picked with the chips ('' = the first: the one playing)
  received: 0,               // when the list arrived (the position goes on from there while playing)
  active: false,
  poll: 0,
  tick: 0,
  seeking: false,
  busy: false,
};

const el = (tag, className, text) => {
  const e = document.createElement(tag);
  if (className) e.className = className;
  if (text !== undefined) e.textContent = text;
  return e;
};
const $ = id => document.getElementById(id);
const buzz = ms => navigator.vibrate?.(ms);

/** 75 → «1:15», 3725 → «1:02:05». */
function time(seconds) {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60, r = String(s % 60).padStart(2, '0');
  return h ? `${h}:${String(m).padStart(2, '0')}:${r}` : `${m}:${r}`;
}

// ── what plays ─────────────────────────────────────────────────────

export function setupMedia(deps) {
  ctx = deps;
  $('nowPrev').addEventListener('click', () => control('previous'));
  $('nowNext').addEventListener('click', () => control('next'));
  $('nowPlay').addEventListener('click', () => control('toggle'));
  const seek = $('nowSeek');
  seek.addEventListener('input', () => {
    media.seeking = true;
    const s = current();
    if (s?.length) $('nowAt').textContent = time(seek.value / 1000 * s.length);
    fill(seek);
  });
  seek.addEventListener('change', async () => {
    const s = current();
    media.seeking = false;
    if (!s?.canSeek) return;
    buzz(6);
    await send({ app: s.appId, position: seek.value / 1000 * s.length });
  });
  document.addEventListener('visibilitychange', () => { if (media.active && document.visibilityState === 'visible') loadMedia(); });
}

/** The Multimedia tab opens or closes. */
export function mediaActive(on) {
  media.active = on;
  clearInterval(media.poll);
  clearInterval(media.tick);
  media.poll = media.tick = 0;
  if (!on) return;
  loadMedia();
  // The event stream says when something changes; this only covers a stream that dropped.
  media.poll = setInterval(() => { if (document.visibilityState === 'visible') loadMedia(); }, 10000);
  media.tick = setInterval(drawProgress, 500);
}

/** K-Desktop says that something changed (a track, a pause, a program that starts playing). */
export function onMediaEvent() {
  if (media.active && document.visibilityState === 'visible') loadMedia();
}

async function loadMedia() {
  if (media.busy) return;
  try {
    render(await ctx.api('media'));
  } catch {
    // The volume card says when the PC does not answer.
  }
}

async function send(body) {
  media.busy = true;
  try {
    const answer = await ctx.api('media', { method: 'POST', body });
    if (!answer.done) ctx.toast('warning', current()?.app ?? '', ctx.t('pwa.media.refused'), 2500);
    render(answer.state);
  } catch {
    ctx.toast('error', ctx.t('pwa.offline'), '', 2500);
  } finally {
    media.busy = false;
  }
}

function control(op) {
  const s = current();
  if (!s) return;
  buzz(8);
  // Straight away on the button: the player confirms in a moment.
  if (op === 'toggle') setPlaying(s.state !== 'playing');
  send({ app: s.appId, op });
}

const current = () => media.list.find(s => s.appId === media.chosen) ?? media.list[0];

function render(state) {
  media.list = state?.sessions ?? [];
  media.received = performance.now();
  const s = current();
  if (s) media.chosen = s.appId;
  $('nowCard').hidden = !s;
  // Without a program that says what it plays, the media keys of the volume card do it.
  $('mediaKeys').hidden = !!s;
  if (!s) return;

  const chips = $('nowChips');
  chips.hidden = media.list.length < 2;
  chips.replaceChildren(...(media.list.length < 2 ? [] : media.list.map(x => {
    const c = el('button', 'chip' + (x.appId === s.appId ? ' active' : ''), x.app);
    if (x.state === 'playing') c.prepend(el('i', 'chip-playing'));
    c.addEventListener('click', () => {
      buzz(5);
      media.chosen = x.appId;
      render(state);
    });
    return c;
  })));

  $('nowTitle').textContent = s.title ?? ctx.t('pwa.media.unknown');
  $('nowArtist').textContent = [s.artist, s.album].filter(Boolean).join(' · ');
  $('nowArtist').hidden = !s.artist && !s.album;
  $('nowApp').textContent = s.app;
  drawArt(s);
  setPlaying(s.state === 'playing');
  $('nowPrev').disabled = !s.canPrevious;
  $('nowNext').disabled = !s.canNext;
  $('nowProgress').hidden = !s.length;
  $('nowSeek').disabled = !s.canSeek;
  drawProgress();
}

function setPlaying(playing) {
  $('nowPlay').innerHTML = ctx.svg(playing ? 'pause' : 'play');
  $('nowCard').classList.toggle('playing', playing);
}

/** The cover, by its key: the same track keeps the image it has (no flicker). */
function drawArt(s) {
  const box = $('nowArt');
  const key = s.art ? s.appId + '|' + s.art : '';
  if (box.dataset.key === key) return;
  box.dataset.key = key;
  box.classList.remove('has-art');
  box.querySelector('img')?.remove();
  if (!key) return;
  const img = new Image();
  img.alt = '';
  img.decoding = 'async';
  img.onload = () => { if (box.dataset.key === key) box.classList.add('has-art'); };
  img.onerror = () => img.remove();
  img.src = `api/media/art?app=${encodeURIComponent(s.appId)}&k=${s.art}`;
  box.append(img);
}

function drawProgress() {
  const s = current();
  if (!s?.length || media.seeking) return;
  let at = s.position ?? 0;
  if (s.state === 'playing') at += (performance.now() - media.received) / 1000;
  at = Math.min(at, s.length);
  const seek = $('nowSeek');
  seek.value = Math.round(at / s.length * 1000);
  fill(seek);
  $('nowAt').textContent = time(at);
  $('nowLength').textContent = time(s.length);
}

const fill = input => input.style.setProperty('--fill', (input.value / input.max * 100) + '%');

// ── the volume of each program ─────────────────────────────────────

const pending = new Map();   // process → the volume still to send (while the finger moves)

/** From the audio state (api/audio): one row per program with sound open; the one being moved is left alone. */
export function renderApps(apps) {
  const list = $('appList');
  $('appsBlock').hidden = !apps.length;
  const same = list.children.length === apps.length && apps.every((a, i) => list.children[i].dataset.process === a.process);
  if (!same) list.replaceChildren(...apps.map(appRow));
  apps.forEach((a, i) => update(list.children[i], a));
}

function appRow(a) {
  const row = el('div', 'row app-volume');
  row.dataset.process = a.process;
  const icon = el('span', 'row-icon app-icon');
  if (a.icon) {
    const img = new Image();
    img.alt = '';
    img.src = 'api/icon/' + a.icon + '.png';
    icon.append(img);
  } else icon.innerHTML = ctx.svg('speakers');
  const main = el('span', 'app-volume-main');
  const name = el('span', 'app-volume-name');
  name.append(el('b', '', a.name), el('span', 'app-volume-value'));
  const slider = el('input');
  slider.type = 'range';
  slider.min = 0;
  slider.max = 100;
  slider.step = 1;
  slider.setAttribute('aria-label', a.name);
  main.append(name, slider);
  const mute = el('button', 'icon-btn small app-mute');
  mute.setAttribute('aria-label', a.name);
  row.append(icon, main, mute);

  slider.addEventListener('input', () => {
    row.dataset.dragging = '1';
    showLevel(row, Number(slider.value), false);
    queue(a.process, Number(slider.value));
  });
  slider.addEventListener('change', () => { delete row.dataset.dragging; });
  mute.addEventListener('click', () => {
    buzz(8);
    const muted = !row.classList.contains('muted');
    row.classList.toggle('muted', muted);
    mute.innerHTML = ctx.svg(muted ? 'speakerOff' : 'speaker');
    ctx.changeAudio({ app: a.process, muted });
  });
  return row;
}

function update(row, a) {
  row.classList.toggle('active', a.active);
  if (!row.dataset.dragging && !pending.has(a.process)) showLevel(row, a.level, a.muted);
}

function showLevel(row, level, muted) {
  const slider = row.querySelector('input');
  slider.value = level;
  fill(slider);
  row.querySelector('.app-volume-value').textContent = level + ' %';
  row.classList.toggle('muted', muted);
  const mute = row.querySelector('.app-mute');
  mute.innerHTML = ctx.svg(muted ? 'speakerOff' : 'speaker');
  mute.title = ctx.t(muted ? 'pwa.audio.unmute' : 'pwa.audio.mute');
}

/** While the finger moves: at most every 100 ms, and always the last value. */
function queue(process, level) {
  const waiting = pending.has(process);
  pending.set(process, level);
  if (waiting) return;
  const flush = async () => {
    const value = pending.get(process);
    await ctx.changeAudio({ app: process, volume: value });
    if (pending.get(process) === value) pending.delete(process);
    else setTimeout(flush, 100);
  };
  setTimeout(flush, 0);
}
