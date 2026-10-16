// Touchpad and keyboard: gestures on the pad become mouse input on the PC, over a WebSocket (api/input).
// One finger moves, a tap clicks, tap and drag (or a long press) drags, two fingers scroll (with inertia),
// a two/three-finger tap is a right/middle click. The phone's own keyboard types live; extra keys and
// sticky modifiers (tap: next key only, double tap: held) cover what it lacks.

const TAP_MS = 230;          // a touch shorter than this (and still) is a tap
const TAP_SLOP = 9;          // px a tap may wander
const DRAG_WAIT = 210;       // after a tap, a new touch within this starts a drag instead of a click
const HOLD_MS = 480;         // a still finger held this long starts a drag
const SENTINEL = '»'.repeat(8);

let ctx;                     // { t, store, svg, toast }
let socket = null;
let wanted = false;          // the tab is open: keep connected
let retry = 0;
let retryTimer = 0;
let pingTimer = 0;
let status = 'off';

const out = { x: 0, y: 0, wx: 0, wy: 0, frame: 0 };
const settings = { speed: 2, scroll: 1.6, natural: true, tap: true };

// ── connection ─────────────────────────────────────────────────────

function setStatus(s) {
  status = s;
  document.getElementById('pad').classList.toggle('offline', s !== 'connected');
  // The connection shows in the header, next to the PC name.
  if (s === 'connected') ctx.online?.(true);
  else if (s === 'offline') ctx.online?.(false);
}

function connect() {
  if (!wanted || socket || document.visibilityState !== 'visible') return;
  clearTimeout(retryTimer);
  const url = new URL('api/input', location.href);
  url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  setStatus('connecting');
  let ws;
  try { ws = new WebSocket(url); } catch { scheduleRetry(); return; }
  socket = ws;
  ws.onopen = () => {
    // The cookie usually authenticates already; the token covers the cases where it does not travel.
    const token = ctx.store.get('token');
    if (token) ws.send(JSON.stringify({ t: 'auth', token }));
    retry = 0;
    setStatus('connected');
    clearInterval(pingTimer);
    pingTimer = setInterval(() => send({ t: 'p' }), 15000);
  };
  ws.onclose = e => {
    if (socket === ws) socket = null;
    clearInterval(pingTimer);
    releaseLocal();
    if (e.code === 4401) {
      setStatus('offline');
      ctx.onUnpaired?.();
      return;
    }
    scheduleRetry();
  };
  ws.onerror = () => { /* onclose follows */ };
}

function scheduleRetry() {
  if (!wanted) {
    setStatus('off');
    return;
  }
  setStatus('offline');
  clearTimeout(retryTimer);
  retryTimer = setTimeout(connect, Math.min(5000, 600 * 2 ** retry++));
}

function disconnect() {
  wanted = false;
  clearTimeout(retryTimer);
  clearInterval(pingTimer);
  const ws = socket;
  socket = null;
  ws?.close();
  releaseLocal();
}

function send(msg) {
  if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify(msg));
}

// ── pointer output (batched per frame) ─────────────────────────────

function queueMove(dx, dy) {
  out.x += dx;
  out.y += dy;
  schedule();
}

function queueWheel(dx, dy) {
  out.wx += dx;
  out.wy += dy;
  schedule();
}

function schedule() {
  if (!out.frame) out.frame = requestAnimationFrame(flush);
}

function flush() {
  out.frame = 0;
  // Whole pixels / wheel units go now, the fractions stay for the next frame.
  const x = Math.trunc(out.x), y = Math.trunc(out.y);
  if (x || y) {
    out.x -= x;
    out.y -= y;
    send({ t: 'm', x, y });
  }
  const wx = Math.trunc(out.wx), wy = Math.trunc(out.wy);
  if (wx || wy) {
    out.wx -= wx;
    out.wy -= wy;
    send({ t: 'w', x: wx, y: wy });
  }
}

/** Pointer gain: slow movements stay precise, fast ones cross the screen. */
function gain(dx, dy, dt) {
  const v = Math.hypot(dx, dy) / Math.max(dt, 1);   // CSS px per ms
  return settings.speed * (0.55 + Math.min(v, 2.6) * 0.85);
}

// ── gestures ───────────────────────────────────────────────────────

