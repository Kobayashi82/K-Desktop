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
// Kept per PC (each screen and mouse is different). keys: the special keys of the row, in order (null: all).
const DEFAULTS = { speed: 2, accel: 1, scroll: 1.6, natural: true, tap: true, gestures: true, pinch: true, keys: null };
const settings = { ...DEFAULTS };

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
    ws.send(JSON.stringify({ t: 'z', a: 'state' }));
    retry = 0;
    setStatus('connected');
    clearInterval(pingTimer);
    pingTimer = setInterval(() => send({ t: 'p' }), 15000);
  };
  ws.onmessage = e => {
    let msg;
    try { msg = JSON.parse(e.data); } catch { return; }
    if (msg.t === 'z') showZoom(!!msg.on, Number(msg.z) || 2);
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

/** Pointer gain: slow movements stay precise, fast ones cross the screen (how much: the acceleration, 0 = none). */
function gain(dx, dy, dt) {
  const v = Math.hypot(dx, dy) / Math.max(dt, 1);   // CSS px per ms
  return settings.speed * (1 + (0.55 + Math.min(v, 2.6) * 0.85 - 1) * settings.accel);
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
  // Two fingers: scroll or pinch, decided by what moves first. Three or four: a swipe (see swipeBy).
  pinch: { from: 0, ref: 0, cx: 0, cy: 0, decided: false, ctrl: false },
  swipe: { x: 0, y: 0, dir: '', steps: 0, alt: false },
};

/** Distance between the first two fingers and their middle point. */
function twoFingers() {
  const [a, b] = [...touches.values()];
  return { d: Math.hypot(a.x - b.x, a.y - b.y), cx: (a.x + b.x) / 2, cy: (a.y + b.y) / 2 };
}

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
    if (!g.dragging) g.mode = touches.size >= 3 && settings.gestures ? 'swipe' : 'scroll';
    g.vx = g.vy = 0;
    g.sample = { x: 0, y: 0, t: 0 };
    if (touches.size === 2) {
      const two = twoFingers();
      endPinch();
      g.pinch = { from: two.d, ref: two.d, cx: two.cx, cy: two.cy, decided: !settings.pinch, ctrl: false };
    }
    if (g.mode === 'swipe') {
      endPinch();
      g.swipe = { x: 0, y: 0, dir: '', steps: 0, alt: false };
    }
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

    if (g.mode === 'swipe') {
      swipeBy(dx / touches.size, dy / touches.size);
    } else if (g.mode === 'pinch') {
      pinchTo(twoFingers().d);
    } else if (g.mode === 'scroll' && touches.size === 2 && !g.pinch.decided) {
      // Fingers apart or together: zoom. Both the same way: scroll.
      const two = twoFingers();
      const spread = Math.abs(two.d - g.pinch.from);
      const travel = Math.hypot(two.cx - g.pinch.cx, two.cy - g.pinch.cy);
      if (spread > 14 && spread > travel * 1.3) {
        g.mode = 'pinch';
        g.pinch.decided = true;
        g.pinch.ref = two.d;
        startPinch();
      } else if (travel > 10) g.pinch.decided = true;
    } else if (g.mode === 'scroll' && touches.size >= 2) {
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
  if (g.mode === 'pinch' && touches.size < 2) {
    // One finger up ends the zoom; the other one left does nothing until it lifts too.
    endPinch();
    g.mode = 'scroll';
  }
  if (touches.size > 0) return;

  clearTimeout(g.holdTimer);
  if (g.mode === 'swipe') endSwipe();
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

// ── three and four fingers, pinch ──────────────────────────────────

const SWIPE_START = 40;      // px the fingers travel before a swipe counts
const SWIPE_STEP = 70;       // px per window while switching windows

/**
 * Like a Windows touchpad. Three fingers sideways: switch windows (Alt held while the fingers are down, a step every
 * SWIPE_STEP, back the other way); up: task view; down: the desktop. Four fingers sideways: the next or previous
 * virtual desktop (the content follows the fingers).
 */
function swipeBy(dx, dy) {
  const s = g.swipe;
  s.x += dx;
  s.y += dy;
  if (!s.dir) {
    if (Math.hypot(s.x, s.y) < SWIPE_START) return;
    s.dir = Math.abs(s.x) > Math.abs(s.y) ? (s.x > 0 ? 'right' : 'left') : (s.y > 0 ? 'down' : 'up');
    const four = g.maxCount >= 4;
    if (s.dir === 'up') gesture('taskView', () => send({ t: 'k', k: 'Tab', m: ['WIN'] }));
    else if (s.dir === 'down') gesture('desktop', () => send({ t: 'k', k: 'D', m: ['WIN'] }));
    else if (four) gesture('vdesk', () => send({ t: 'k', k: s.dir === 'left' ? 'Right' : 'Left', m: ['CTRL', 'WIN'] }));
    else {
      gesture('windows', () => {
        if (mods.ALT !== 2) {
          send({ t: 'kd', k: 'ALT' });
          s.alt = true;
        }
        send({ t: 'k', k: 'Tab', m: s.dir === 'right' ? [] : ['SHIFT'] });
      });
      s.ref = s.x;
    }
    return;
  }
  if (!s.alt) return;
  while (s.x - s.ref >= SWIPE_STEP) {
    send({ t: 'k', k: 'Tab', m: [] });
    s.ref += SWIPE_STEP;
    buzz(5);
  }
  while (s.ref - s.x >= SWIPE_STEP) {
    send({ t: 'k', k: 'Tab', m: ['SHIFT'] });
    s.ref -= SWIPE_STEP;
    buzz(5);
  }
}

/** Fingers up: letting go of Alt picks the window. */
function endSwipe() {
  if (g.swipe.alt) send({ t: 'ku', k: 'ALT' });
  g.swipe.alt = false;
  hideGesture();
}

const PINCH_STEP = 38;       // px of change between the fingers per zoom step

/** Pinch: Ctrl held while the fingers are down and the wheel per step, the zoom of browsers and most programs. */
function startPinch() {
  if (mods.CTRL !== 2) {
    send({ t: 'kd', k: 'CTRL' });
    g.pinch.ctrl = true;
  }
  showGesture('zoom');
  buzz(8);
}

function pinchTo(d) {
  const p = g.pinch;
  while (d - p.ref >= PINCH_STEP) {
    send({ t: 'w', x: 0, y: 120 });
    p.ref += PINCH_STEP;
    buzz(4);
  }
  while (p.ref - d >= PINCH_STEP) {
    send({ t: 'w', x: 0, y: -120 });
    p.ref -= PINCH_STEP;
    buzz(4);
  }
}

function endPinch() {
  if (g.pinch.ctrl) send({ t: 'ku', k: 'CTRL' });
  g.pinch.ctrl = false;
  hideGesture();
}

/** Does it and says which gesture it was, on the pad. */
function gesture(name, act) {
  act();
  buzz(12);
  showGesture(name);
}

let gestureTimer = 0;

function showGesture(name) {
  const label = document.getElementById('padGesture');
  clearTimeout(gestureTimer);
  label.textContent = ctx.t('pwa.pad.gesture.' + name);
  label.classList.add('on');
  // The ones that are done at once go away by themselves; the held ones when the fingers lift.
  if (name !== 'windows' && name !== 'zoom') gestureTimer = setTimeout(hideGesture, 1100);
}

function hideGesture() {
  clearTimeout(gestureTimer);
  gestureTimer = setTimeout(() => document.getElementById('padGesture')?.classList.remove('on'), 250);
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
  // The server lets go of Alt and Ctrl itself when the connection drops.
  g.swipe.alt = g.pinch.ctrl = false;
  g.mode = 'none';
  document.getElementById('padGesture')?.classList.remove('on');
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
  ['Insert', 'pwa.key.insert'], ['PrintScreen', 'pwa.key.print'], ['AppsKey', '☰'],
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

/** The special keys scroll sideways with the mouse wheel too (on a computer). */
function wheelScrollsSideways(row) {
  row.addEventListener('wheel', e => {
    if (row.scrollWidth <= row.clientWidth || Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
    e.preventDefault();
    row.scrollBy({ left: e.deltaY * (e.deltaMode === 1 ? 32 : 1), behavior: 'auto' });
  }, { passive: false });
}

// ── on-screen keyboard ─────────────────────────────────────────────

/** No touch screen: the keyboard button shows an on-screen keyboard (the physical one types as well). */
const noTouch = () => !matchMedia('(any-pointer: coarse)').matches;
const VK_ROWS = [
  ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0', ['Backspace', '⌫', 'wide']],
  ['q', 'w', 'e', 'r', 't', 'y', 'u', 'i', 'o', 'p'],
  ['a', 's', 'd', 'f', 'g', 'h', 'j', 'k', 'l', 'ñ', ['Enter', '↵', 'wide']],
  [['Shift', '⇧', 'wide shift'], 'z', 'x', 'c', 'v', 'b', 'n', 'm', ',', '.', '-'],
  [['Space', '', 'space']],
];
let vkShift = false;

function vkType(ch) {
  const combo = onceMods();
  if ((combo.length || MODS.some(m => mods[m] === 2)) && /^[a-z0-9]$/i.test(ch)) {
    // Ctrl + C and friends: as a key, not as text.
    send({ t: 'k', k: ch.toUpperCase(), m: combo });
    clearOnceMods();
  } else {
    send({ t: 't', s: vkShift ? ch.toUpperCase() : ch });
    if (combo.length) clearOnceMods();
  }
  if (vkShift) setVkShift(false);
  buzz(4);
}

function setVkShift(on) {
  vkShift = on;
  const vk = document.getElementById('vk');
  vk.querySelector('.shift')?.classList.toggle('on', on);
  for (const b of vk.querySelectorAll('[data-ch]')) b.textContent = on ? b.dataset.ch.toUpperCase() : b.dataset.ch;
}

function buildVk() {
  document.getElementById('vk').replaceChildren(...VK_ROWS.map(row => {
    const r = document.createElement('div');
    r.className = 'vk-row';
    r.append(...row.map(k => {
      if (typeof k === 'string') {
        const b = keyButton(k, '', () => vkType(k));
        b.dataset.ch = k;
        return b;
      }
      const [key, label, cls] = k;
      if (key === 'Shift') return keyButton(label, cls, () => { buzz(5); setVkShift(!vkShift); });
      if (key === 'Space') return keyButton(label, cls, () => vkType(' '));
      const b = keyButton(label, cls, () => {
        pressKey(key);
        if (key !== 'Backspace') return;
        let timer = setTimeout(function again() {
          if (!b.classList.contains('down')) return;
          send({ t: 'k', k: key, m: [] });
          timer = setTimeout(again, 55);
        }, 420);
        b.addEventListener('pointerup', () => clearTimeout(timer), { once: true });
      });
      return b;
    }));
    return r;
  }));
  setVkShift(false);
}

function setVk(on) {
  document.getElementById('vk').hidden = !on;
  document.getElementById('keyboardButton').classList.toggle('active', on);
  ctx.store.set('padVk', on);
  const input = document.getElementById('kbInput');
  if (on) {
    resetKb();
    input.focus({ preventScroll: true }); // the physical keyboard types too
  } else input.blur();
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
  document.getElementById('keysNav').replaceChildren(...keyList().map(key => {
    const b = keyButton(keyLabel(key), '', () => {
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
  if (noTouch()) return; // the on-screen keyboard has its own state
  const focused = document.activeElement === document.getElementById('kbInput');
  // Closed with Android's back button the box keeps the focus: what counts is whether the keyboard is up.
  const up = focused && keyboardUp();
  document.body.classList.toggle('kb-open', up);
  document.getElementById('keyboardButton').classList.toggle('active', up);
  updateFull();
}

// ── landscape: the whole screen ────────────────────────────────────

/** A phone on its side: the pad takes the whole screen (no header or tabs; in the app, no system bars either). */
const landscape = matchMedia('(orientation: landscape) and (max-height: 600px) and (any-pointer: coarse)');
let padShown = false;

function updateFull() {
  const on = padShown && landscape.matches && !document.body.classList.contains('kb-open');
  if (document.body.classList.contains('pad-full') === on) return;
  document.body.classList.toggle('pad-full', on);
  window.KDesktopApp?.setFullscreen?.(on);
}

function toggleKeyboard() {
  const input = document.getElementById('kbInput');
  buzz(5);
  if (noTouch()) {
    setVk(document.getElementById('vk').hidden);
    return;
  }
  // Focused but the keyboard closed (Android's back button): open it again instead.
  if (document.activeElement === input && (keyboardUp() || !matchMedia('(pointer: coarse)').matches)) {
    input.blur();
    return;
  }
  input.blur();
  resetKb();
  input.focus({ preventScroll: true });
  // The special keys have a button of their own: each button opens and closes only its panel.
}

function setKeysPanel(on) {
  document.getElementById('keysPanel').hidden = !on;
  document.getElementById('keysButton').classList.toggle('active', on);
  ctx.store.set('padKeys', on);
}

// ── settings ───────────────────────────────────────────────────────

const RANGES = { padSpeed: 'speed', padAccel: 'accel', padScroll: 'scroll' };
const SWITCHES = { padNatural: 'natural', padTap: 'tap', padGestures: 'gestures', padPinch: 'pinch' };
const fill = input => input.style.setProperty('--fill', ((input.value - input.min) / (input.max - input.min) * 100) + '%');

/** The settings of the PC on screen (until 5.0.406 they were the same for every PC: those are where it starts). */
export function padSettings() {
  if (!ctx) return;
  Object.assign(settings, DEFAULTS, ctx.store.get('pad', {}), ctx.pcStore.get('pad', {}));
  for (const [id, key] of Object.entries(RANGES)) {
    const input = document.getElementById(id);
    input.value = settings[key];
    fill(input);
  }
  for (const [id, key] of Object.entries(SWITCHES)) document.getElementById(id).checked = settings[key];
  document.getElementById('padKeysText').textContent = keyList().map(keyLabel).join('  ');
  buildKeys();
}

function savePad() {
  ctx.pcStore.set('pad', settings);
}

function wireSettings() {
  const save = () => {
    for (const [id, key] of Object.entries(RANGES)) {
      const input = document.getElementById(id);
      settings[key] = Number(input.value);
      fill(input);
    }
    for (const [id, key] of Object.entries(SWITCHES)) settings[key] = document.getElementById(id).checked;
    savePad();
  };
  for (const id of [...Object.keys(RANGES), ...Object.keys(SWITCHES)]) document.getElementById(id).addEventListener('input', save);
  document.getElementById('padKeysEdit').addEventListener('click', openKeysEditor);
}

/** The keys of the row, in order: the ones chosen, or all. */
function keyList() {
  const known = new Set(NAV_KEYS.map(([key]) => key));
  const chosen = Array.isArray(settings.keys) ? settings.keys.filter(k => known.has(k)) : null;
  return chosen ?? NAV_KEYS.map(([key]) => key);
}

function keyLabel(key) {
  const label = NAV_KEYS.find(([k]) => k === key)?.[1] ?? key;
  return label.startsWith('pwa.') ? ctx.t(label) : label;
}

/** Which special keys the row has and in which order: tap one in the row to take it out, another to add it at the end. */
function openKeysEditor() {
  buzz(5);
  ctx.openSheet(box => {
    box.append(Object.assign(document.createElement('h2'), { textContent: ctx.t('pwa.pad.keys') }));
    const hint = Object.assign(document.createElement('p'), { className: 'sheet-hint', textContent: ctx.t('pwa.pad.keysHint') });
    const inRow = document.createElement('div');
    const others = document.createElement('div');
    inRow.className = others.className = 'key-chips';
    const othersTitle = Object.assign(document.createElement('h3'), { className: 'section-title', textContent: ctx.t('pwa.pad.keysMore') });
    const reset = Object.assign(document.createElement('button'), { className: 'btn', textContent: ctx.t('pwa.pad.keysReset') });
    const chip = (key, on) => {
      const b = document.createElement('button');
      b.className = 'key-chip' + (on ? ' on' : '');
      b.textContent = keyLabel(key);
      b.addEventListener('click', () => {
        buzz(5);
        const list = keyList();
        settings.keys = on ? list.filter(k => k !== key) : [...list, key];
        changed();
      });
      return b;
    };
    const draw = () => {
      const list = keyList();
      inRow.replaceChildren(...list.map(k => chip(k, true)));
      const rest = NAV_KEYS.map(([k]) => k).filter(k => !list.includes(k));
      others.replaceChildren(...rest.map(k => chip(k, false)));
      othersTitle.hidden = rest.length === 0;
      reset.disabled = settings.keys === null;
    };
    const changed = () => {
      savePad();
      draw();
      padSettings();
    };
    reset.addEventListener('click', () => {
      settings.keys = null;
      changed();
    });
    const buttons = document.createElement('div');
    buttons.className = 'sheet-buttons';
    buttons.append(reset);
    box.append(hint, inRow, othersTitle, others, buttons);
    draw();
  });
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
  buildVk();
  wheelScrollsSideways(document.getElementById('keysNav'));
  wireSettings();
  padSettings();
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
  landscape.addEventListener('change', updateFull);
  const kbButton = document.getElementById('keyboardButton');
  // Cancelling pointerdown keeps the focus in the text box; the keyboard itself only opens from a click.
  kbButton.addEventListener('pointerdown', e => e.preventDefault());
  kbButton.addEventListener('click', toggleKeyboard);
  // The zoom around the mouse («Lupa»): on the PC's main monitor, it follows the pointer.
  document.getElementById('zoomButton').addEventListener('click', () => { buzz(5); send({ t: 'z', a: 'toggle' }); });
  document.getElementById('zoomIn').addEventListener('click', () => { buzz(5); send({ t: 'z', a: 'in' }); });
  document.getElementById('zoomOut').addEventListener('click', () => { buzz(5); send({ t: 'z', a: 'out' }); });
  // Inside the pad: touching them is not moving the pointer.
  for (const type of ['pointerdown', 'pointermove', 'pointerup']) document.getElementById('zoomSteps').addEventListener(type, e => e.stopPropagation());
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

/** The PC says how the zoom is (on, level): the button lights up and − / + show. */
function showZoom(on, level) {
  document.getElementById('zoomButton').classList.toggle('active', on);
  document.getElementById('zoomSteps').hidden = !on;
  document.getElementById('zoomText').textContent = '×' + (Math.round(level * 10) / 10).toLocaleString();
}

/** Called when the tab opens or closes, and when the session says whether the PC allows it. */
export function padActive(on, allowed = true) {
  padShown = on && allowed;
  updateFull();
  document.getElementById('padOff').hidden = allowed;
  document.getElementById('pad').hidden = !allowed;
  document.querySelector('.pad-buttons').hidden = !allowed;
  document.querySelector('.pad-bar').hidden = !allowed;
  if (!allowed) document.getElementById('keysPanel').hidden = true;
  else setKeysPanel(!!ctx.store.get('padKeys', false));
  if (noTouch()) {
    if (on && allowed) setVk(!!ctx.store.get('padVk', false));
    else document.getElementById('vk').hidden = true;
  }

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
  padSettings();
  setStatus(status);
}
