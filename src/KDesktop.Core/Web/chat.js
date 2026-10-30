// The assistants from the phone: the same conversations as Panel → Assistant on the PC. An order written here (or said
// with the microphone of this tab) goes to the chosen assistant; its answer arrives line by line through the event
// stream (event "chat"), and away from this tab it comes as a toast. Only the active ones (on the desktop) are listed.

let ctx;
const c = {
  enabled: false,      // the module is on: the tab is shown
  active: false,       // this tab is on screen
  list: [],
  id: null,            // the chosen assistant
  chats: new Map(),    // id → lines [{ role, turn, text, final, at }]
  did: new Map(),      // id → what it did [{ text, ok, at }]: chips under the answer of that moment
  loaded: new Set(),   // conversations read from the PC (the rest only have what came by events)
  asked: new Map(),    // id → when this phone last asked it something
  sending: false,
  listTimer: 0,
};

const $ = id => document.getElementById(id);
const ASKED_TOAST = 3 * 60 * 1000;

export function setupChat(deps) {
  ctx = deps;
  c.id = ctx.store.get('assistant', null);
  const input = $('chatInput');
  $('chatForm').addEventListener('submit', e => {
    e.preventDefault();
    send();
  });
  input.addEventListener('keydown', e => {
    // Enter sends, Shift+Enter is a new line (the phone's «send» key sends too).
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
      e.preventDefault();
      send();
    }
  });
  input.addEventListener('input', () => {
    grow();
    updateBar();
  });
  // Typing on a phone: the tab bar makes way for the keyboard.
  const touch = matchMedia('(pointer: coarse)');
  input.addEventListener('focus', () => document.body.classList.toggle('chat-typing', touch.matches));
  input.addEventListener('blur', () => document.body.classList.remove('chat-typing'));
  $('chatStop').addEventListener('click', stop);
  $('chatMic').addEventListener('click', () => {
    if (c.id) ctx.talk(c.id);
  });
}

/** The module is on or off (session and list say it): its tab comes and goes. */
export function chatEnabled(on) {
  on = !!on;
  if (on === c.enabled) return;
  c.enabled = on;
  ctx.showTab(on);
  if (!on) {
    c.chats.clear();
    c.loaded.clear();
  }
}

/** The microphone of this tab: when the PC understands voice. */
export function chatVoice(on) {
  $('chatMic').hidden = !on;
}

/** The language changed: the texts that name the assistant. */
export function chatTexts() {
  if (ctx && c.active) render();
}

/** The tab is shown or left. */
export function chatActive(on) {
  c.active = on;
  if (!on) {
    document.body.classList.remove('chat-typing');
    return;
  }
  loadList();
}

/** The live stream came back (or the app is visible again): what was missed. */
export function chatResync() {
  c.loaded.clear();
  if (c.active) loadList();
}

/** Event "assistants": who is active or busy changed. A little later, several come together. */
export function onAssistants() {
  clearTimeout(c.listTimer);
  c.listTimer = setTimeout(() => {
    if (c.active) loadList(false);
    else refreshEnabled();
  }, 250);
}

/** Event "did": it did something (opened a program, set the volume…): a chip under its answer. */
export function onDid(data) {
  const id = data?.assistant;
  if (!id || !data.did) return;
  const list = c.did.get(id) ?? [];
  list.push(data.did);
  if (list.length > 60) list.splice(0, list.length - 60);
  c.did.set(id, list);
  if (c.active && id === c.id) render();
}

/** Each answer gets what was done between the order before it and the next one. */
function attach(lines, did) {
  let from = -Infinity;
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i];
    if (l.role === 'user') {
      from = Date.parse(l.at);
      continue;
    }
    const next = lines.slice(i + 1).find(x => x.role === 'user');
    const until = next ? Date.parse(next.at) : Infinity;
    // Several answers to one order: only the last one shows them.
    const later = lines.slice(i + 1).find(x => x.role !== 'user' || x === next);
    l.did = later && later.role !== 'user' ? [] : did.filter(d => Date.parse(d.at) >= from - 1000 && Date.parse(d.at) < until);
  }
}