const touches = new Map();   // pointerId → { x, y, sx, sy, t }
const g = {
  mode: 'none',              // none | move | scroll | drag
  start: 0,                  // time the gesture began
  maxCount: 0,               // most fingers seen in this gesture
  moved: false,
  lastTapEnd: 0,
  clickTimer: 0,
  dragCandidate: false,      // a touch right after a tap
  holdTimer: 0,
  dragging: false,
  last: 0,                   // time of the last move event
  vx: 0, vy: 0,              // scroll velocity (CSS px / ms) for the inertia
  sample: { x: 0, y: 0, t: 0 },
  inertia: 0,
};

function padPoint(e) {
  return { x: e.clientX, y: e.clientY };
}

function onDown(e) {
  if (status !== 'connected') return;
  e.preventDefault();
  const pad = e.currentTarget;
  pad.setPointerCapture?.(e.pointerId);
  stopInertia();
  const p = padPoint(e);
  const now = performance.now();
  touches.set(e.pointerId, { x: p.x, y: p.y, sx: p.x, sy: p.y, t: now });
  showFinger(e.pointerId, p);
  hideHint();

  if (touches.size === 1) {
    g.start = now;
    g.last = e.timeStamp || now;
    g.maxCount = 1;
    g.moved = false;
    g.mode = 'move';
    // Touch again right after a tap: that tap becomes the start of a drag.
    g.dragCandidate = !!g.clickTimer && now - g.lastTapEnd < DRAG_WAIT;
    if (g.dragCandidate) {
      clearTimeout(g.clickTimer);
      g.clickTimer = 0;
    }
    clearTimeout(g.holdTimer);
    g.holdTimer = setTimeout(() => {
      if (touches.size === 1 && !g.moved && !g.dragging) startDrag();
    }, HOLD_MS);
  } else {
    g.maxCount = Math.max(g.maxCount, touches.size);
    clearTimeout(g.holdTimer);
    if (!g.dragging) g.mode = 'scroll';
    g.vx = g.vy = 0;
    g.sample = { x: 0, y: 0, t: 0 };
  }
}

function onMove(e) {
  const touch = touches.get(e.pointerId);
  if (!touch) return;
  e.preventDefault();
  const now = performance.now();
  // Coalesced events give the full resolution of the finger on fast phones.
  const events = e.getCoalescedEvents?.() ?? [];
  const list = events.length ? events : [e];
  for (const ev of list) {
    const p = padPoint(ev);
    const dx = p.x - touch.x, dy = p.y - touch.y;
    const at = ev.timeStamp || now;
    touch.x = p.x;
    touch.y = p.y;
    if (!g.moved && Math.hypot(p.x - touch.sx, p.y - touch.sy) > TAP_SLOP) {
      g.moved = true;
      clearTimeout(g.holdTimer);
      if (g.dragCandidate && touches.size === 1 && !g.dragging) startDrag();
    }
    if (!g.moved) {
      g.last = at;
      continue;
    }

    if (g.mode === 'scroll' && touches.size >= 2) {
      // Every finger reports: use the average.
      const n = touches.size;
      scrollBy(dx / n, dy / n, at);
    } else if (touches.size === 1 && g.mode !== 'scroll') {
      const k = gain(dx, dy, at - g.last);
      queueMove(dx * k, dy * k);
    }
    g.last = at;
  }
  moveFinger(e.pointerId, padPoint(e));
}

function onUp(e) {
  const touch = touches.get(e.pointerId);
  if (!touch) return;
  e.preventDefault();
  touches.delete(e.pointerId);
  hideFinger(e.pointerId);
  const now = performance.now();
  if (touches.size > 0) return;

  clearTimeout(g.holdTimer);
  const short = now - g.start < TAP_MS * (g.maxCount > 1 ? 1.4 : 1);
  if (g.dragging) {
    endDrag();
  } else if (!g.moved && short) {
    if (g.maxCount === 2) click('r');
    else if (g.maxCount >= 3) click('m');
    else if (g.dragCandidate) {
      // Tap, tap: a double click.
      send({ t: 'c', b: 'l', n: 2 });
      buzz(8);
      tapRipple(touch);
    } else if (settings.tap) {
      // Wait a moment: a touch right now turns this tap into a drag.
      g.lastTapEnd = now;
      tapRipple(touch);
      g.clickTimer = setTimeout(() => {
        g.clickTimer = 0;
        click('l', false);
      }, DRAG_WAIT);
    }
  } else if (g.mode === 'scroll' && g.moved) {
    startInertia();
  }
  g.mode = 'none';
  g.dragCandidate = false;
}

