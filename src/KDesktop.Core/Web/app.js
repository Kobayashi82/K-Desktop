// K-Desktop web app: pairing, QuickMenus, all commands with search, touchpad, audio, settings.
// Every URL is relative, so the app works at the root of a domain or under a path behind the proxy.
import { searchKey, tokens, score } from './fuzzy.js';
import { setupPad, padActive, padTexts } from './pad.js';
import { setupVoice, voiceAvailable, startVoiceFor } from './voice.js';
import { setupChat, chatEnabled, chatVoice, chatActive, chatResync, onAssistants, onChatLine, chatAsked, chatTexts } from './chat.js';

const $ = id => document.getElementById(id);

const store = {
  get(key, fallback = null) {
    try {
      const v = localStorage.getItem('kd.' + key);
      return v === null ? fallback : JSON.parse(v);
    } catch { return fallback; }
  },
  set(key, value) {
    try {
      if (value === null || value === undefined) localStorage.removeItem('kd.' + key);
      else localStorage.setItem('kd.' + key, JSON.stringify(value));
    } catch { /* private mode */ }
  },
};

// ── icons ──────────────────────────────────────────────────────────

const ICONS = {
  refresh: '<path d="M20 11a8 8 0 1 0-2.34 5.66"/><path d="M20 4v7h-7"/>',
  eye: '<path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/>',
  eyeOff: '<path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/><path d="M4 4l16 16"/>',
  back: '<path d="M15 18l-6-6 6-6"/>',
  search: '<circle cx="11" cy="11" r="7"/><path d="M20 20l-3.5-3.5"/>',
  menus: '<rect x="3.5" y="3.5" width="7" height="7" rx="2"/><rect x="13.5" y="3.5" width="7" height="7" rx="2"/><rect x="3.5" y="13.5" width="7" height="7" rx="2"/><rect x="13.5" y="13.5" width="7" height="7" rx="2"/>',
  all: '<path d="M13 2.5L4.5 13.5H12l-1 8 8.5-11H12z"/>',
  settings: '<path d="M4 7h9M17 7h3M4 17h3M11 17h9"/><circle cx="15" cy="7" r="2"/><circle cx="9" cy="17" r="2"/>',
  ok: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
  mic: '<rect x="9" y="3" width="6" height="11" rx="3"/><path d="M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21"/>',
  error: '<path d="M7 7l10 10M17 7L7 17"/>',
  warning: '<path d="M12 7v6M12 17v.01"/>',
  running: '<circle cx="12" cy="12" r="7"/><path d="M12 8.5V12l2.5 1.5"/>',
  info: '<path d="M12 11v6M12 7v.01"/>',
  chevron: '<path d="M9 6l6 6-6 6"/>',
  speaker: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M15.5 9a4 4 0 0 1 0 6M18 6.5a7.5 7.5 0 0 1 0 11"/>',
  speakerOff: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M16 9.5l5 5M21 9.5l-5 5"/>',
  minus: '<path d="M6 12h12"/>',
  plus: '<path d="M12 6v12M6 12h12"/>',
  previous: '<path d="M7 6v12"/><path d="M18 6l-8 6 8 6z"/>',
  next: '<path d="M17 6v12"/><path d="M6 6l8 6-8 6z"/>',
  playPause: '<path d="M4.5 6l7 6-7 6z"/><path d="M15 6v12M19.5 6v12"/>',
  arrowUp: '<path d="M12 19V5M6 11l6-6 6 6"/>',
  arrowDown: '<path d="M12 5v14M6 13l6 6 6-6"/>',
  arrowLeft: '<path d="M19 12H5M11 6l-6 6 6 6"/>',
  arrowRight: '<path d="M5 12h14M13 6l6 6-6 6"/>',
  space: '<path d="M4 10.5v3.5h16v-3.5"/>',
  speakers: '<rect x="6" y="3" width="12" height="18" rx="2.5"/><circle cx="12" cy="14" r="3"/><path d="M12 7.5h.01"/>',
  headphones: '<path d="M4 15v-3a8 8 0 0 1 16 0v3"/><rect x="3.5" y="14" width="4" height="6" rx="1.5"/><rect x="16.5" y="14" width="4" height="6" rx="1.5"/>',
  headset: '<path d="M4 14v-2a8 8 0 0 1 16 0v2"/><rect x="3.5" y="13" width="4" height="6" rx="1.5"/><rect x="16.5" y="13" width="4" height="6" rx="1.5"/><path d="M18.5 19c0 1.5-1.5 2.5-4.5 2.5"/>',
  monitor: '<rect x="3" y="4.5" width="18" height="12" rx="2"/><path d="M9 20h6M12 16.5V20"/>',
  digital: '<rect x="3.5" y="7" width="17" height="10" rx="2"/><path d="M7.5 12h9"/>',
  touchpad: '<rect x="3" y="4" width="18" height="16" rx="3"/><path d="M3 15h18M12 15v5"/>',
  keyboard: '<rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6.5 10h.01M10 10h.01M13.5 10h.01M17 10h.01M7.5 14h9"/>',
  zoom: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="M20 20l-4.8-4.8M10.5 7.5v6M7.5 10.5h6"/>',
  keys: '<path d="M5 9h3M5 15h3M10 9h9M10 15h9"/><rect x="2.5" y="5" width="19" height="14" rx="2.5"/>',
  phone: '<rect x="6.5" y="2.5" width="11" height="19" rx="2.5"/><path d="M10.5 18.5h3"/>',
  theme: '<circle cx="12" cy="12" r="8.5"/><path d="M12 3.5v17A8.5 8.5 0 0 0 12 3.5z" fill="currentColor"/>',
  language: '<circle cx="12" cy="12" r="8.5"/><path d="M3.5 12h17M12 3.5c2.5 2.5 3.5 5.5 3.5 8.5s-1 6-3.5 8.5c-2.5-2.5-3.5-5.5-3.5-8.5s1-6 3.5-8.5z"/>',
  pointer: '<path d="M6 3.5l12 7.5-5.5 1.5L10 18z"/>',
  scroll: '<rect x="7" y="3" width="10" height="18" rx="5"/><path d="M12 7v3"/>',
  natural: '<path d="M7 4v16M7 4L4 7M7 4l3 3M17 20V4M17 20l-3-3M17 20l3-3"/>',
  tap: '<circle cx="12" cy="9" r="3"/><path d="M12 15v6M6.5 5.5a8 8 0 0 1 11 0"/>',
  install: '<path d="M12 3.5v11M7.5 10l4.5 4.5 4.5-4.5"/><path d="M4.5 16v2.5a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2V16"/>',
  link: '<path d="M10 14a4 4 0 0 0 5.66 0l3-3a4 4 0 0 0-5.66-5.66l-1 1"/><path d="M14 10a4 4 0 0 0-5.66 0l-3 3a4 4 0 0 0 5.66 5.66l1-1"/>',
  server: '<rect x="3.5" y="4" width="17" height="7" rx="2"/><rect x="3.5" y="13" width="17" height="7" rx="2"/><path d="M7 7.5h.01M7 16.5h.01"/>',
  unlink: '<path d="M9.5 14.5l5-5M10 6.5l1.5-1.5a4 4 0 0 1 5.66 5.66L15.5 12M14 17.5L12.5 19a4 4 0 0 1-5.66-5.66L8.5 12"/><path d="M3.5 3.5l3 3M20.5 20.5l-3-3"/>',
  chat: '<path d="M20.5 11.5a8 7.5 0 0 1-11.6 6.7L4 19.5l1.4-4A7.3 7.3 0 0 1 4.5 11.5a8 7.5 0 0 1 16 0z"/><path d="M8.5 11.5h.01M12.5 11.5h.01M16.5 11.5h.01"/>',
  send: '<path d="M12 19V5M6 11l6-6 6 6"/>',
  stop: '<rect x="7" y="7" width="10" height="10" rx="2"/>',
};

