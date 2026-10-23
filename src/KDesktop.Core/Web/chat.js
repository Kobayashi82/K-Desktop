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

  const existing = line && list.querySelector(`[data-key="${CSS.escape(keyOf(line))}"]`);
  if (existing) fill(existing, line);
  else {
    list.replaceChildren();
    let day = '';
    for (const l of lines) {
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
      list.append(b);
    }
  }
  if (near || !line) list.scrollTop = list.scrollHeight;
  updateBar();
}

const keyOf = l => l.role + '/' + l.turn;

function fill(b, l) {
  b.className = 'bubble ' + (l.role === 'user' ? 'user' : 'assistant') + (l.final ? '' : ' writing');
  b.textContent = l.text;
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
    await ctx.api('assistants/say', { method: 'POST', body: { id: c.id, text } });
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
  try { await ctx.api('assistants/stop', { method: 'POST', body: { id: c.id } }); } catch { /* offline */ }
}

/** Said with this tab's microphone: the answer comes as for something written. */
export function chatAsked(id) {
  c.asked.set(id, Date.now());
}