function click(button, ripple = true) {
  send({ t: 'c', b: button, n: 1 });
  buzz(button === 'l' ? 6 : 12);
  if (ripple) tapRipple(null, button);
}

function startDrag() {
  g.dragging = true;
  g.mode = 'move';
  send({ t: 'b', b: 'l', d: true });
  buzz(15);
  document.getElementById('pad').classList.add('dragging');
}

function endDrag() {
  g.dragging = false;
  send({ t: 'b', b: 'l', d: false });
  buzz(6);
  document.getElementById('pad').classList.remove('dragging');
}

// ── scrolling ──────────────────────────────────────────────────────

function scrollBy(dx, dy, now) {
  // Velocity for the fling at the end, measured over windows of time: the fingers report almost at
  // once, and dividing by those tiny gaps would give absurd speeds.
  const s = g.sample;
  if (!s.t) s.t = now;
  s.x += dx;
  s.y += dy;
  const dt = now - s.t;
  if (dt >= 16) {
    const cap = v => Math.max(-3, Math.min(3, v));
    g.vx = g.vx * 0.5 + cap(s.x / dt) * 0.5;
    g.vy = g.vy * 0.5 + cap(s.y / dt) * 0.5;
    s.x = s.y = 0;
    s.t = now;
  }
  wheel(dx, dy);
}

/** Finger movement in CSS px → wheel units (120 = one notch). */
function wheel(dx, dy) {
  const k = settings.scroll * 3.2;
  const sign = settings.natural ? 1 : -1;
  // Natural: the content follows the finger (finger down = scroll up = positive wheel).
  queueWheel(-dx * k * sign, dy * k * sign);
}

function startInertia() {
  let vx = g.vx, vy = g.vy;
  if (Math.hypot(vx, vy) < 0.25) return;
  let last = performance.now();
  const step = now => {
    const dt = Math.min(now - last, 40);
    last = now;
    wheel(vx * dt, vy * dt);
    const decay = Math.pow(0.994, dt);
    vx *= decay;
    vy *= decay;
    g.inertia = Math.hypot(vx, vy) > 0.03 ? requestAnimationFrame(step) : 0;
  };
  g.inertia = requestAnimationFrame(step);
}

function stopInertia() {
  if (g.inertia) cancelAnimationFrame(g.inertia);
  g.inertia = 0;
}

// ── feedback ───────────────────────────────────────────────────────

const fingers = new Map();

function showFinger(id, p) {
  const pad = document.getElementById('pad');
  const dot = document.createElement('span');
  dot.className = 'finger';
  pad.append(dot);
  fingers.set(id, dot);
  moveFinger(id, p);
}

function moveFinger(id, p) {
  const dot = fingers.get(id);
  if (!dot) return;
  const r = dot.parentElement.getBoundingClientRect();
  dot.style.translate = `${p.x - r.left}px ${p.y - r.top}px`;
}

function hideFinger(id) {
  const dot = fingers.get(id);
  fingers.delete(id);
  if (!dot) return;
  dot.classList.add('out');
  dot.addEventListener('animationend', () => dot.remove());
}

function tapRipple(touch, button = 'l') {
  const pad = document.getElementById('pad');
  const r = pad.getBoundingClientRect();
  const ring = document.createElement('span');
  ring.className = 'tap-ring ' + button;
  const x = touch ? touch.x - r.left : r.width / 2;
  const y = touch ? touch.y - r.top : r.height / 2;
  ring.style.left = x + 'px';
  ring.style.top = y + 'px';
  pad.append(ring);
  ring.addEventListener('animationend', () => ring.remove());
}

function hideHint() {
  if (ctx.store.get('padHintSeen')) return;
  ctx.store.set('padHintSeen', true);
  document.getElementById('padHint').classList.add('gone');
}

const buzz = ms => navigator.vibrate?.(ms);

/** Nothing stays pressed on the PC when the connection drops (the server releases its side too). */
function releaseLocal() {
  touches.clear();
  for (const id of [...fingers.keys()]) hideFinger(id);
  clearTimeout(g.holdTimer);
  clearTimeout(g.clickTimer);
  g.clickTimer = 0;
  stopInertia();
  if (g.dragging) {
    g.dragging = false;
    document.getElementById('pad')?.classList.remove('dragging');
  }
  for (const b of document.querySelectorAll('.pad-buttons button.down')) b.classList.remove('down');
  for (const mod of Object.keys(mods)) setMod(mod, 0, false);
}