const svg = name => `<svg class="i" viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] ?? ''}</svg>`;

function el(tag, className, text) {
  const e = document.createElement(tag);
  if (className) e.className = className;
  if (text !== undefined) e.textContent = text;
  return e;
}

// ── state ──────────────────────────────────────────────────────────

const state = {
  texts: store.get('texts', null),
  catalog: store.get('catalog', null),
  catalogJson: '',
  session: null,
  tab: store.get('tab', 'all'),
  menuId: store.get('menu', null),
  filter: store.get('filter', 'all'),       // commands tab: "all", "none" (in no QuickMenu) or a QuickMenu id
  showHidden: store.get('showHidden', false),
  path: [],          // ids of the open submenus
  lastLoad: 0,
};

class Unpaired extends Error {}

// ── texts ──────────────────────────────────────────────────────────

function t(key, ...args) {
  let s = state.texts?.strings?.[key] ?? key;
  args.forEach((a, i) => { s = s.replaceAll(`{${i}}`, a); });
  return s;
}

function applyTexts() {
  document.documentElement.lang = state.texts?.lang ?? 'es';
  for (const e of document.querySelectorAll('[data-i18n]')) e.textContent = t(e.dataset.i18n);
  for (const e of document.querySelectorAll('[data-i18n-placeholder]')) e.placeholder = t(e.dataset.i18nPlaceholder);
  for (const e of document.querySelectorAll('[data-i18n-title]')) {
    e.title = t(e.dataset.i18nTitle);
    e.setAttribute('aria-label', e.title);
  }
  fillLanguages();
  padTexts();
  chatTexts();
  setTitles();
}

async function loadTexts() {
  const lang = store.get('lang', '') || navigator.language.slice(0, 2);
  try {
    const r = await fetch(`api/i18n?lang=${encodeURIComponent(lang)}`, { cache: 'no-store' });
    if (!r.ok) return;
    state.texts = await r.json();
    store.set('texts', state.texts);
    applyTexts();
  } catch { /* offline: keep the cached texts */ }
}

// ── API ────────────────────────────────────────────────────────────