/** Event "chat": a line is new or grew; null = the conversation was cleared. */
export function onChatLine(data) {
  const id = data?.assistant;
  if (!id) return;
  if (!data.line) {
    c.chats.set(id, []);
    if (c.active && id === c.id) render();
    return;
  }
  const lines = c.chats.get(id) ?? [];
  c.chats.set(id, lines);
  const line = data.line;
  const same = lines.find(l => l.turn === line.turn && l.role === line.role);
  if (same) {
    // Events can overtake one another: never go back from the final text.
    if (same.final && !line.final) return;
    Object.assign(same, line);
  } else lines.push(line);
  if (lines.length > 100) lines.splice(0, lines.length - 100);

  if (c.active && id === c.id) render(line);
  else if (line.role === 'assistant' && line.final && line.text && Date.now() - (c.asked.get(id) ?? 0) < ASKED_TOAST) {
    const who = c.list.find(a => a.id === id)?.name ?? id;
    ctx.toast('info', who, line.text, Math.min(15000, 3000 + line.text.length * 60), () => {
      c.id = id;
      ctx.store.set('assistant', id);
      ctx.openTab();
    });
  }
  updateBar();
}

async function refreshEnabled() {
  try {
    const r = await ctx.api('assistants');
    c.list = r.assistants ?? [];
    chatEnabled(r.enabled);
  } catch { /* offline: next time */ }
}

async function loadList(withChat = true) {
  try {
    const r = await ctx.api('assistants');
    chatEnabled(r.enabled);
    c.list = r.assistants ?? [];
    if (!c.list.some(a => a.id === c.id)) c.id = c.list[0]?.id ?? null;
    renderChips();
    if (c.id && (withChat || !c.loaded.has(c.id))) await loadChat(c.id);
    else render();
  } catch (e) {
    if (!e?.unpaired) render();
  }
}

async function loadChat(id) {
  try {
    const r = await ctx.api('assistants/chat?id=' + encodeURIComponent(id));
    c.chats.set(id, r.lines ?? []);
    c.did.set(id, r.did ?? []);
    c.loaded.add(id);
  } catch { /* keep what came by events */ }
  if (id === c.id) render();
}

function renderChips() {
  const box = $('assistantChips');
  box.replaceChildren();
  // With one there is nothing to choose: its name is enough in the box's placeholder.
  box.hidden = c.list.length < 2;
  for (const a of c.list) {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'chip' + (a.id === c.id ? ' active' : '') + (a.busy ? ' busy' : '');
    if (a.icon) {
      const img = document.createElement('img');
      img.src = 'api/icon/' + a.icon + '.png';
      img.alt = '';
      chip.append(img);
    }
    chip.append(document.createTextNode(a.name), document.createElement('i'));
    chip.addEventListener('click', () => {
      if (a.id === c.id) return;
      navigator.vibrate?.(4);
      c.id = a.id;
      ctx.store.set('assistant', a.id);
      renderChips();
      if (c.loaded.has(a.id)) render();
      else loadChat(a.id);
    });
    box.append(chip);
  }
}

