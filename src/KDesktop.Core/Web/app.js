// K-Desktop web app: pairing, QuickMenus, all commands with search, touchpad, audio, settings.
// Every URL is relative, so the app works at the root of a domain or under a path behind the proxy.
import { searchKey, tokens, score } from './fuzzy.js';
import { setupPad, padActive, padTexts, padSettings } from './pad.js';
import { setupVoice, voiceAvailable, startVoiceFor } from './voice.js';
import { setupChat, chatEnabled, chatVoice, chatActive, chatResync, onAssistants, onChatLine, onDid, chatAsked, chatTexts, chatTalk } from './chat.js';
import { setupInbox, fetchInbox } from './inbox.js';
import { setupMedia, mediaActive, onMediaEvent, renderApps } from './media.js';
import { setupSpeech, speakWhere, speechSettings, onSpeech, onSpeechEnd, stopSpeech } from './speech.js';

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

/** What this phone keeps for this PC: in the Android app, kept by the app (the same at every address of the PC). */
const pcStore = {
  get(key, fallback) {
    const app = window.KDesktopApp;
    const raw = app?.getPref ? app.getPref(key) : localStorage.getItem('kd.pc.' + key);
    try { return raw ? JSON.parse(raw) : fallback; } catch { return fallback; }
  },
  set(key, value) {
    const raw = value === null || value === undefined ? '' : JSON.stringify(value);
    const app = window.KDesktopApp;
    if (app?.setPref) app.setPref(key, raw);
    else if (raw) localStorage.setItem('kd.pc.' + key, raw);
    else localStorage.removeItem('kd.pc.' + key);
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
  play: '<path d="M8 5.5l11 6.5-11 6.5z"/>',
  pause: '<path d="M8.5 5.5v13M15.5 5.5v13"/>',
  music: '<path d="M9 17.5V6l10-2v11.5"/><circle cx="6.5" cy="17.5" r="2.5"/><circle cx="16.5" cy="15.5" r="2.5"/>',
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
  speed: '<path d="M4.5 17a8 8 0 1 1 15 0"/><path d="M12 13l4-4.5"/><circle cx="12" cy="13" r="1.3"/>',
  fingers: '<path d="M8 11V5.5a1.5 1.5 0 0 1 3 0V11M11 10V4a1.5 1.5 0 0 1 3 0v6M14 10.5V6a1.5 1.5 0 0 1 3 0v7.5a6.5 6.5 0 0 1-6.5 6.5h-.6a5 5 0 0 1-4-2l-2.6-3.5a1.5 1.5 0 0 1 2.4-1.8L8 14"/>',
  install: '<path d="M12 3.5v11M7.5 10l4.5 4.5 4.5-4.5"/><path d="M4.5 16v2.5a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2V16"/>',
  link: '<path d="M10 14a4 4 0 0 0 5.66 0l3-3a4 4 0 0 0-5.66-5.66l-1 1"/><path d="M14 10a4 4 0 0 0-5.66 0l-3 3a4 4 0 0 0 5.66 5.66l1-1"/>',
  server: '<rect x="3.5" y="4" width="17" height="7" rx="2"/><rect x="3.5" y="13" width="17" height="7" rx="2"/><path d="M7 7.5h.01M7 16.5h.01"/>',
  unlink: '<path d="M9.5 14.5l5-5M10 6.5l1.5-1.5a4 4 0 0 1 5.66 5.66L15.5 12M14 17.5L12.5 19a4 4 0 0 1-5.66-5.66L8.5 12"/><path d="M3.5 3.5l3 3M20.5 20.5l-3-3"/>',
  inbox: '<rect x="3" y="5" width="18" height="14" rx="2.5"/><path d="M3.5 7l8.5 6 8.5-6"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2.5"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>',
  chat: '<path d="M20.5 11.5a8 7.5 0 0 1-11.6 6.7L4 19.5l1.4-4A7.3 7.3 0 0 1 4.5 11.5a8 7.5 0 0 1 16 0z"/><path d="M8.5 11.5h.01M12.5 11.5h.01M16.5 11.5h.01"/>',
  send: '<path d="M12 19V5M6 11l6-6 6 6"/>',
  stop: '<rect x="7" y="7" width="10" height="10" rx="2"/>',
  chevronDown: '<path d="M6 9l6 6 6-6"/>',
  lock: '<rect x="5" y="10.5" width="14" height="10" rx="2.5"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/>',
  sleep: '<path d="M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z"/>',
  hibernate: '<path d="M12 3v18M4.2 7.5l15.6 9M4.2 16.5l15.6-9M9.5 4.5L12 7l2.5-2.5M9.5 19.5L12 17l2.5 2.5"/>',
  restart: '<path d="M4 12a8 8 0 1 0 2.34-5.66"/><path d="M4 4v4.5h4.5"/>',
  power: '<path d="M12 3.5v8"/><path d="M7 6.5a7 7 0 1 0 10 0"/>',
  star: '<path d="M12 3.8l2.5 5.2 5.7.8-4.1 4 1 5.7-5.1-2.7-5.1 2.7 1-5.7-4.1-4 5.7-.8z"/>',
  starFill: '<path fill="currentColor" d="M12 3.8l2.5 5.2 5.7.8-4.1 4 1 5.7-5.1-2.7-5.1 2.7 1-5.7-4.1-4 5.7-.8z"/>',
  shield: '<path d="M12 3l7.5 3v5.5c0 4.5-3.2 8.2-7.5 9.5-4.3-1.3-7.5-5-7.5-9.5V6z"/><path d="M9 12l2 2 4-4"/>',
  broadcast: '<circle cx="12" cy="12" r="2"/><path d="M8.5 8.5a5 5 0 0 0 0 7M15.5 8.5a5 5 0 0 1 0 7M5.6 5.6a9 9 0 0 0 0 12.8M18.4 5.6a9 9 0 0 1 0 12.8"/>',
  folder: '<path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2.5h7a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z"/>',
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
  favorites: new Set(pcStore.get('favorites', [])),   // ids of the commands pinned at the top (this phone, this PC)
  confirm: new Set(pcStore.get('confirm', [])),       // ids of the commands that ask before running (this phone)
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
  const s = value === null ? 'connecting' : value ? 'online' : 'offline';
  $('connPill').dataset.state = s;
  $('connText').textContent = t('pwa.pc.' + s);
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

/** The tab bar (five at most: the assistant only with the module on). Settings is a page of its own, from the header. */
const TABS = ['all', 'menus', 'pad', 'audio', 'assistant'];

/** The assistant tab only while the module is on. */
function showAssistantTab(on) {
  const button = $('tabbar').querySelector('[data-tab="assistant"]');
  button.hidden = !on;
  $('tabbar').style.setProperty('--tabs', on ? 5 : 4);
  if (!on && state.tab === 'assistant') setTab('all');
  else requestAnimationFrame(() => movePill($('tabPill'), $('tabbar').querySelector('button.active')));
}

function setTitles() {
  $('bigTitle').textContent = t('pwa.tab.' + (state.view ?? state.tab));
}

/** Shows a view (a tab's, or settings) and starts or stops what only runs while it is on screen. */
function showView(view, back = false) {
  state.view = view;
  for (const v of document.querySelectorAll('.view')) {
    const on = v.dataset.view === view;
    v.classList.toggle('active', on);
    v.classList.toggle('back', on && back);
  }
  const settings = view === 'settings';
  $('titleBack').hidden = !settings;
  $('settingsButton').classList.toggle('active', settings);
  $('tabbar').classList.toggle('dimmed', settings);
  setTitles();
  window.scrollTo({ top: 0 });
  updateHeader();
  if (view === 'audio') startAudio();
  else stopAudio();
  mediaActive(view === 'audio');
  padActive(view === 'pad', state.session?.input !== false);
  chatActive(view === 'assistant');
}

function setTab(tab) {
  if (!TABS.includes(tab)) tab = 'all';
  // Leaving settings by a tab: its history entry goes too (the back button would reopen it).
  if (state.view === 'settings') {
    ignorePops++;
    history.back();
  }
  const back = TABS.indexOf(tab) < TABS.indexOf(state.tab);
  state.tab = tab;
  store.set('tab', tab);
  for (const b of $('tabbar').querySelectorAll('button')) b.classList.toggle('active', b.dataset.tab === tab);
  movePill($('tabPill'), $('tabbar').querySelector('button.active'));
  showView(tab, back);
}

/** Settings, from the header: a page above the tabs (back, the gesture or the title's arrow return to the tab). */
function openSettings() {
  if (state.view === 'settings') return;
  navigator.vibrate?.(4);
  history.pushState({ kdView: 'settings' }, '');
  showView('settings');
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
  renderApps(state.apps ?? []);

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
    if (commandId) tryRun(commandId, b, label);
    else onOpen?.();
  });
  if (commandId) onHold(b, () => { const c = commandById(commandId); if (c) openCommandSheet(c, b); });

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

/** Runs a command and shows how it went on <target> (its row or tile): a spinner, then ✓ or ✗. Returns the status. */
async function run(id, target, label) {
  if (target.classList.contains('busy')) return null;
  navigator.vibrate?.(8);
  target.classList.add('busy');
  let result;
  try {
    result = await api('run', { method: 'POST', body: { id } });
    setOnline(true);
  } catch (e) {
    if (e instanceof Unpaired) return null;
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
  // The row or tile already says it went well: a toast only for what it can't show (messages, errors, still running).
  const messages = (result.messages ?? []).join('\n');
  if (kind !== 'ok' || messages) {
    const statusText = t({ ok: 'pwa.run.ok', error: 'pwa.run.error', warning: 'pwa.run.warning', running: 'pwa.run.running',
                           offline: 'pwa.run.offline', unavailable: 'pwa.run.unavailable' }[status] ?? 'pwa.run.ok');
    toast(kind, label, messages || statusText, kind === 'error' ? 5000 : 3500);
  }
  if (status === 'unavailable') loadCatalog();
  return status;
}

/** Runs it, asking first when this phone was told to («Pedir confirmación»). The status, or null if not run. */
function tryRun(id, target, label) {
  if (!state.confirm.has(id)) return run(id, target, label);
  return new Promise(resolve => {
    let chosen = false;
    const close = openSheet(box => {
      box.append(el('h2', '', t('pwa.cmd.confirmTitle', label)));
      const buttons = el('div', 'sheet-buttons');
      const cancel = el('button', 'btn', t('pwa.cancel'));
      const ok = el('button', 'btn primary', t('pwa.cmd.run'));
      cancel.addEventListener('click', () => close());
      ok.addEventListener('click', () => {
        chosen = true;
        close();
        setTimeout(() => resolve(run(id, target, label)), 200);
      });
      buttons.append(cancel, ok);
      box.append(buttons);
    });
    sheets[sheets.length - 1].onClose = () => { if (!chosen) resolve(null); };
  });
}

/** «Llamada desde fuera»: fires the events waiting for that name; nobody waiting is an error. */
async function fireEvent(name, target) {
  if (target.classList.contains('busy')) return;
  navigator.vibrate?.(8);
  target.classList.add('busy');
  let kind = 'ok';
  try {
    await api('event', { method: 'POST', body: { name } });
    setOnline(true);
  } catch (e) {
    if (e instanceof Unpaired) return;
    kind = 'error';
    toast('error', name, e.status === 404 ? t('pwa.event.nobody') : t('pwa.run.offline'), 3500);
    if (!e.status) setOnline(false);
  } finally {
    target.classList.remove('busy');
  }
  badge(target, kind);
}

/** Holding <target> does <onHold> instead of its tap (the tap that follows is ignored). */
function onHold(target, onLong) {
  let timer = 0, x = 0, y = 0, down = false, last = 0;
  const cancel = () => { clearTimeout(timer); down = false; };
  const hold = () => {
    cancel();
    if (Date.now() - last < 900) return;
    last = Date.now();
    target.dataset.held = '1';
    navigator.vibrate?.(15);
    onLong();
  };
  target.addEventListener('pointerdown', e => {
    x = e.clientX;
    y = e.clientY;
    cancel();
    down = true;
    timer = setTimeout(hold, 480);
  });
  target.addEventListener('pointermove', e => { if (Math.hypot(e.clientX - x, e.clientY - y) > 10) cancel(); });
  for (const type of ['pointerup', 'pointercancel', 'pointerleave']) target.addEventListener(type, cancel);
  // Android's own long press (it may cancel the pointer first): the same hold, once. With a mouse, the right click.
  target.addEventListener('contextmenu', e => {
    e.preventDefault();
    hold();
  });
  target.addEventListener('click', e => {
    if (!target.dataset.held) return;
    delete target.dataset.held;
    e.stopImmediatePropagation();
    e.preventDefault();
  }, true);
}

const commandById = id => state.catalog?.commands?.find(c => c.id === id);

function savePcSets() {
  pcStore.set('favorites', [...state.favorites]);
  pcStore.set('confirm', [...state.confirm]);
  syncFavorites();
}

/** The favourites with their names, for the Android app's widget and tile (only when they change). */
function syncFavorites() {
  if (!window.KDesktopApp?.setPref || !state.catalog) return;
  const info = state.catalog.commands.filter(c => state.favorites.has(c.id)).slice(0, 8).map(c => ({ id: c.id, name: c.name }));
  const json = JSON.stringify(info);
  if (json === syncFavorites.last) return;
  syncFavorites.last = json;
  pcStore.set('favoriteInfo', info);
}

/** Holding a command: what it does, its hotkeys and where it works; favourite, ask first, run. */
function openCommandSheet(c, target) {
  const close = openSheet(box => {
    const head = el('div', 'sheet-head');
    const icon = el('span', 'sheet-icon cmd-sheet-icon');
    if (iconOf(c)) icon.append(iconImage(iconOf(c)));
    else icon.innerHTML = svg('all');
    const text = el('div', 'sheet-head-text');
    text.append(el('b', '', c.name));
    if (c.summary) text.append(el('span', '', c.summary));
    head.append(icon, text);
    box.append(head);
    if (c.hotkeys?.length || c.apps?.length) {
      const meta = el('div', 'cmd-sheet-meta');
      for (const h of c.hotkeys ?? []) meta.append(el('kbd', '', h));
      if (c.apps?.length) meta.append(el('span', 'cmd-apps', t('pwa.cmd.only', c.apps.join(', '))));
      box.append(meta);
    }
    const group = el('div', 'group');
    const toggle = (iconName, label, sub, set) => {
      const row = el('label', 'row');
      row.innerHTML = `<span class="row-icon">${svg(iconName)}</span><span class="row-label"><span></span><small class="row-sub"></small></span><input type="checkbox" class="switch">`;
      row.querySelector('.row-label > span').textContent = label;
      row.querySelector('.row-sub').textContent = sub;
      const input = row.querySelector('input');
      input.checked = set.has(c.id);
      input.addEventListener('change', () => {
        navigator.vibrate?.(6);
        if (input.checked) set.add(c.id);
        else set.delete(c.id);
        savePcSets();
        renderAll();
      });
      return row;
    };
    group.append(toggle('star', t('pwa.cmd.favorite'), t('pwa.cmd.favoriteHint'), state.favorites),
      toggle('shield', t('pwa.cmd.confirm'), t('pwa.cmd.confirmHint'), state.confirm));
    box.append(group);
    const buttons = el('div', 'sheet-buttons');
    const runButton = el('button', 'btn primary', t('pwa.cmd.run'));
    runButton.addEventListener('click', () => {
      close();
      // Already asked here: straight away.
      setTimeout(() => run(c.id, target, c.name), 200);
    });
    buttons.append(runButton);
    box.append(buttons);
  });
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

/** A level of a QuickMenu, drawn like the PC's: rows with their icons, separators, titles and submenus with their arrow. */
function renderNodes(nodes, container, counter) {
  const panel = el('div', 'menu-panel');
  const add = items => {
    for (const node of items) {
      if (node.kind === 'separator') {
        if (panel.lastElementChild && !panel.lastElementChild.classList.contains('menu-sep')) panel.append(el('div', 'menu-sep'));
        continue;
      }
      if (node.kind === 'title') {
        panel.append(el('div', 'menu-title', node.label ?? ''));
        if (node.children) add(node.children);
        continue;
      }
      panel.append(menuItem(node, counter.i++));
    }
  };
  add(nodes);
  container.append(panel, el('p', 'menu-hint', t('pwa.menu.hold')));
}

function menuItem(node, index) {
  const folder = node.kind === 'submenu' || !node.commandId;
  const b = el('button', 'menu-item' + (folder ? ' submenu' : ''));
  b.style.setProperty('--n', Math.min(index, 20));
  const ic = el('span', 'menu-icon');
  const icon = iconOf(node) ?? (folder ? previewIcons(node, 1)[0] : null);
  if (icon) ic.append(iconImage(icon, img => tintFrom(img, b)));
  else ic.innerHTML = svg(folder ? 'folder' : 'all');
  b.append(ic, el('span', 'menu-label', node.label ?? ''));
  if (folder) {
    const count = countItems(node);
    if (count) b.append(el('span', 'menu-count', String(count)));
    const chevron = el('span', 'menu-chevron');
    chevron.innerHTML = svg('chevron');
    b.append(chevron);
  } else if (node.children) {
    // A command with items of its own: the arrow opens them, the rest runs it.
    const more = el('span', 'menu-open');
    more.innerHTML = svg('chevron');
    more.setAttribute('role', 'button');
    more.setAttribute('aria-label', t('pwa.openSub'));
    more.addEventListener('click', e => {
      e.stopPropagation();
      openSubmenu(node);
    });
    b.append(more);
  }
  b.addEventListener('pointerdown', e => ripple(b, e));
  b.addEventListener('click', async e => {
    if (e.target.closest('.menu-open')) return;
    if (folder) {
      openSubmenu(node);
      return;
    }
    const status = await tryRun(node.commandId, b, node.label);
    // As on the PC, running something closes the menu: back to its first level (holding the row keeps it open).
    if (status === 'ok' && state.path.length) setTimeout(() => { if (state.tab === 'menus' && state.path.length) goToDepth(0); }, 650);
  });
  if (!folder) onHold(b, () => tryRun(node.commandId, b, node.label));
  return b;
}

function openSubmenu(node) {
  navigator.vibrate?.(5);
  state.path.push(node.id);
  history.pushState({ kd: state.path.length }, '');
  renderMenus('forward');
  window.scrollTo({ top: 0 });
}

function closeSubmenu() {
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
/** The filter chips: all, not in a QuickMenu, each QuickMenu, and the events that can be fired by name. */
function renderFilters() {
  const menus = state.catalog?.menus ?? [];
  const events = state.catalog?.events ?? [];
  const valid = ['all', 'none', ...(events.length ? ['events'] : []), ...menus.map(m => m.id)];
  if (!valid.includes(state.filter)) state.filter = 'all';
  const options = [['all', t('pwa.filter.all')], ...(events.length ? [['events', t('pwa.filter.events')]] : []),
    ['none', t('pwa.filter.none')], ...menus.map(m => [m.id, m.name])];
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
  h.hidden = !state.catalog || state.filter === 'events';
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

/** A command in the list: icon, name, what it does, its hotkey and where it works; the result shows in the row. */
function commandRow(c, index) {
  const b = el('button', 'cmd');
  b.style.setProperty('--n', Math.min(index, 24));
  const ic = el('span', 'cmd-icon');
  if (iconOf(c)) ic.append(iconImage(iconOf(c), img => tintFrom(img, b)));
  else ic.innerHTML = svg('all');
  const text = el('span', 'cmd-text');
  text.append(el('b', '', c.name));
  if (c.summary) text.append(el('span', 'cmd-sub', c.summary));
  if (c.hotkeys?.length || c.apps?.length) {
    const meta = el('span', 'cmd-meta');
    if (c.hotkeys?.length) {
      meta.append(el('kbd', '', c.hotkeys[0]));
      if (c.hotkeys.length > 1) meta.append(el('span', 'cmd-more', '+' + (c.hotkeys.length - 1)));
    }
    if (c.apps?.length) meta.append(el('span', 'cmd-apps', c.apps[0] + (c.apps.length > 1 ? ' +' + (c.apps.length - 1) : '')));
    text.append(meta);
  }
  const end = el('span', 'cmd-end');
  if (state.favorites.has(c.id)) end.innerHTML = svg('starFill');
  if (state.confirm.has(c.id)) end.insertAdjacentHTML('beforeend', svg('shield'));
  b.append(ic, text, end);
  b.addEventListener('pointerdown', e => ripple(b, e));
  b.addEventListener('click', () => tryRun(c.id, b, c.name));
  onHold(b, () => openCommandSheet(c, b));
  return b;
}

function eventRow(ev, index) {
  const b = el('button', 'cmd event');
  b.style.setProperty('--n', Math.min(index, 24));
  const ic = el('span', 'cmd-icon');
  if (ev.icon) ic.append(iconImage(ev.icon, img => tintFrom(img, b)));
  else ic.innerHTML = svg('broadcast');
  const text = el('span', 'cmd-text');
  text.append(el('b', '', ev.name), el('span', 'cmd-sub', t('pwa.event.sub')));
  b.append(ic, text, el('span', 'cmd-end'));
  b.addEventListener('pointerdown', e => ripple(b, e));
  b.addEventListener('click', () => fireEvent(ev.name, b));
  return b;
}

function skeletonList(count = 8) {
  const list = el('div', 'cmd-list');
  for (let i = 0; i < count; i++) {
    const row = el('div', 'cmd skeleton');
    row.append(el('span', 'cmd-icon'), el('span', 'cmd-text'));
    list.append(row);
  }
  return list;
}

function renderAll() {
  const grid = $('allGrid');
  const q = $('searchBox').value.trim();
  renderFilters();
  if (!state.catalog) {
    grid.replaceChildren(skeletonList());
    $('topStrip').hidden = true;
    $('favStrip').hidden = true;
    return;
  }
  if (state.filter === 'events') {
    renderEvents(q);
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

  syncFavorites();
  // Favourites, then the most used: only without a search.
  const favorites = q ? [] : commands.filter(c => state.favorites.has(c.id));
  $('favStrip').hidden = favorites.length === 0;
  $('favList').replaceChildren(...favorites.map((c, i) => tile({ label: c.name, icon: iconOf(c), commandId: c.id, index: i })));
  const top = q ? [] : [...commands].filter(c => c.runs > 0 && !state.favorites.has(c.id)).sort((a, b) => b.runs - a.runs).slice(0, 10);
  $('topStrip').hidden = top.length < 3;
  $('topList').replaceChildren(...top.map((c, i) => tile({ label: c.name, icon: iconOf(c), commandId: c.id, index: i })));

  if (q || list.length < LETTER_SECTIONS) {
    // A search, or few enough to see at a glance: one list.
    const rows = el('div', 'cmd-list');
    rows.append(...list.map((c, i) => commandRow(c, q ? Math.min(i, 8) : i)));
    // Under the favourites or the most used, a title says where the whole list starts.
    const strips = !$('favStrip').hidden || !$('topStrip').hidden;
    grid.replaceChildren(...(strips && list.length ? [el('h3', 'section-title', t('pwa.filter.all'))] : []), rows);
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
      const rows = el('div', 'cmd-list');
      rows.append(...items.map(c => commandRow(c, n++)));
      section.append(el('h3', 'section-title', letter), rows);
      return section;
    }));
  }
  const empty = $('allEmpty');
  empty.hidden = list.length > 0;
  empty.textContent = q ? t('pwa.noResults', q)
    : state.catalog.commands.length && (state.filter !== 'all' || !state.showHidden) ? t('pwa.filter.empty') : t('pwa.noCommands');
}

/** From how many commands the list goes in sections by letter. */
const LETTER_SECTIONS = 30;

/** The «Eventos» filter: the events that can be fired by name. */
function renderEvents(q) {
  $('favStrip').hidden = true;
  $('topStrip').hidden = true;
  let list = state.catalog.events ?? [];
  if (q) {
    const toks = tokens(q);
    list = list.map(e => ({ e, s: score(toks, searchKey(e.name)) })).filter(x => x.s > 0).sort((a, b) => b.s - a.s).map(x => x.e);
  }
  shownAll = list;
  $('allCount').textContent = list.length ? String(list.length) : '';
  const rows = el('div', 'cmd-list');
  rows.append(...list.map((e, i) => eventRow(e, i)));
  $('allGrid').replaceChildren(rows);
  const empty = $('allEmpty');
  empty.hidden = list.length > 0;
  empty.textContent = q ? t('pwa.noResults', q) : t('pwa.event.none');
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
  state.favorites = new Set(pcStore.get('favorites', []));
  state.confirm = new Set(pcStore.get('confirm', []));
  padSettings();
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
  speechSettings(!!s.assistants, !!s.local);
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
  // The app keeps a list of PCs: what it needs of this one (its id, addresses and how to wake it while it is off).
  if (app?.setPcInfo) {
    app.setPcInfo(JSON.stringify({ pc: s.pc, machine: s.machine, addresses, wake: s.wake, power: !!s.power }));
    return;
  }
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
    onMediaEvent();
  });
  events.addEventListener('catalog', () => loadCatalog());
  events.addEventListener('media', () => onMediaEvent());
  events.addEventListener('did', e => {
    try { onDid(JSON.parse(e.data)); } catch { /* not for us */ }
  });
  events.addEventListener('speech', e => {
    try { onSpeech(JSON.parse(e.data)); } catch { /* not for us */ }
  });
  events.addEventListener('speechEnd', e => {
    try { onSpeechEnd(JSON.parse(e.data)); } catch { /* not for us */ }
  });
  events.addEventListener('inbox', () => fetchInbox());
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
    fetchInbox();
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

// ── sheets ─────────────────────────────────────────────────────────

/** Open bottom sheets, newest last: each has a history entry, so back (button, gesture) closes it. */
const sheets = [];

/** A sheet from the bottom; <build>(box) fills it. Returns the function that closes it (through the history). */
function openSheet(build, className = '') {
  const back = el('div', 'sheet-back');
  const box = el('div', 'sheet ' + className);
  back.append(box);
  back.addEventListener('click', e => { if (e.target === back) history.back(); });
  build(box);
  document.body.append(back);
  history.pushState({ kdSheet: sheets.length + 1 }, '');
  const entry = { back, onClose: null };
  sheets.push(entry);
  return () => { if (sheets.includes(entry)) history.back(); };
}

/** Back closes the sheet on top; false when none is open. */
function closeTopSheet() {
  const entry = sheets.pop();
  if (!entry) return false;
  entry.onClose?.();
  entry.back.classList.add('out');
  entry.back.addEventListener('animationend', () => entry.back.remove(), { once: true });
  setTimeout(() => entry.back.remove(), 400);
  return true;
}

// ── the PC: reconnect, switch PC, power ────────────────────────────

const POWER = ['lock', 'sleep', 'hibernate', 'restart', 'shutdown'];

/** The header's PC: how the connection is, reconnect, another PC (in the app), and power when the PC allows it. */
function openPcSheet() {
  navigator.vibrate?.(6);
  const s = state.session;
  const app = window.KDesktopApp;
  let countdown = 0;
  const close = openSheet(box => {
    const head = el('div', 'sheet-head');
    const icon = el('span', 'sheet-icon');
    icon.innerHTML = svg('monitor');
    const text = el('div', 'sheet-head-text');
    text.append(el('b', '', s?.machine ?? $('machine').textContent),
      el('span', '', [t('pwa.pc.' + $('connPill').dataset.state), location.host].join(' · ')));
    head.append(icon, text);
    box.append(head);

    const rows = el('div', 'group');
    const row = (iconName, label, run) => {
      const r = el('button', 'row row-button');
      r.innerHTML = `<span class="row-icon">${svg(iconName)}</span><span class="row-label"></span><span class="row-chevron">${svg('chevron')}</span>`;
      r.querySelector('.row-label').textContent = label;
      r.addEventListener('click', run);
      return r;
    };
    rows.append(row('refresh', t('pwa.pc.reconnect'), () => { close(); connect(true); }));
    if (app?.showPcs) rows.append(row('server', t('pwa.app.changeServer'), () => app.showPcs()));
    box.append(rows);

    // Power: only with «Encender y apagar el PC» on in K-Desktop (the session says so); never for this same PC's browser.
    if (s && !s.local) {
      box.append(el('h3', 'section-title', t('pwa.power.title')));
      if (s.power) {
        const grid = el('div', 'power-grid');
        for (const action of POWER) {
          const b = el('button', 'power-btn' + (action === 'shutdown' ? ' danger' : ''));
          b.innerHTML = `<span>${svg(action === 'shutdown' ? 'power' : action)}</span><b></b>`;
          b.querySelector('b').textContent = t('pwa.power.' + action);
          b.addEventListener('click', () => askPower(action, box));
          grid.append(b);
        }
        box.append(grid);
      } else {
        box.append(el('p', 'sheet-note', t('pwa.power.notAllowed')));
      }
    }
    if (s?.version) box.append(el('p', 'sheet-note center', 'K-Desktop ' + s.version));
  });
  sheets[sheets.length - 1].onClose = () => clearInterval(countdown);

  /** Shutting down and restarting can't be undone: five seconds to change one's mind. The rest at once. */
  function askPower(action, box) {
    navigator.vibrate?.(10);
    if (action !== 'shutdown' && action !== 'restart') {
      sendPower(action);
      return;
    }
    let left = 5;
    const panel = el('div', 'countdown');
    panel.innerHTML = `<div class="count-ring"><svg viewBox="0 0 100 100"><circle class="track" cx="50" cy="50" r="44" pathLength="100"/><circle class="fill" cx="50" cy="50" r="44" pathLength="100"/></svg><b></b></div>`;
    const label = el('p', 'count-text');
    const buttons = el('div', 'sheet-buttons');
    const cancel = el('button', 'btn', t('pwa.cancel'));
    const now = el('button', 'btn danger', t('pwa.power.now'));
    buttons.append(cancel, now);
    panel.append(label, buttons);
    box.replaceChildren(panel);
    const tick = () => {
      panel.querySelector('b').textContent = String(left);
      panel.querySelector('.fill').style.strokeDasharray = `${left / 5 * 100} 100`;
      label.textContent = t(action === 'shutdown' ? 'pwa.power.countShutdown' : 'pwa.power.countRestart', s.machine, left);
      if (left <= 0) go();
      left--;
    };
    const go = () => {
      clearInterval(countdown);
      sendPower(action);
    };
    cancel.addEventListener('click', () => { clearInterval(countdown); close(); });
    now.addEventListener('click', go);
    tick();
    countdown = setInterval(tick, 1000);
  }

  async function sendPower(action) {
    try {
      await api('power', { method: 'POST', body: { action } });
      navigator.vibrate?.(20);
      close();
      toast('ok', t('pwa.power.done.' + action), s.machine, 3000);
      if (action !== 'lock') setTimeout(() => poweredOff(action), 700);
    } catch (e) {
      if (e instanceof Unpaired) return;
      close();
      toast('error', t('pwa.power.failed'), e.status === 403 ? t('pwa.power.notAllowed') : t('pwa.offline'), 5000);
    }
  }
}

/** The PC is going off (or restarting): the app shows its list of PCs; a browser, the PC as offline. */
function poweredOff(action) {
  stopEvents();
  setOnline(false);
  window.KDesktopApp?.poweredOff?.(action);
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
  setupPad({ t, store, pcStore, svg, toast, openSheet, online: setOnline, onUnpaired: () => showPair() });
  setupVoice({ t, toast, post: postBinary, updateApp, speak: () => speakWhere() });
  setupInbox({ $, el, svg, t, api, store, toast, lang: () => state.texts?.lang ?? navigator.language });
  setupSpeech({
    t, store,
    local: () => !!state.session?.local,
    visible: () => document.visibilityState === 'visible',
    headers: () => {
      const token = store.get('token');
      return token ? { 'X-KD': '1', Authorization: 'Bearer ' + token } : { 'X-KD': '1' };
    },
    talk: id => {
      chatAsked(id);
      startVoiceFor(id);
    },
  });
  setupChat({
    t, toast, api, store,
    speak: () => speakWhere(),
    stopSpeech,
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
      if (b.dataset.tab === state.tab && state.view !== 'settings') {
        window.scrollTo({ top: 0, behavior: 'smooth' });
        return;
      }
      setTab(b.dataset.tab);
    });

  $('connPill').addEventListener('click', openPcSheet);
  $('settingsButton').addEventListener('click', openSettings);
  $('titleBack').addEventListener('click', () => history.back());
  $('hiddenToggle').addEventListener('click', () => {
    navigator.vibrate?.(4);
    state.showHidden = !state.showHidden;
    store.set('showHidden', state.showHidden);
    renderAll();
  });
  $('reloadButton').addEventListener('click', () => loadCatalog(true));
  $('appUpdateButton').addEventListener('click', updateApp);
  $('backButton').addEventListener('click', () => history.back());
  // Back (button, gesture): the sheet on top, else settings, else the open submenu.
  window.addEventListener('popstate', () => {
    if (ignorePops > 0) {
      ignorePops--;
      return;
    }
    if (closeTopSheet()) return;
    if (state.view === 'settings') {
      showView(state.tab, true);
      return;
    }
    closeSubmenu();
  });
  window.addEventListener('scroll', updateHeader, { passive: true });

  const search = $('searchBox');
  search.addEventListener('input', renderAll);
  search.addEventListener('keydown', e => {
    if (e.key !== 'Enter' || !shownAll.length) return;
    e.preventDefault();
    $('allGrid').querySelector('.cmd')?.click();
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
  setupMedia({ api, t, svg, toast, changeAudio });

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
    fetchInbox();
    if (Date.now() - state.lastLoad > 5000) {
      loadSession();
      loadCatalog();
    }
  });
}

async function start() {
  applyTheme(store.get('theme', 'system'));
  wire();
  // The Android app's shortcuts (holding its icon): a tab, or talking (to the chosen assistant, else to the commands).
  window.kdOpenTab = tab => setTab(tab);
  window.kdTalk = () => {
    if (state.session?.assistants && chatTalk()) return;
    if (!$('voiceButton').hidden) $('voiceButton').click();
  };
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