async function api(path, { method = 'GET', body } = {}) {
  const headers = { 'X-KD': '1' };
  const token = store.get('token');
  if (token) headers.Authorization = 'Bearer ' + token;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const r = await fetch('api/' + path, {
    method, headers, cache: 'no-store', credentials: 'same-origin',
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (r.status === 401) {
    showPair();
    throw new Unpaired();
  }
  const data = await r.json().catch(() => ({}));
  if (!r.ok) throw Object.assign(new Error(data.error || String(r.status)), { status: r.status, data });
  return data;
}

/** Sends raw bytes (a voice order's recording) and returns the JSON answer. */
async function postBinary(path, buffer) {
  const headers = { 'X-KD': '1', 'Content-Type': 'application/octet-stream' };
  const token = store.get('token');
  if (token) headers.Authorization = 'Bearer ' + token;
  const r = await fetch('api/' + path, { method: 'POST', headers, body: buffer, cache: 'no-store', credentials: 'same-origin' });
  if (r.status === 401) {
    showPair();
    throw Object.assign(new Unpaired(), { unpaired: true });
  }
  const data = await r.json().catch(() => ({}));
  if (!r.ok) throw Object.assign(new Error(data.error || String(r.status)), { status: r.status, data });
  setOnline(true);
  return data;
}

/** Header pill: is K-Desktop reachable right now? */
function setOnline(value) {
  $('connPill').dataset.state = value === null ? 'connecting' : value ? 'online' : 'offline';
}

// ── theme ──────────────────────────────────────────────────────────

let lightTheme = false;

function applyTheme(theme) {
  const root = document.documentElement;
  if (theme === 'light' || theme === 'dark') root.dataset.theme = theme;
  else delete root.dataset.theme;
  for (const b of $('themeSwitch').querySelectorAll('button')) b.classList.toggle('active', b.dataset.theme === (theme || 'system'));
  movePill($('themeSwitch').querySelector('.seg-pill'), $('themeSwitch').querySelector('button.active'));
  requestAnimationFrame(() => {
    const bg = getComputedStyle(root).getPropertyValue('--bg').trim();
    document.querySelector('meta[name="theme-color"]').content = bg || '#0e0e10';
  });
  // White glyph icons come in a dark-on-light version for the light theme.
  const light = theme === 'light' || (theme !== 'dark' && !matchMedia('(prefers-color-scheme: dark)').matches);
  if (light !== lightTheme) {
    lightTheme = light;
    if (!$('shell').hidden) {
      renderMenus();
      renderAll();
    }
  }
}

function movePill(pill, target) {
  if (!pill || !target || !target.offsetWidth) return;
  pill.style.width = target.offsetWidth + 'px';
  pill.style.transform = `translateX(${target.offsetLeft}px)`;
}

// ── tabs ───────────────────────────────────────────────────────────

const TABS = ['all', 'menus', 'pad', 'audio', 'assistant', 'settings'];

/** The assistant tab only while the module is on. */
function showAssistantTab(on) {
  const button = $('tabbar').querySelector('[data-tab="assistant"]');
  button.hidden = !on;
  $('tabbar').style.setProperty('--tabs', on ? 6 : 5);
  $('tabbar').classList.toggle('six', on);
  if (!on && state.tab === 'assistant') setTab('all');
  else requestAnimationFrame(() => movePill($('tabPill'), $('tabbar').querySelector('button.active')));
}

function setTitles() {
  const title = t('pwa.tab.' + state.tab);
  $('bigTitle').textContent = title;
}

function setTab(tab) {
  if (!TABS.includes(tab)) tab = 'all';
  const back = TABS.indexOf(tab) < TABS.indexOf(state.tab);
  state.tab = tab;
  store.set('tab', tab);
  for (const v of document.querySelectorAll('.view')) {
    const on = v.dataset.view === tab;
    v.classList.toggle('active', on);
    v.classList.toggle('back', on && back);
  }
  for (const b of $('tabbar').querySelectorAll('button')) b.classList.toggle('active', b.dataset.tab === tab);
  movePill($('tabPill'), $('tabbar').querySelector('button.active'));
  setTitles();
  window.scrollTo({ top: 0 });
  updateHeader();
  if (tab === 'audio') startAudio();
  else stopAudio();
  padActive(tab === 'pad', state.session?.input !== false);
  chatActive(tab === 'assistant');
}

/** The big title scrolls away; then the bar turns to glass. */
function updateHeader() {
  const title = $('bigTitle');
  const scrolled = title.getBoundingClientRect().bottom < $('top').getBoundingClientRect().bottom + 4;
  $('top').classList.toggle('scrolled', scrolled);
}

// ── audio ──────────────────────────────────────────────────────────

/** The cross of arrows and space: a tap sends the key once; holding an arrow repeats it (seek, player volume). */
function wireKeys() {
  for (const b of document.querySelectorAll('[data-key]')) {
    let delay = 0, repeat = 0;
    const send = () => api('audio', { method: 'POST', body: { key: b.dataset.key } }).catch(() => {});
    const stop = () => {
      clearTimeout(delay);
      clearInterval(repeat);
      delay = repeat = 0;
      b.classList.remove('held');
    };
    b.addEventListener('pointerdown', e => {
      e.preventDefault();
      stop();
      b.setPointerCapture?.(e.pointerId);
      b.classList.add('held');
      navigator.vibrate?.(8);
      send();
      if (b.dataset.key !== 'space') delay = setTimeout(() => { repeat = setInterval(send, 140); }, 420);
    });
    for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) b.addEventListener(type, stop);
    b.addEventListener('contextmenu', e => e.preventDefault());
  }
}

const audio = { state: null, timer: 0, dragging: false, busy: false, sendTimer: 0, pending: null };

function startAudio() {
  stopAudio();
  loadAudio();
  // Follow changes made on the PC while the tab is open.
  audio.timer = setInterval(() => { if (document.visibilityState === 'visible') loadAudio(); }, 2500);
}

function stopAudio() {
  clearInterval(audio.timer);
  audio.timer = 0;
}

async function loadAudio() {
  if (audio.busy || audio.dragging || audio.pending) return;
  try {
    renderAudio(await api('audio'));
    setOnline(true);
  } catch (e) {
    if (!(e instanceof Unpaired)) {
      stopAudio();
      setOnline(false);
    }
  }
}

async function changeAudio(change) {
  audio.busy = true;
  try {
    const state = await api('audio', { method: 'POST', body: change });
    if (!audio.dragging && !audio.pending) renderAudio(state);
    return state;
  } catch (e) {
    if (!(e instanceof Unpaired)) toast('error', t('pwa.offline'), '', 2500);
    return null;
  } finally {
    audio.busy = false;
  }
}

function renderAudio(state) {
  audio.state = state;
  const card = $('volumeCard');
  const has = state.volume !== null && state.volume !== undefined;
  card.classList.toggle('muted', !!state.muted);
  $('muteButton').innerHTML = svg(state.muted ? 'speakerOff' : 'speaker');
  $('muteButton').setAttribute('aria-label', t(state.muted ? 'pwa.audio.unmute' : 'pwa.audio.mute'));
  if (!audio.dragging) setVolumeUi(has ? state.volume : 0);
  if (!has) $('volumeValue').textContent = '–';

  const list = $('deviceList');
  const devices = state.devices ?? [];
  if (!devices.length) {
    list.replaceChildren(el('p', 'empty', t('pwa.audio.none')));
    return;
  }
  // Same devices: only move the selection (keeps the animation smooth).
  const same = list.children.length === devices.length && devices.every((d, i) => list.children[i].dataset?.id === d.id);
  if (same) {
    devices.forEach((d, i) => {
      list.children[i].classList.toggle('current', d.default);
      list.children[i].disabled = !d.active;
    });
    return;
  }
  list.replaceChildren(...devices.map((d, i) => {
    const b = el('button', 'device' + (d.default ? ' current' : ''));
    b.dataset.id = d.id;
    b.disabled = !d.active;
    b.style.setProperty('--n', i);
    const ic = el('span', 'device-icon');
    ic.innerHTML = svg({ headphones: 'headphones', headset: 'headset', monitor: 'monitor', digital: 'digital' }[d.kind] ?? 'speakers');
    const text = el('span', 'device-text');
    text.append(el('b', '', d.name), el('span', '', d.active ? d.adapter : t('pwa.audio.notConnected')));
    const check = el('span', 'device-check');
    check.innerHTML = svg('ok');
    b.append(ic, text, check);
    b.addEventListener('pointerdown', e => ripple(b, e));
    b.addEventListener('click', async () => {
      if (b.classList.contains('current')) return;
      navigator.vibrate?.(8);
      for (const other of list.children) other.classList.toggle('current', other === b);
      const state = await changeAudio({ device: d.id });
      if (state?.devices?.find(x => x.id === d.id)?.default) toast('ok', t('pwa.audio.switched'), d.name, 1800);
    });
    return b;
  }));
}

/** The dial: a 270° arc from bottom-left to bottom-right; 0 % at the start, 100 % at the end. */
const DIAL = { cx: 110, cy: 110, r: 92, start: 135, sweep: 270 };

function setVolumeUi(value) {
  const slider = $('volumeSlider');
  slider.value = value;
  slider.style.setProperty('--fill', value + '%');
  $('volumeValue').textContent = value;
  $('dialFill').style.strokeDasharray = `${value * DIAL.sweep / 360} 100`;
  const a = (DIAL.start + DIAL.sweep * value / 100) * Math.PI / 180;
  $('dialKnob').setAttribute('cx', (DIAL.cx + DIAL.r * Math.cos(a)).toFixed(2));
  $('dialKnob').setAttribute('cy', (DIAL.cy + DIAL.r * Math.sin(a)).toFixed(2));
  $('dial').setAttribute('aria-valuenow', value);
}

/** Volume from a touch on the dial (its angle around the centre). */
function dialValue(e) {
  const r = $('dial').getBoundingClientRect();
  const x = e.clientX - (r.left + r.width / 2), y = e.clientY - (r.top + r.height / 2);
  let deg = Math.atan2(y, x) * 180 / Math.PI - DIAL.start;
  while (deg < 0) deg += 360;
  if (deg > DIAL.sweep) deg = deg - DIAL.sweep < (360 - DIAL.sweep) / 2 ? DIAL.sweep : 0; // the gap at the bottom
  return Math.round(deg / DIAL.sweep * 100);
}

/** Sends the volume while dragging, at most every 90 ms, and always the last value. */
function sendVolume(value) {
  audio.pending = value;
  if (audio.sendTimer) return;
  const flush = async () => {
    const v = audio.pending;
    audio.pending = null;
    if (v === null) {
      audio.sendTimer = 0;
      return;
    }
    await changeAudio({ volume: v });
    audio.sendTimer = setTimeout(flush, 90);
  };
  audio.sendTimer = setTimeout(flush, 0);
}

function stepVolume(delta) {
  const value = Math.max(0, Math.min(100, Number($('volumeSlider').value) + delta));
  navigator.vibrate?.(4);
  setVolumeUi(value);
  sendVolume(value);
}

// ── tiles ──────────────────────────────────────────────────────────

/** The icon for the current theme (a light-background version exists for white glyph icons). */
const iconOf = node => (lightTheme && node.iconLight) || node.icon;

const tints = new Map();

/** A soft glow in the icon's own colour: its average colour, brightened if very dark. */
function tintFrom(img, target) {
  const key = img.src;
  if (tints.has(key)) {
    if (tints.get(key)) target.style.setProperty('--tint', tints.get(key));
    return;
  }
  try {
    const c = document.createElement('canvas');
    c.width = c.height = 12;
    const g = c.getContext('2d', { willReadFrequently: true });
    g.drawImage(img, 0, 0, 12, 12);
    const d = g.getImageData(0, 0, 12, 12).data;
    let r = 0, gr = 0, b = 0, w = 0;
    for (let i = 0; i < d.length; i += 4) {
      const a = d[i + 3] / 255;
      const max = Math.max(d[i], d[i + 1], d[i + 2]), min = Math.min(d[i], d[i + 1], d[i + 2]);
      const weight = a * (0.25 + (max - min) / 255); // colourful pixels count more than greys
      r += d[i] * weight; gr += d[i + 1] * weight; b += d[i + 2] * weight; w += weight;
    }
    if (w < 1) { tints.set(key, null); return; }
    r /= w; gr /= w; b /= w;
    const lift = Math.max(0, 110 - Math.max(r, gr, b));
    const value = `${Math.round(r + lift)}, ${Math.round(gr + lift)}, ${Math.round(b + lift)}`;
    tints.set(key, value);
    target.style.setProperty('--tint', value);
  } catch { tints.set(key, null); }
}

function iconImage(hash, onLoad) {
  const img = new Image();
  img.src = `api/icon/${hash}.png`;
  img.alt = '';
  img.decoding = 'async';
  img.onerror = () => img.remove();
  if (onLoad) img.onload = () => onLoad(img);
  return img;
}

/** Icons of the first commands inside a submenu, for its folder preview. */
function previewIcons(node, max = 4) {
  const found = [];
  const walk = nodes => {
    for (const n of nodes ?? []) {
      if (found.length >= max) return;
      if (n.kind === 'command' && iconOf(n)) found.push(iconOf(n));
      else if (n.children) walk(n.children);
    }
  };
  walk(node.children);
  return found;
}

function countItems(node) {
  let n = 0;
  for (const c of node.children ?? []) n += c.kind === 'title' ? countItems(c) : c.kind === 'separator' ? 0 : 1;
  return n;
}

/** A command (or submenu) tile. */
function tile({ label, icon, commandId, folder, node, onOpen, index }) {
  const b = el('button', 'tile' + (folder ? ' folder' : ''));
  b.style.setProperty('--n', Math.min(index ?? 0, 24));
  const iconBox = el('span', 'tile-icon');
  if (folder && node) {
    const icons = previewIcons(node);
    if (icons.length <= 1 && icon) {
      iconBox.classList.add('single');
      iconBox.append(iconImage(icon, img => tintFrom(img, b)));
    } else {
      icons.forEach((h, i) => iconBox.append(iconImage(h, i === 0 ? img => tintFrom(img, b) : null)));
    }
    const count = countItems(node);
    if (count) b.append(el('span', 'tile-count', String(count)));
  } else if (icon) {
    iconBox.append(iconImage(icon, img => tintFrom(img, b)));
  }
  b.append(iconBox, el('span', 'tile-label', label ?? ''));

  b.addEventListener('pointerdown', e => ripple(b, e));
  b.addEventListener('click', e => {
    if (e.target.closest('.tile-more')) return;
    if (commandId) run(commandId, b, label);
    else onOpen?.();
  });

  // A command that also has children: the corner button opens them.
  if (commandId && onOpen) {
    const more = el('span', 'tile-more');
    more.innerHTML = svg('chevron');
    more.setAttribute('role', 'button');
    more.setAttribute('aria-label', t('pwa.openSub'));
    more.addEventListener('click', e => {
      e.stopPropagation();
      onOpen();
    });
    b.append(more);
  }
  return b;
}

function skeletonGrid(count = 12) {
  const grid = el('div', 'grid');
  for (let i = 0; i < count; i++) {
    const s = el('div', 'tile skeleton');
    s.append(el('span', 'tile-icon'), el('span', 'tile-label'));
    grid.append(s);
  }
  return grid;
}

function ripple(target, e) {
  const r = target.getBoundingClientRect();
  const size = Math.max(r.width, r.height) * 2;
  const dot = el('span', 'ripple');
  dot.style.width = dot.style.height = size + 'px';
  dot.style.left = (e.clientX - r.left - size / 2) + 'px';
  dot.style.top = (e.clientY - r.top - size / 2) + 'px';
  target.append(dot);
  dot.addEventListener('animationend', () => dot.remove());
}

function badge(target, kind) {
  target.querySelector('.badge')?.remove();
  const b = el('span', 'badge ' + kind);
  b.innerHTML = svg(kind === 'running' ? 'running' : kind === 'warning' ? 'warning' : kind === 'error' ? 'error' : 'ok');
  target.append(b);
  b.addEventListener('animationend', e => { if (e.animationName === 'badge-out') b.remove(); });
  if (kind === 'ok') {
    const burst = el('span', 'burst');
    target.append(burst);
    burst.addEventListener('animationend', () => burst.remove());
    target.classList.remove('done');
    void target.offsetWidth;
    target.classList.add('done');
  }
}

// ── running commands ───────────────────────────────────────────────

async function run(id, target, label) {
  if (target.classList.contains('busy')) return;
  navigator.vibrate?.(8);
  target.classList.add('busy');
  let result;
  try {
    result = await api('run', { method: 'POST', body: { id } });
    setOnline(true);
  } catch (e) {
    if (e instanceof Unpaired) return;
    result = { status: e.status === 404 ? 'unavailable' : 'offline', messages: [] };
    if (!e.status) setOnline(false);
  } finally {
    target.classList.remove('busy');
  }

  const status = result.status;
  const kind = status === 'unavailable' || status === 'offline' ? 'error' : status;
  badge(target, kind);
  if (kind === 'error') {
    target.classList.remove('shake');
    void target.offsetWidth;
    target.classList.add('shake');
    navigator.vibrate?.([25, 40, 25]);
  }
  const messages = (result.messages ?? []).join('\n');
  const statusText = t({ ok: 'pwa.run.ok', error: 'pwa.run.error', warning: 'pwa.run.warning', running: 'pwa.run.running',
                         offline: 'pwa.run.offline', unavailable: 'pwa.run.unavailable' }[status] ?? 'pwa.run.ok');
  toast(kind, label, messages || statusText, kind === 'error' ? 5000 : messages ? 3500 : 1800);
  if (status === 'unavailable') loadCatalog();
}

// ── toasts ─────────────────────────────────────────────────────────

/** A short message at the bottom. With <onTap>, tapping it does that (and it says so by its look). */
function toast(kind, title, detail, ms = 2500, onTap = null) {
  const box = $('toasts');
  while (box.children.length >= 3) box.firstElementChild.remove();
  const tEl = el('div', 'toast ' + kind);
  const ic = el('span', 'toast-icon');
  ic.innerHTML = svg(kind === 'info' ? 'info' : kind);
  const text = el('div', 'toast-text');
  text.append(el('b', '', title));
  if (detail) text.append(el('span', '', detail));
  tEl.append(ic, text);
  box.append(tEl);
  const close = () => {
    if (tEl.classList.contains('out')) return;
    tEl.classList.add('out');
    tEl.addEventListener('animationend', () => tEl.remove());
  };
  if (onTap) tEl.classList.add('tappable');
  tEl.addEventListener('click', () => {
    close();
    onTap?.();
  });
  setTimeout(close, ms);
}

// ── QuickMenus ─────────────────────────────────────────────────────

function currentMenu() {
  const menus = state.catalog?.menus ?? [];
  return menus.find(m => m.id === state.menuId) ?? menus[0] ?? null;
}

/** The nodes of the open level and the trail of submenus that lead to it. */
function currentLevel(menu) {
  let nodes = menu.items;
  const trail = [];
  for (const id of state.path) {
    const node = findNode(nodes, id);
    if (!node?.children) break;
    trail.push(node);
    nodes = node.children;
  }
  state.path = trail.map(n => n.id);
  return { nodes, trail };
}

/** Finds a node among the nodes of a level, including the children of its titles (they show inline). */
function findNode(nodes, id) {
  for (const n of nodes) {
    if (n.id === id) return n;
    if (n.kind === 'title' && n.children) {
      const inner = findNode(n.children, id);
      if (inner) return inner;
    }
  }
  return null;
}

function renderMenus(direction) {
  const menus = state.catalog?.menus ?? [];
  const chips = $('menuChips');
  chips.hidden = menus.length < 2;
  const menu = currentMenu();
  chips.replaceChildren(...menus.map(m => {
    const c = el('button', 'chip' + (m === menu ? ' active' : ''), m.name);
    c.addEventListener('click', () => {
      if (m === menu) return;
      navigator.vibrate?.(4);
      state.menuId = m.id;
      store.set('menu', m.id);
      state.path = [];
      renderMenus();
    });
    return c;
  }));

  const stack = $('menuStack');
  if (!state.catalog) {
    $('crumbs').hidden = true;
    stack.replaceChildren(skeletonGrid(8));
    return;
  }
  if (!menu) {
    $('crumbs').hidden = true;
    stack.replaceChildren(el('p', 'empty', t('pwa.noMenus')));
    return;
  }
  const { nodes, trail } = currentLevel(menu);
  renderCrumbs(menu, trail);

  const level = el('div', 'level' + (direction === 'back' ? ' back' : ''));
  renderNodes(nodes, level, { i: 0 });
  stack.replaceChildren(level);
}

/** "Menu › Submenu › Submenu": each part goes back to that level. */
function renderCrumbs(menu, trail) {
  $('crumbs').hidden = trail.length === 0;
  if (!trail.length) return;
  const path = $('crumbPath');
  const parts = [{ label: menu.name, depth: 0 }, ...trail.map((n, i) => ({ label: n.label ?? '', depth: i + 1 }))];
  path.replaceChildren(...parts.flatMap((p, i) => {
    const b = el('button', '', p.label);
    b.addEventListener('click', () => goToDepth(p.depth));
    if (i === 0) return [b];
    const sep = el('span', 'sep');
    sep.innerHTML = svg('chevron');
    return [sep, b];
  }));
  requestAnimationFrame(() => { path.scrollLeft = path.scrollWidth; });
}

function goToDepth(depth) {
  if (depth >= state.path.length) return;
  const steps = state.path.length - depth;
  state.path.length = depth;
  renderMenus('back');
  // Keep the history entries of the submenus in step (back button / gesture): one popstate for the jump.
  ignorePops++;
  history.go(-steps);
}

let ignorePops = 0;

function renderNodes(nodes, container, counter) {
  let grid = null;
  for (const node of nodes) {
    // Separators make sense in a desktop list, not in a grid of tiles: only titles start sections here.
    if (node.kind === 'separator') continue;
    if (node.kind === 'title') {
      container.append(el('h3', 'section-title', node.label ?? ''));
      grid = null;
      if (node.children) {
        const body = el('div', 'section-body');
        renderNodes(node.children, body, counter);
        container.append(body);
      }
      continue;
    }
    if (!grid) {
      grid = el('div', 'grid');
      container.append(grid);
    }
    const open = node.children ? () => openSubmenu(node) : null;
    grid.append(tile({
      label: node.label, icon: iconOf(node), commandId: node.commandId,
      folder: node.kind === 'submenu', node, onOpen: open, index: counter.i++,
    }));
  }
}

function openSubmenu(node) {
  navigator.vibrate?.(5);
  state.path.push(node.id);
  history.pushState({ kd: state.path.length }, '');
  renderMenus('forward');
  window.scrollTo({ top: 0 });
}

function closeSubmenu() {
  if (ignorePops > 0) {
    ignorePops--;
    return;
  }
  if (!state.path.length) return;
  state.path.pop();
  renderMenus('back');
}

// ── all commands ───────────────────────────────────────────────────

const keys = new Map();
const keyOf = c => {
  let k = keys.get(c.id);
  if (!k || k.text !== c.name) keys.set(c.id, k = searchKey(c.name));
  return k;
};

let shownAll = [];

/** First letter for the sections (accents folded, digits and symbols together). */
function letterOf(name) {
  const ch = (name ?? '').trim().charAt(0).normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase();
  return /[A-ZÑ]/.test(ch) ? ch : '#';
}

/** Ids of the commands in a QuickMenu (one, or all of them). */
function menuCommands(menuId) {
  const ids = new Set();
  const walk = nodes => {
    for (const n of nodes ?? []) {
      if (n.commandId) ids.add(n.commandId);
      walk(n.children);
    }
  };
  for (const m of state.catalog?.menus ?? []) if (!menuId || m.id === menuId) walk(m.items);
  return ids;
}

/** The filter chips: all, not in a QuickMenu, each QuickMenu. */
function renderFilters() {
  const menus = state.catalog?.menus ?? [];
  if (!['all', 'none'].includes(state.filter) && !menus.some(m => m.id === state.filter)) state.filter = 'all';
  const options = [['all', t('pwa.filter.all')], ['none', t('pwa.filter.none')], ...menus.map(m => [m.id, m.name])];
  const chips = options.map(([key, label]) => {
    const c = el('button', 'chip' + (key === state.filter ? ' active' : ''), label);
    c.addEventListener('click', () => {
      if (key === state.filter) return;
      navigator.vibrate?.(4);
      state.filter = key;
      store.set('filter', key);
      renderAll();
    });
    return c;
  });
  // "Hidden" is a switch in the search bar, always in sight (also with no hidden commands: then it shows the same).
  const h = $('hiddenToggle');
  h.hidden = !state.catalog;
  h.classList.toggle('active', state.showHidden);
  h.setAttribute('aria-pressed', String(state.showHidden));
  h.innerHTML = svg(state.showHidden ? 'eye' : 'eyeOff');
  const box = $('cmdFilters');
  box.hidden = !state.catalog;
  box.replaceChildren(...chips);
}

/** The commands that pass the filters. */
function filteredCommands() {
  let list = state.catalog?.commands ?? [];
  if (!state.showHidden) list = list.filter(c => !c.hidden);
  if (state.filter === 'none') {
    const inMenus = menuCommands(null);
    list = list.filter(c => !inMenus.has(c.id));
  } else if (state.filter !== 'all') {
    const inMenu = menuCommands(state.filter);
    list = list.filter(c => inMenu.has(c.id));
  }
  return list;
}

function renderAll() {
  const grid = $('allGrid');
  const q = $('searchBox').value.trim();
  renderFilters();
  if (!state.catalog) {
    grid.replaceChildren(skeletonGrid());
    $('topStrip').hidden = true;
    return;
  }
  const commands = filteredCommands();
  let list = commands;
  if (q) {
    const toks = tokens(q);
    list = commands.map(c => ({ c, s: score(toks, keyOf(c)) }))
      .filter(x => x.s > 0)
      .sort((a, b) => b.s - a.s || b.c.runs - a.c.runs)
      .map(x => x.c);
  }
  shownAll = list;
  $('allCount').textContent = list.length ? String(list.length) : '';

  // Most used: only without a search, and only when there is a history.
  const top = q ? [] : [...commands].filter(c => c.runs > 0).sort((a, b) => b.runs - a.runs).slice(0, 10);
  $('topStrip').hidden = top.length < 3;
  $('topList').replaceChildren(...top.map((c, i) => tile({ label: c.name, icon: iconOf(c), commandId: c.id, index: i })));

  if (q) {
    const g = el('div', 'grid');
    g.append(...list.map((c, i) => tile({ label: c.name, icon: iconOf(c), commandId: c.id, index: Math.min(i, 8) })));
    grid.replaceChildren(g);
  } else {
    // A section per letter: quicker to find something among many commands.
    const groups = new Map();
    for (const c of list) {
      const letter = letterOf(c.name);
      if (!groups.has(letter)) groups.set(letter, []);
      groups.get(letter).push(c);
    }
    let n = 0;
    grid.replaceChildren(...[...groups].map(([letter, items]) => {
      const section = el('section', 'letter');
      const g = el('div', 'grid');
      g.append(...items.map(c => tile({ label: c.name, icon: iconOf(c), commandId: c.id, index: n++ })));
      section.append(el('h3', 'section-title', letter), g);
      return section;
    }));
  }
  const empty = $('allEmpty');
  empty.hidden = list.length > 0;
  empty.textContent = q ? t('pwa.noResults', q)
    : state.catalog.commands.length && (state.filter !== 'all' || !state.showHidden) ? t('pwa.filter.empty') : t('pwa.noCommands');
}

// ── catalog / session ──────────────────────────────────────────────

async function loadCatalog(manual = false) {
  try {
    const catalog = await api('catalog');
    state.lastLoad = Date.now();
    setOnline(true);
    const json = JSON.stringify(catalog);
    if (json !== state.catalogJson) {
      state.catalogJson = json;
      state.catalog = catalog;
      store.set('catalog', catalog);
      $('machine').textContent = catalog.machine;
      renderMenus();
      renderAll();
    }
    if (manual) toast('ok', t('pwa.refreshed'), '', 1500);
  } catch (e) {
    if (!(e instanceof Unpaired)) {
      setOnline(false);
      toast('error', t('pwa.offline'), '', 3000);
    }
  }
}

function showShell() {
  $('pair').hidden = true;
  if (!$('shell').hidden) return;
  $('shell').hidden = false;
  if (state.catalog) {
    state.catalogJson = JSON.stringify(state.catalog);
    $('machine').textContent = state.catalog.machine;
  }
  renderMenus();
  renderAll();
  setTab(state.tab);
  applyTheme(store.get('theme', 'system'));
}

function fillSession(s = state.session) {
  if (!s) return;
  $('deviceName').textContent = s.device?.name ?? '';
  $('connectedTo').textContent = t('pwa.settings.connected', s.machine);
  $('version').textContent = s.version ? 'K-Desktop ' + s.version : s.app;
  // Inside the Android app: its own version too.
  const appVersion = window.KDesktopApp?.version?.();
  $('appVersion').hidden = !appVersion;
  if (appVersion) $('appVersion').textContent = t('pwa.settings.appVersion', appVersion);
  if (!$('machine').textContent) $('machine').textContent = s.machine;
  // This same PC needs no pairing, so there is nothing to undo.
  $('unpairButton').hidden = !!s.local;
  $('padSettings').hidden = s.input === false;
  $('keysPad').hidden = s.input === false;
  voiceAvailable(!!s.voice);
  chatVoice(!!s.voice);
  chatEnabled(!!s.assistants);
  if (state.tab === 'pad') padActive(true, s.input !== false);
  fillAddresses(s);
  offerAppUpdate(s);
}

// ── Android app update ─────────────────────────────────────────────

let appUpdateOffered = 0;

/** K-Desktop carries the Android app: inside an older one, offer the new one (a toast on opening, always a row in Settings). */
function offerAppUpdate(s) {
  const app = window.KDesktopApp;
  const newer = app?.versionCode && s.android && s.android.code > app.versionCode();
  $('appUpdateButton').hidden = !newer;
  if (!newer) return;
  $('appUpdateText').textContent = t('pwa.appUpdate.version', app.version(), s.android.version);
  // Every time the app opens while it is pending (once ever was too easy to miss).
  if (appUpdateOffered !== s.android.code) {
    appUpdateOffered = s.android.code;
    toast('info', t('pwa.appUpdate.available'), t('pwa.appUpdate.where'), 8000, updateApp);
  }
}

function updateApp() {
  $('appUpdateButton').disabled = true;
  toast('info', t('pwa.appUpdate.downloading'), '', 4000);
  window.KDesktopApp.updateApp(new URL('api/android', location.href).href);
}

/** The Android app tells how the update went: installing (the system asks now), cancelled or error. */
window.kdAppUpdate = (result, detail) => {
  $('appUpdateButton').disabled = false;
  if (result === 'error') toast('error', t('pwa.appUpdate.failed'), detail, 5000);
};

// ── addresses ──────────────────────────────────────────────────────

/** This app's own address (scheme://host/path/), to tell it apart from the others. */
const here = () => new URL('.', location.href).href.toLowerCase();

/**
 * The PC can be reached at several addresses (its IP at home, the proxy's domain outside). The Android app keeps
 * them all and uses the one that answers; a browser keeps its pairing per address, so it gets a button to pair this
 * same device at the other ones in one tap.
 */
function fillAddresses(s) {
  const addresses = s.addresses ?? [];
  const app = window.KDesktopApp;
  if (app?.setAddresses) {
    if (addresses.length) app.setAddresses(JSON.stringify({ machine: s.machine, addresses }));
    return;
  }
  const others = s.local ? [] : addresses.filter(a => a.toLowerCase() !== here());
  const rows = $('addressRows');
  rows.hidden = others.length === 0;
  rows.replaceChildren(...others.map(address => {
    const row = document.createElement('button');
    row.className = 'row row-button';
    row.innerHTML = `<span class="row-icon">${svg('link')}</span><span class="row-label"><span></span><small class="row-sub"></small></span><span class="row-chevron">${svg('chevron')}</span>`;
    row.querySelector('.row-label > span').textContent = t('pwa.settings.otherAddress');
    row.querySelector('.row-sub').textContent = address;
    row.addEventListener('click', () => openAt(address, row));
    return row;
  }));
}

/** Pairs this device at another address too (a one-time link code) and goes there. */
async function openAt(address, row) {
  row.disabled = true;
  try {
    const { code } = await api('link', { method: 'POST' });
    location.href = address + '#link=' + code;
  } catch (e) {
    if (!(e instanceof Unpaired)) toast('error', t('pwa.offline'), '', 3000);
    row.disabled = false;
  }
}

/** Arrived from another address of the same PC with a link code: paired at once, as the same device. */
async function linkHere(code) {
  try {
    const r = await fetch('api/pair', {
      method: 'POST', cache: 'no-store',
      headers: { 'X-KD': '1', 'Content-Type': 'application/json' },
      body: JSON.stringify({ code, name: store.get('deviceName', '') || deviceName() }),
    });
    if (!r.ok) throw new Error(String(r.status));
    const data = await r.json();
    keepToken(data.token);
    toast('ok', t('pwa.pair.done'), data.device?.name ?? '', 2000);
    await connect();
  } catch {
    showPair();
    $('pairError').textContent = t('pwa.settings.linkFailed');
  }
}

/** Whether this browser still gets in (its token or cookie is good), without showing the pairing screen. */
async function stillPaired() {
  const headers = { 'X-KD': '1' };
  const token = store.get('token');
  if (token) headers.Authorization = 'Bearer ' + token;
  try {
    const r = await fetch('api/session', { headers, cache: 'no-store', credentials: 'same-origin' });
    return r.ok;
  } catch {
    return false;
  }
}

/** The token, also in the Android app: it moves between the PC's addresses with it. */
function keepToken(token) {
  store.set('token', token);
  window.KDesktopApp?.setToken?.(token ?? '');
}

// ── live events ────────────────────────────────────────────────────

let events = null;

/** K-Desktop says when the commands or menus change, so the open app follows the PC at once. */
function startEvents() {
  if (events || !window.EventSource || document.visibilityState !== 'visible') return;
  events = new EventSource('api/events');
  events.addEventListener('open', () => {
    setOnline(true);
    // The stream came back: maybe K-Desktop restarted, perhaps as a newer version.
    loadSession();
    chatResync();
  });
  events.addEventListener('catalog', () => loadCatalog());
  events.addEventListener('session', () => loadSession());
  events.addEventListener('assistants', () => onAssistants());
  events.addEventListener('chat', e => {
    try { onChatLine(JSON.parse(e.data)); } catch { /* not for us */ }
  });
  events.addEventListener('error', () => {
    // A refused stream (unpaired, too many) is not retried by the browser.
    if (events?.readyState === EventSource.CLOSED) events = null;
    else setOnline(null);
  });
}

function stopEvents() {
  events?.close();
  events = null;
}

// ── install ────────────────────────────────────────────────────────

let installPrompt = null;

function setupInstall() {
  if ('serviceWorker' in navigator && window.isSecureContext)
    navigator.serviceWorker.register('sw.js').catch(() => { /* not over plain HTTP */ });

  const standalone = matchMedia('(display-mode: standalone)').matches || navigator.standalone;
  window.addEventListener('beforeinstallprompt', e => {
    e.preventDefault();
    if (window.KDesktopApp) return;
    installPrompt = e;
    $('installButton').hidden = false;
  });
  window.addEventListener('appinstalled', () => {
    installPrompt = null;
    $('installButton').hidden = true;
  });
  $('installButton').addEventListener('click', async () => {
    if (!installPrompt) return;
    installPrompt.prompt();
    await installPrompt.userChoice.catch(() => null);
    installPrompt = null;
    $('installButton').hidden = true;
  });
  // Safari on iPhone/iPad has no install prompt: say how.
  $('iosHint').hidden = standalone || !/iPhone|iPad|iPod/.test(navigator.userAgent) || !window.isSecureContext;

  // Inside the Android app (window.KDesktopApp): it is installed already; offer to point it at another K-Desktop.
  const app = window.KDesktopApp;
  if (app) {
    $('installButton').hidden = true;
    $('iosHint').hidden = true;
    $('serverButton').hidden = false;
    $('serverText').textContent = app.getServer?.() ?? '';
    $('serverButton').addEventListener('click', () => app.changeServer());
  }
}

/** The session again (what the PC allows changed). */
/**
 * K-Desktop was updated while this page was open (an app back from the background keeps its page, and the PC
 * restarting does not reload it): load the new web app. Only live answers count, compared within this page.
 */
let liveVersion = null;
function reloadIfUpdated(s) {
  if (!s?.version) return false;
  if (liveVersion && liveVersion !== s.version) {
    location.reload();
    return true;
  }
  liveVersion = s.version;
  return false;
}

async function loadSession() {
  try {
    state.session = await api('session');
    if (reloadIfUpdated(state.session)) return;
    store.set('session', state.session);
    fillSession();
  } catch { /* offline or unpaired: handled elsewhere */ }
}

/** Session and catalog again. <manual>: tapped on the PC name, so say when it is done. */
async function connect(manual = false) {
  setOnline(null);
  try {
    state.session = await api('session');
    if (reloadIfUpdated(state.session)) return;
    store.set('session', state.session);
    if (state.session.cookie) state.catalogJson = '';
    fillSession();
    showShell();
    startEvents();
    await loadCatalog(manual);
  } catch (e) {
    if (e instanceof Unpaired) return;
    // K-Desktop not reachable: the cached commands are still shown.
    setOnline(false);
    if (state.catalog) showShell();
    toast('error', t('pwa.offline'), '', 3500);
  }
}

// ── pairing ────────────────────────────────────────────────────────

function deviceName() {
  const ua = navigator.userAgent;
  let os = /iPhone/.test(ua) ? 'iPhone' : /iPad/.test(ua) ? 'iPad' : /Android/.test(ua) ? 'Android'
    : /Windows/.test(ua) ? 'Windows' : /Mac OS/.test(ua) ? 'Mac' : /Linux/.test(ua) ? 'Linux' : 'Web';
  const model = /Android[^;)]*;\s*([^;)]+?)(?:\s+Build\/|\))/.exec(ua)?.[1];
  if (model && model.length > 2) os = model;
  const browser = /KDesktopApp/.test(ua) ? 'App' : /EdgA?\//.test(ua) ? 'Edge' : /OPR\//.test(ua) ? 'Opera' : /SamsungBrowser/.test(ua) ? 'Samsung Internet'
    : /Firefox|FxiOS/.test(ua) ? 'Firefox' : /Chrome|CriOS/.test(ua) ? 'Chrome' : /Safari/.test(ua) ? 'Safari' : '';
  return browser ? `${os} · ${browser}` : os;
}