// ── mouse buttons under the pad ────────────────────────────────────

function wireButtons() {
  for (const b of document.querySelectorAll('.pad-buttons button')) {
    const button = b.dataset.button;
    b.addEventListener('pointerdown', e => {
      if (status !== 'connected') return;
      e.preventDefault();
      b.setPointerCapture?.(e.pointerId);
      b.classList.add('down');
      send({ t: 'b', b: button, d: true });
      buzz(8);
    });
    const up = e => {
      if (!b.classList.contains('down')) return;
      e.preventDefault();
      b.classList.remove('down');
      send({ t: 'b', b: button, d: false });
    };
    b.addEventListener('pointerup', up);
    b.addEventListener('pointercancel', up);
    b.addEventListener('contextmenu', e => e.preventDefault());
  }
}

// ── keyboard ───────────────────────────────────────────────────────

const MODS = ['CTRL', 'ALT', 'SHIFT', 'WIN'];
const mods = { CTRL: 0, ALT: 0, SHIFT: 0, WIN: 0 };   // 0 off, 1 next key only, 2 held
const modTaps = {};

const NAV_KEYS = [
  ['Escape', 'Esc'], ['Tab', '⇥'], ['Backspace', '⌫'], ['Delete', 'pwa.key.delete'], ['Enter', '↵'],
  ['Left', '←'], ['Up', '↑'], ['Down', '↓'], ['Right', '→'],
  ['Home', 'pwa.key.home'], ['End', 'pwa.key.end'], ['PageUp', 'pwa.key.pageUp'], ['PageDown', 'pwa.key.pageDown'],
  ...Array.from({ length: 12 }, (_, i) => ['F' + (i + 1), 'F' + (i + 1)]),
  ['PrintScreen', 'pwa.key.print'], ['AppsKey', '☰'],
];

/** Browser key names (hardware keyboards, desktop browsers) → K-Desktop key names. */
const BROWSER_KEYS = {
  Escape: 'Escape', Tab: 'Tab', Delete: 'Delete', Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
  ArrowLeft: 'Left', ArrowUp: 'Up', ArrowDown: 'Down', ArrowRight: 'Right', Insert: 'Insert', ContextMenu: 'AppsKey',
  ...Object.fromEntries(Array.from({ length: 12 }, (_, i) => ['F' + (i + 1), 'F' + (i + 1)])),
};

function onceMods() {
  return MODS.filter(m => mods[m] === 1);
}

function clearOnceMods() {
  for (const m of MODS) if (mods[m] === 1) setMod(m, 0);
}

function setMod(mod, value, notify = true) {
  const before = mods[mod];
  mods[mod] = value;
  if (notify && before === 2 && value !== 2) send({ t: 'ku', k: mod });
  if (notify && value === 2 && before !== 2) send({ t: 'kd', k: mod });
  const b = document.querySelector(`.key[data-mod="${mod}"]`);
  b?.classList.toggle('once', value === 1);
  b?.classList.toggle('held', value === 2);
}

/** Tap: for the next key only. Tap again quickly: held (Alt+Tab…). Tap once more: off. */
function tapMod(mod) {
  const now = performance.now();
  const quick = now - (modTaps[mod] ?? 0) < 350;
  modTaps[mod] = now;
  buzz(5);
  if (mods[mod] === 0) setMod(mod, 1);
  else if (mods[mod] === 1 && quick) setMod(mod, 2);
  else setMod(mod, 0);
}

function pressKey(key) {
  send({ t: 'k', k: key, m: onceMods() });
  clearOnceMods();
  buzz(4);
}

function keyButton(label, className, onTap) {
  const b = document.createElement('button');
  b.className = 'key' + (className ? ' ' + className : '');
  b.textContent = label;
  b.addEventListener('pointerdown', e => {
    e.preventDefault();   // keep the phone keyboard open
    if (status !== 'connected') return;
    b.classList.add('down');
    onTap(b);
  });
  const up = () => b.classList.remove('down');
  b.addEventListener('pointerup', up);
  b.addEventListener('pointercancel', up);
  b.addEventListener('pointerleave', up);
  b.addEventListener('contextmenu', e => e.preventDefault());
  return b;
}

