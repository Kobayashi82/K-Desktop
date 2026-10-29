// What K-Desktop sends to this device (B11, «Enviar al móvil»): fetched from api/inbox when the PC says «inbox» (and on
// opening), kept here until dismissed, and acknowledged so it is not given again. In the Android app its service may
// have shown (and acknowledged) some already as notifications: they come from KDesktopApp.takeInbox().
let ctx = null;
const KEEP = 30;

export function setupInbox(context) {
  ctx = context;
  ctx.$('inboxButton').addEventListener('click', openInbox);
  ctx.$('inboxClose').addEventListener('click', closeInbox);
  ctx.$('inboxSheet').addEventListener('click', e => { if (e.target === ctx.$('inboxSheet')) closeInbox(); });
  ctx.$('inboxClear').addEventListener('click', () => {
    save([]);
    render();
    closeInbox();
  });
  window.kdOpenInbox = () => {
    takeFromApp();
    openInbox();
  };
  wireBackground();
  takeFromApp();
  render();
}

function items() { return ctx.store.get('inbox', []); }

function save(list) { ctx.store.set('inbox', list.slice(-KEEP)); }

function add(messages) {
  const list = items();
  const known = new Set(list.map(m => m.id));
  const fresh = messages.filter(m => m && m.id && !known.has(m.id));
  if (!fresh.length) return [];
  save([...list, ...fresh]);
  render();
  return fresh;
}

/** The ones the app's service already showed as notifications. */
function takeFromApp() {
  try {
    const json = window.KDesktopApp?.takeInbox?.();
    if (json) add(JSON.parse(json));
  } catch { /* an older app */ }
}

/** Asks K-Desktop what is waiting, shows it and says it arrived. */
export async function fetchInbox() {
  if (!ctx) return;
  takeFromApp();
  let data;
  try { data = await ctx.api('inbox'); } catch { return; }
  const messages = data.messages ?? [];
  if (!messages.length) return;
  const fresh = add(messages);
  try { await ctx.api('inbox/ack', { method: 'POST', body: { ids: messages.map(m => m.id) } }); } catch { /* next time */ }
  for (const m of fresh.slice(-2)) {
    navigator.vibrate?.([10, 60, 10]);
    ctx.toast('info', m.title || ctx.t('pwa.inbox.from', m.from), m.title ? m.text : m.text || m.url || m.file?.name || '', 5000, openInbox);
  }
}

function openInbox() {
  render();
  ctx.$('inboxSheet').hidden = false;
}

function closeInbox() { ctx.$('inboxSheet').hidden = true; }

function render() {
  const list = items();
  const button = ctx.$('inboxButton');
  button.hidden = list.length === 0;
  button.dataset.count = String(list.length);
  const box = ctx.$('inboxList');
  box.replaceChildren();
  for (const m of [...list].reverse()) box.append(card(m));
  ctx.$('inboxEmpty').hidden = list.length > 0;
}

function card(m) {
  const { el, t, svg } = ctx;
  const c = el('div', 'inbox-card card');
  const head = el('div', 'inbox-head');
  head.append(el('b', '', m.title || t('pwa.inbox.from', m.from)));
  const when = new Date(m.at);
  head.append(el('span', 'muted small', isNaN(when) ? '' : when.toLocaleString(ctx.lang(), { dateStyle: 'short', timeStyle: 'short' })));
  const close = el('button', 'icon-btn small');
  close.innerHTML = svg('error');
  close.title = t('pwa.inbox.dismiss');
  close.addEventListener('click', () => {
    save(items().filter(x => x.id !== m.id));
    render();
  });
  head.append(close);
  c.append(head);
  if (m.title && m.from) c.append(el('span', 'muted small', t('pwa.inbox.from', m.from)));
  if (m.text) c.append(el('p', 'inbox-text', m.text));
  const actions = el('div', 'inbox-actions');
  if (m.text) actions.append(button('copy', t('pwa.inbox.copy'), () => copy(m.text)));
  if (m.url) {
    c.append(el('p', 'inbox-url muted small', m.url));
    actions.append(button('link', t('pwa.inbox.open'), () => openLink(m.url)));
  }
  if (m.file) actions.append(button('install', t('pwa.inbox.download', m.file.name), () => download(m)));
  if (actions.children.length) c.append(actions);
  return c;
}

function button(icon, label, run) {
  const b = ctx.el('button', 'btn small');
  b.innerHTML = ctx.svg(icon);
  b.append(ctx.el('span', '', label));
  b.addEventListener('click', run);
  return b;
}

async function copy(text) {
  let ok = false;
  if (window.KDesktopApp?.copy) {
    window.KDesktopApp.copy(text);
    ok = true;
  } else {
    try {
      await navigator.clipboard.writeText(text);
      ok = true;
    } catch {
      // Over plain HTTP there is no clipboard API: the old way.
      const area = document.createElement('textarea');
      area.value = text;
      document.body.append(area);
      area.select();
      ok = document.execCommand('copy');
      area.remove();
    }
  }
  ctx.toast(ok ? 'ok' : 'error', t2(ok ? 'pwa.inbox.copied' : 'pwa.inbox.copyFailed'), '', 1800);
}

const t2 = key => ctx.t(key);

function openLink(url) {
  const a = document.createElement('a');
  a.href = url;
  a.target = '_blank';
  a.rel = 'noopener';
  a.click();
}

async function download(m) {
  const path = `api/inbox/file/${encodeURIComponent(m.id)}`;
  // The app saves it with Android's downloads (a web view cannot save a blob).
  if (window.KDesktopApp?.download) {
    window.KDesktopApp.download(new URL(path, location.href).href, m.file.name);
    ctx.toast('info', t2('pwa.inbox.downloading'), m.file.name, 2500);
    return;
  }
  try {
    const headers = {};
    const token = ctx.store.get('token');
    if (token) headers.Authorization = 'Bearer ' + token;
    const r = await fetch(path, { headers, credentials: 'same-origin', cache: 'no-store' });
    if (!r.ok) throw new Error(String(r.status));
    const url = URL.createObjectURL(await r.blob());
    const a = document.createElement('a');
    a.href = url;
    a.download = m.file.name;
    document.body.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  } catch {
    ctx.toast('error', t2('pwa.inbox.downloadFailed'), m.file.name, 3500);
  }
}

// ── the Android app's service: messages with the app closed ──────────

function wireBackground() {
  const app = window.KDesktopApp;
  const row = ctx.$('inboxBackgroundRow');
  if (!app?.setInbox || !app?.inboxEnabled) {
    row.hidden = true;
    return;
  }
  row.hidden = false;
  const box = ctx.$('inboxBackground');
  box.checked = !!app.inboxEnabled();
  box.addEventListener('change', () => {
    app.setInbox(box.checked);
    // Android may refuse the notifications: the app says so when it is asked again.
    setTimeout(() => { box.checked = !!app.inboxEnabled(); }, 1500);
  });
  window.kdInboxState = on => { box.checked = !!on; };
}