function formatCode(value) {
  const raw = (value ?? '').toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 8);
  return raw.length > 4 ? raw.slice(0, 4) + '-' + raw.slice(4) : raw;
}

function showPair(code, auto = false) {
  stopEvents();
  $('shell').hidden = true;
  $('pair').hidden = false;
  if (code) $('pairCode').value = formatCode(code);
  if (!$('pairName').value) $('pairName').value = store.get('deviceName', '') || deviceName();
  $('pairError').textContent = '';
  if (auto && $('pairCode').value.length === 9) pair();
  else $('pairCode').focus({ preventScroll: true });
}

async function pair(e) {
  e?.preventDefault();
  const button = $('pairButton');
  if (button.disabled) return;
  button.disabled = true;
  button.textContent = t('pwa.pair.pairing');
  $('pairError').textContent = '';
  const name = $('pairName').value.trim();
  store.set('deviceName', name);
  try {
    const r = await fetch('api/pair', {
      method: 'POST', cache: 'no-store',
      headers: { 'X-KD': '1', 'Content-Type': 'application/json' },
      body: JSON.stringify({ code: $('pairCode').value, name }),
    });
    if (!r.ok) throw new Error(String(r.status));
    const data = await r.json();
    keepToken(data.token);
    navigator.vibrate?.(20);
    toast('ok', t('pwa.pair.done'), data.device?.name ?? '', 2000);
    await connect();
  } catch (err) {
    $('pairError').textContent = err.message === '403' ? t('pwa.pair.bad') : t('pwa.offline');
    const card = document.querySelector('.pair-card');
    card.classList.remove('shake');
    void card.offsetWidth;
    card.classList.add('shake');
    navigator.vibrate?.([25, 40, 25]);
  } finally {
    button.disabled = false;
    button.textContent = t('pwa.pair.button');
  }
}