function buildKeys() {
  const modRow = document.getElementById('keysMods');
  modRow.replaceChildren(...MODS.map(m => {
    const b = keyButton(ctx.t('pwa.key.' + m.toLowerCase()), 'mod', () => tapMod(m));
    b.dataset.mod = m;
    return b;
  }));
  // Keys that repeat while held, like on a real keyboard.
  const repeating = new Set(['Backspace', 'Delete', 'Left', 'Up', 'Down', 'Right']);
  document.getElementById('keysNav').replaceChildren(...NAV_KEYS.map(([key, label]) => {
    const b = keyButton(label.startsWith('pwa.') ? ctx.t(label) : label, '', () => {
      pressKey(key);
      if (!repeating.has(key)) return;
      let timer = setTimeout(function again() {
        if (!b.classList.contains('down')) return;
        send({ t: 'k', k: key, m: [] });
        timer = setTimeout(again, 55);
      }, 420);
      b.addEventListener('pointerup', () => clearTimeout(timer), { once: true });
    });
    return b;
  }));
  for (const m of MODS) setMod(m, mods[m], false);
}

/**
 * The phone keyboard types into a hidden text box. What changed in it (the common start kept, the rest
 * erased and the new end typed) is replayed on the PC, so predictions, autocorrect and swipe typing all work.
 * A few sentinel characters before the text make Backspace work with nothing typed.
 */
const kb = { value: SENTINEL, composing: false };

function resetKb() {
  const input = document.getElementById('kbInput');
  kb.value = SENTINEL;
  input.value = SENTINEL;
  input.setSelectionRange(SENTINEL.length, SENTINEL.length);
}

function onKbInput() {
  const input = document.getElementById('kbInput');
  const value = input.value;
  const old = kb.value;
  let p = 0;
  while (p < old.length && p < value.length && old[p] === value[p]) p++;
  const erased = old.length - p;
  const typed = value.slice(p);
  kb.value = value;

  for (let i = 0; i < erased; i++) send({ t: 'k', k: 'Backspace', m: [] });
  if (typed) {
    const combo = onceMods();
    const single = [...typed].length === 1;
    if ((combo.length || MODS.some(m => mods[m] === 2)) && single && /^[a-z0-9 ]$/i.test(typed)) {
      // Ctrl + C and friends: as a key, not as text.
      send({ t: 'k', k: typed === ' ' ? 'Space' : typed.toUpperCase(), m: combo });
      clearOnceMods();
    } else {
      send({ t: 't', s: typed });
      if (combo.length) clearOnceMods();
    }
  }
  if (!kb.composing && (!value.startsWith(SENTINEL) || value.length > SENTINEL.length + 40 || /\n/.test(typed))) resetKb();
}

function onKbKeyDown(e) {
  // Keys a phone keyboard does not type as text (hardware keyboards, desktop browsers).
  const key = BROWSER_KEYS[e.key];
  const combo = [e.ctrlKey && 'CTRL', e.altKey && 'ALT', e.shiftKey && 'SHIFT', e.metaKey && 'WIN'].filter(Boolean);
  if (key) {
    e.preventDefault();
    send({ t: 'k', k: key, m: [...new Set([...combo, ...onceMods()])] });
    clearOnceMods();
  } else if ((e.ctrlKey || e.altKey || e.metaKey) && /^[a-z0-9]$/i.test(e.key)) {
    e.preventDefault();
    send({ t: 'k', k: e.key.toUpperCase(), m: combo });
  }
}

let fullHeight = 0;

/** With resizes-content the page shrinks while the phone keyboard is up. */
function keyboardUp() {
  fullHeight = Math.max(fullHeight, window.innerHeight);
  return window.innerHeight < fullHeight * 0.78;
}

function updateKbState() {
  const focused = document.activeElement === document.getElementById('kbInput');
  document.body.classList.toggle('kb-open', focused && (keyboardUp() || !matchMedia('(pointer: coarse)').matches));
  document.getElementById('keyboardButton').classList.toggle('active', focused);
}