/** The conversation; with <line>, only that bubble changes (the answer as it is written). */
function render(line) {
  const list = $('chatList');
  const near = list.scrollHeight - list.scrollTop - list.clientHeight < 80;
  const assistant = c.list.find(a => a.id === c.id);
  const empty = $('chatEmpty');
  $('chatForm').hidden = !assistant;
  if (!assistant) {
    list.replaceChildren();
    list.hidden = true;
    empty.hidden = false;
    empty.textContent = ctx.t('pwa.chat.nobody');
    return;
  }
  $('chatInput').placeholder = ctx.t('pwa.chat.placeholder', assistant.name);
  const lines = c.chats.get(c.id) ?? [];
  empty.hidden = lines.length > 0;
  list.hidden = lines.length === 0;
  if (!lines.length) empty.textContent = ctx.t('pwa.chat.empty', assistant.name);

  attach(lines, c.did.get(c.id) ?? []);
  const existing = line && list.querySelector(`[data-key="${CSS.escape(keyOf(line))}"]`);
  if (existing) fill(existing, line);
  else {
    list.replaceChildren();
    let day = '';
    lines.forEach((l, index) => {
      const d = new Date(l.at).toLocaleDateString(ctx.lang());
      if (d !== day) {
        day = d;
        const tag = document.createElement('p');
        tag.className = 'day';
        tag.textContent = d === new Date().toLocaleDateString(ctx.lang()) ? ctx.t('pwa.chat.today') : d;
        list.append(tag);
      }
      const b = document.createElement('div');
      b.dataset.key = keyOf(l);
      fill(b, l);
      if (l.role === 'user') list.append(b);
      else {
        // Its picture by the last of its answers in a row, like a chat app.
        const row = document.createElement('div');
        row.className = 'chat-row';
        const avatar = document.createElement('span');
        avatar.className = 'chat-avatar';
        if (lines[index + 1]?.role !== 'assistant' && assistant.icon) {
          const img = new Image();
          img.alt = '';
          img.src = 'api/icon/' + assistant.icon + '.png';
          avatar.append(img);
        }
        row.append(avatar, b);
        list.append(row);
      }
    });
  }
  if (near || !line) list.scrollTop = list.scrollHeight;
  updateBar();
}

const keyOf = l => l.role + '/' + l.turn;

const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** Inline Markdown of escaped text: code, bold, italics, struck, links (http and https only). */
function inline(s) {
  return esc(s)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*(.+?)\*\*/g, '<b>$1</b>').replace(/__(.+?)__/g, '<b>$1</b>')
    .replace(/~~(.+?)~~/g, '<s>$1</s>')
    .replace(/(^|[^\w*])\*([^*\s][^*]*?)\*(?![\w*])/g, '$1<i>$2</i>')
    .replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');
}