// ── settings ───────────────────────────────────────────────────────

function fillLanguages() {
  const select = $('langSelect');
  const langs = state.texts?.languages ?? [];
  const chosen = store.get('lang', '');
  select.replaceChildren(
    new Option(t('pwa.lang.system'), ''),
    ...langs.map(l => new Option(l.name, l.code)));
  select.value = chosen;
}

async function unpair() {
  if (!confirm(t('pwa.settings.unpairConfirm'))) return;
  try { await api('logout', { method: 'POST' }); } catch { /* already gone */ }
  keepToken(null);
  store.set('catalog', null);
  store.set('session', null);
  state.catalog = null;
  state.catalogJson = '';
  showPair();
}

// ── wiring ─────────────────────────────────────────────────────────

function wireDial() {
  const dial = $('dial');
  const set = e => {
    const v = dialValue(e);
    if (v === Number($('volumeSlider').value)) return;
    if (v % 10 === 0) navigator.vibrate?.(3);
    setVolumeUi(v);
    sendVolume(v);
  };
  dial.addEventListener('pointerdown', e => {
    // Only the ring: the centre has the mute button.
    const r = dial.getBoundingClientRect();
    const dist = Math.hypot(e.clientX - (r.left + r.width / 2), e.clientY - (r.top + r.height / 2));
    if (dist < r.width * 0.3) return;
    dial.setPointerCapture(e.pointerId);
    audio.dragging = true;
    dial.classList.add('dragging');
    set(e);
  });
  dial.addEventListener('pointermove', e => { if (dial.hasPointerCapture(e.pointerId)) set(e); });
  const end = e => {
    if (!dial.hasPointerCapture?.(e.pointerId)) return;
    dial.releasePointerCapture(e.pointerId);
    audio.dragging = false;
    dial.classList.remove('dragging');
  };
  dial.addEventListener('pointerup', end);
  dial.addEventListener('pointercancel', end);
  // The mouse wheel over the dial: 2 % per notch (touchpads send many small steps: they add up).
  let wheel = 0;
  $('volumeCard').querySelector('.dial').addEventListener('wheel', e => {
    e.preventDefault();
    wheel += e.deltaMode === 1 ? e.deltaY * 33 : e.deltaY;
    const notches = Math.trunc(wheel / 100);
    if (!notches) return;
    wheel -= notches * 100;
    stepVolume(-2 * notches);
  }, { passive: false });
  dial.addEventListener('keydown', e => {
    const step = { ArrowUp: 2, ArrowRight: 2, ArrowDown: -2, ArrowLeft: -2, PageUp: 10, PageDown: -10 }[e.key];
    if (!step) return;
    e.preventDefault();
    stepVolume(step);
  });
}