function toggleKeyboard() {
  const input = document.getElementById('kbInput');
  buzz(5);
  // Focused but the keyboard closed (Android's back button): open it again instead.
  if (document.activeElement === input && (keyboardUp() || !matchMedia('(pointer: coarse)').matches)) {
    input.blur();
    return;
  }
  input.blur();
  resetKb();
  input.focus({ preventScroll: true });
  // The extra keys come with the keyboard.
  setKeysPanel(true);
}

function setKeysPanel(on) {
  document.getElementById('keysPanel').hidden = !on;
  document.getElementById('keysButton').classList.toggle('active', on);
  ctx.store.set('padKeys', on);
}

// ── settings ───────────────────────────────────────────────────────

function loadSettings() {
  Object.assign(settings, ctx.store.get('pad', {}));
  const speed = document.getElementById('padSpeed');
  const scroll = document.getElementById('padScroll');
  const fill = input => input.style.setProperty('--fill', ((input.value - input.min) / (input.max - input.min) * 100) + '%');
  speed.value = settings.speed;
  scroll.value = settings.scroll;
  document.getElementById('padNatural').checked = settings.natural;
  document.getElementById('padTap').checked = settings.tap;
  fill(speed);
  fill(scroll);
  const save = () => {
    settings.speed = Number(speed.value);
    settings.scroll = Number(scroll.value);
    settings.natural = document.getElementById('padNatural').checked;
    settings.tap = document.getElementById('padTap').checked;
    ctx.store.set('pad', settings);
    fill(speed);
    fill(scroll);
  };
  for (const id of ['padSpeed', 'padScroll', 'padNatural', 'padTap'])
    document.getElementById(id).addEventListener('input', save);
}

// ── public ─────────────────────────────────────────────────────────

export function setupPad(deps) {
  ctx = deps;
  const pad = document.getElementById('pad');
  pad.addEventListener('pointerdown', onDown);
  pad.addEventListener('pointermove', onMove);
  pad.addEventListener('pointerup', onUp);
  pad.addEventListener('pointercancel', onUp);
  pad.addEventListener('contextmenu', e => e.preventDefault());
  if (ctx.store.get('padHintSeen')) document.getElementById('padHint').classList.add('gone');

  wireButtons();
  buildKeys();
  loadSettings();
  setKeysPanel(!!ctx.store.get('padKeys', false));

  const input = document.getElementById('kbInput');
  resetKb();
  input.addEventListener('input', onKbInput);
  input.addEventListener('keydown', onKbKeyDown);
  input.addEventListener('compositionstart', () => { kb.composing = true; });
  input.addEventListener('compositionend', () => {
    kb.composing = false;
    onKbInput();
  });
  input.addEventListener('focus', updateKbState);
  input.addEventListener('blur', updateKbState);
  window.addEventListener('resize', updateKbState);
  window.addEventListener('orientationchange', () => { fullHeight = 0; });
  const kbButton = document.getElementById('keyboardButton');
  // Cancelling pointerdown keeps the focus in the text box; the keyboard itself only opens from a click.
  kbButton.addEventListener('pointerdown', e => e.preventDefault());
  kbButton.addEventListener('click', toggleKeyboard);
  document.getElementById('keysButton').addEventListener('click', () => {
    buzz(5);
    setKeysPanel(document.getElementById('keysPanel').hidden);
  });

  document.addEventListener('visibilitychange', () => {
    if (!wanted) return;
    if (document.visibilityState === 'visible') connect();
    else {
      // In the background: let go (nothing stays pressed) and save the battery.
      const ws = socket;
      socket = null;
      ws?.close();
      releaseLocal();
      setStatus('offline');
    }
  });
  setStatus('off');
}

/** Called when the tab opens or closes, and when the session says whether the PC allows it. */
export function padActive(on, allowed = true) {
  document.getElementById('padOff').hidden = allowed;
  document.getElementById('pad').hidden = !allowed;
  document.querySelector('.pad-buttons').hidden = !allowed;
  document.querySelector('.pad-bar').hidden = !allowed;
  if (!allowed) document.getElementById('keysPanel').hidden = true;
  else setKeysPanel(!!ctx.store.get('padKeys', false));

  if (on && allowed) {
    wanted = true;
    connect();
  } else {
    disconnect();
    document.getElementById('kbInput').blur();
    setStatus('off');
  }
}

/** Texts changed (language): rebuild the key labels. */
export function padTexts() {
  if (!ctx) return;
  buildKeys();
  setStatus(status);
}