/** A long answer's Markdown as HTML: headings, paragraphs, lists, quotes, code, tables and rules. */
function markdown(text) {
  const lines = text.replace(/\r\n/g, '\n').split('\n');
  const out = [];
  let para = [], list = null;
  const endPara = () => { if (para.length) out.push('<p>' + inline(para.join(' ')) + '</p>'); para = []; };
  const endList = () => { if (list) out.push('</' + list + '>'); list = null; };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i], t = line.trim();
    if (t.startsWith('```')) {
      endPara(); endList();
      const code = [];
      while (++i < lines.length && !lines[i].trim().startsWith('```')) code.push(lines[i]);
      out.push('<pre>' + esc(code.join('\n')) + '</pre>');
      continue;
    }
    if (!t) { endPara(); continue; }
    let m;
    if ((m = t.match(/^(#{1,6})\s+(.*)$/))) { endPara(); endList(); const h = Math.min(6, m[1].length + 2); out.push(`<h${h}>${inline(m[2])}</h${h}>`); continue; }
    if (/^([-*_])(\s*\1){2,}$/.test(t)) { endPara(); endList(); out.push('<hr>'); continue; }
    if ((m = line.match(/^\s*([-*+•]|\d{1,3}[.)])\s+(.*)$/))) {
      endPara();
      const kind = /\d/.test(m[1]) ? 'ol' : 'ul';
      if (list !== kind) { endList(); list = kind; out.push('<' + kind + '>'); }
      out.push('<li>' + inline(m[2]) + '</li>');
      continue;
    }
    if (t.startsWith('>')) { endPara(); endList(); out.push('<blockquote>' + inline(t.replace(/^>\s?/, '')) + '</blockquote>'); continue; }
    if (t.startsWith('|')) {
      endPara(); endList();
      const rows = [];
      for (; i < lines.length && lines[i].trim().startsWith('|'); i++) {
        const row = lines[i].trim().replace(/^\||\|$/g, '');
        if (!row.replace(/[|\s:-]/g, '')) continue;
        rows.push(row.split('|').map(c => c.trim()));
      }
      i--;
      out.push('<table>' + rows.map((r, n) => '<tr>' + r.map(c => (n ? '<td>' : '<th>') + inline(c) + (n ? '</td>' : '</th>')).join('') + '</tr>').join('') + '</table>');
      continue;
    }
    endList();
    para.push(t);
  }
  endPara(); endList();
  return out.join('');
}

function fill(b, l) {
  b.className = 'bubble ' + (l.role === 'user' ? 'user' : 'assistant') + (l.final ? '' : ' writing');
  b.textContent = l.text;
  // A long answer (stage 14.2): its summary, then the whole text.
  if (l.role !== 'user' && l.written) {
    b.classList.add('long');
    const body = document.createElement('div');
    body.className = 'md';
    body.innerHTML = markdown(l.written);
    b.append(body);
  }
  // Its pictures (the ones given with the order, or the ones of the answer), by their key; a tap opens them whole.
  if (l.images?.length && c.id) {
    const pics = document.createElement('div');
    pics.className = 'pics';
    for (const key of l.images) {
      const url = 'api/assistants/image?id=' + encodeURIComponent(c.id) + '&k=' + encodeURIComponent(key);
      const link = document.createElement('a');
      link.href = url;
      link.target = '_blank';
      link.rel = 'noopener';
      const img = document.createElement('img');
      img.src = url;
      img.loading = 'lazy';
      img.alt = '';
      // Gone from the PC: nothing is left of it.
      img.addEventListener('error', () => link.remove());
      link.append(img);
      pics.append(link);
    }
    b.append(pics);
  }
  if (l.did?.length) {
    const chips = document.createElement('div');
    chips.className = 'did';
    for (const d of l.did) {
      const chip = document.createElement('span');
      chip.className = 'did-chip' + (d.ok ? '' : ' failed');
      chip.textContent = d.text;
      chips.append(chip);
    }
    b.append(chips);
  }
  if (l.final) {
    const time = document.createElement('small');
    time.textContent = new Date(l.at).toLocaleTimeString(ctx.lang(), { hour: '2-digit', minute: '2-digit' });
    b.append(time);
  }
}

/** Send or stop: stop while the chosen one is thinking or answering. */
function updateBar() {
  const assistant = c.list.find(a => a.id === c.id);
  const lines = c.chats.get(c.id) ?? [];
  const last = lines[lines.length - 1];
  const busy = !!assistant?.busy || (last && !last.final);
  const typed = $('chatInput').value.trim().length > 0;
  $('chatStop').hidden = !busy || typed;
  $('chatSend').hidden = busy && !typed;
  $('chatSend').disabled = !typed || c.sending;
}

function grow() {
  const input = $('chatInput');
  input.style.height = 'auto';
  input.style.height = Math.min(input.scrollHeight, 120) + 'px';
}

async function send() {
  const input = $('chatInput');
  const text = input.value.trim();
  if (!text || !c.id || c.sending) return;
  c.sending = true;
  updateBar();
  navigator.vibrate?.(6);
  try {
    await ctx.api('assistants/say', { method: 'POST', body: { id: c.id, text, speak: ctx.speak() } });
    c.asked.set(c.id, Date.now());
    input.value = '';
    grow();
  } catch (e) {
    if (!e?.unpaired) {
      const busy = e?.status === 409;
      ctx.toast(busy ? 'warning' : 'error', busy ? ctx.t('pwa.chat.notActive') : ctx.t('pwa.offline'), '', 3500);
      if (busy) loadList(false);
    }
  } finally {
    c.sending = false;
    updateBar();
  }
}

async function stop() {
  if (!c.id) return;
  navigator.vibrate?.(8);
  ctx.stopSpeech(c.id);
  try { await ctx.api('assistants/stop', { method: 'POST', body: { id: c.id } }); } catch { /* offline */ }
}

/** «Hablar» (a shortcut of the app): to the chosen assistant, in its tab; false when there is none. */
export function chatTalk() {
  if (!c.enabled || !c.id) return false;
  ctx.openTab();
  ctx.talk(c.id);
  return true;
}

/** Said with this tab's microphone: the answer comes as for something written. */
export function chatAsked(id) {
  c.asked.set(id, Date.now());
}