function wire() {
  for (const e of document.querySelectorAll('[data-icon]')) e.innerHTML = svg(e.dataset.icon);
  setupPad({ t, store, svg, toast, online: setOnline, onUnpaired: () => showPair() });
  setupVoice({ t, toast, post: postBinary, updateApp });
  setupChat({
    t, toast, api, store,
    lang: () => state.texts?.lang ?? navigator.language,
    showTab: showAssistantTab,
    openTab: () => setTab('assistant'),
    talk: id => {
      chatAsked(id);
      startVoiceFor(id);
    },
  });

  for (const b of $('tabbar').querySelectorAll('button'))
    b.addEventListener('click', () => {
      navigator.vibrate?.(4);
      if (b.dataset.tab === state.tab && state.tab === 'menus' && state.path.length) {
        goToDepth(0);
        return;
      }
      if (b.dataset.tab === state.tab) {
        window.scrollTo({ top: 0, behavior: 'smooth' });
        return;
      }
      setTab(b.dataset.tab);
    });

  $('connPill').addEventListener('click', () => {
    navigator.vibrate?.(6);
    connect(true);
  });
  $('hiddenToggle').addEventListener('click', () => {
    navigator.vibrate?.(4);
    state.showHidden = !state.showHidden;
    store.set('showHidden', state.showHidden);
    renderAll();
  });
  $('homeButton').addEventListener('click', () => {
    navigator.vibrate?.(4);
    if (state.tab === 'all') window.scrollTo({ top: 0, behavior: 'smooth' });
    else setTab('all');
  });
  $('reloadButton').addEventListener('click', () => loadCatalog(true));
  $('appUpdateButton').addEventListener('click', updateApp);
  $('backButton').addEventListener('click', () => history.back());
  window.addEventListener('popstate', () => closeSubmenu());
  window.addEventListener('scroll', updateHeader, { passive: true });

  const search = $('searchBox');
  search.addEventListener('input', renderAll);
  search.addEventListener('keydown', e => {
    if (e.key !== 'Enter' || !shownAll.length) return;
    e.preventDefault();
    $('allGrid').querySelector('.tile')?.click();
  });

  for (const b of $('themeSwitch').querySelectorAll('button'))
    b.addEventListener('click', () => {
      store.set('theme', b.dataset.theme);
      applyTheme(b.dataset.theme);
    });
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => applyTheme(store.get('theme', 'system')));

  $('langSelect').addEventListener('change', async e => {
    store.set('lang', e.target.value);
    await loadTexts();
    fillSession();
    renderMenus();
    renderAll();
    requestAnimationFrame(() => {
      movePill($('tabPill'), $('tabbar').querySelector('button.active'));
      applyTheme(store.get('theme', 'system'));
    });
  });
  $('unpairButton').addEventListener('click', unpair);

  wireDial();
  $('volDown').addEventListener('click', () => stepVolume(-2));
  $('volUp').addEventListener('click', () => stepVolume(2));
  $('muteButton').addEventListener('click', () => {
    navigator.vibrate?.(8);
    const muted = !audio.state?.muted;
    $('volumeCard').classList.toggle('muted', muted);
    $('muteButton').innerHTML = svg(muted ? 'speakerOff' : 'speaker');
    if (audio.state) audio.state.muted = muted;
    changeAudio({ muted });
  });
  for (const b of document.querySelectorAll('[data-media]'))
    b.addEventListener('click', () => {
      navigator.vibrate?.(8);
      changeAudio({ media: b.dataset.media });
    });
  wireKeys();

  $('pairForm').addEventListener('submit', pair);
  $('pairCode').addEventListener('input', e => {
    const input = e.target;
    input.value = formatCode(input.value);
  });

  window.addEventListener('resize', () => {
    movePill($('tabPill'), $('tabbar').querySelector('button.active'));
    movePill($('themeSwitch').querySelector('.seg-pill'), $('themeSwitch').querySelector('button.active'));
  });

  // Back to the app after a while: pick up changes made on the PC.
  // The live stream is closed in the background (saves battery); on return, catch up.
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState !== 'visible') {
      stopEvents();
      return;
    }
    if (!state.session) return;
    startEvents();
    if (Date.now() - state.lastLoad > 5000) {
      loadSession();
      loadCatalog();
    }
  });
}

async function start() {
  applyTheme(store.get('theme', 'system'));
  wire();
  setupInstall();
  if (state.texts) applyTexts();

  // Shortcuts of the installed app: #tab=audio…
  const tab = /[#&]tab=(\w+)/.exec(location.hash)?.[1];
  if (tab && TABS.includes(tab)) {
    state.tab = tab;
    history.replaceState(null, '', location.pathname + location.search);
  }
  const textsLoaded = loadTexts();
  if (!state.texts) await textsLoaded;

  // The Android app keeps the token itself: the same one works at every address of the PC.
  const appToken = window.KDesktopApp?.getToken?.();
  if (appToken && appToken !== store.get('token')) store.set('token', appToken);
  else if (!appToken && store.get('token')) window.KDesktopApp?.setToken?.(store.get('token'));

  // Opened from the QR: #pair=CODE. From another address of this PC: #link=CODE.
  const code = /[#&]pair=([A-Za-z0-9-]+)/.exec(location.hash)?.[1];
  const link = /[#&]link=([a-f0-9]+)/.exec(location.hash)?.[1];
  if (code || link) history.replaceState(null, '', location.pathname + location.search);
  if (code) {
    showPair(code, true);
    return;
  }
  // Opened from K-Desktop on this PC with a link code too: a browser that is already paired does not need it.
  if (link && !(await stillPaired())) {
    await linkHere(link);
    return;
  }
  // Last known state first (instant, and it works with the PC off), then the live one.
  fillSession(store.get('session'));
  if (state.catalog && (store.get('token') || store.get('session')?.local)) showShell();
  await connect();
}

start();
