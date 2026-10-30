// The agenda on the phone (calendar, phase 9): the citas of the PC's calendars by days (api/calendar), and new,
// change and delete in a sheet. The PC says «calendar» in the event stream when something changed.
let ctx = null;
let data = null;
let days = 14;
let active = false;
let loading = false;

const SCOPES = ['this', 'following', 'all'];

export function setupAgenda(context) {
  ctx = context;
  ctx.$('agendaNew').addEventListener('click', () => openEditor(null));
  ctx.$('agendaMore').addEventListener('click', () => {
    days += 14;
    load();
  });
}

/** The tab is on screen (or not): it reads the agenda when it shows. */
export function agendaActive(on) {
  active = on;
  if (on) load();
}

/** The PC says the calendar changed. */
export function onCalendarEvent() {
  if (active) load();
}

async function load() {
  if (loading) return;
  loading = true;
  try {
    data = await ctx.api(`calendar?from=${isoDay(new Date())}&days=${days}`);
    ctx.store.set('agenda', data);
  } catch {
    data = data ?? ctx.store.get('agenda', null);
  } finally {
    loading = false;
  }
  render();
}

function isoDay(d) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function lang() { return ctx.lang(); }

/** «Hoy», «Mañana» or «jueves, 5 nov». */
function dayTitle(iso) {
  const today = isoDay(new Date());
  const tomorrow = isoDay(new Date(Date.now() + 86400000));
  if (iso === today) return ctx.t('calendar.today');
  if (iso === tomorrow) return ctx.t('calendar.tomorrow');
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(lang(), { weekday: 'long', day: 'numeric', month: 'short' });
}

function calendarOf(item) { return data?.calendars?.find(c => c.key === item.calendar); }

function hours(item) {
  if (item.allDay) return ctx.t('calendar.allDay');
  const start = item.start.slice(11, 16);
  const end = item.end.slice(11, 16);
  return item.end.slice(0, 10) === item.start.slice(0, 10) ? `${start} – ${end}` : start;
}

function render() {
  const list = ctx.$('agendaList');
  list.replaceChildren();
  const items = data?.items ?? [];
  ctx.$('agendaEmpty').hidden = items.length > 0;
  ctx.$('agendaNew').hidden = !(data?.calendars ?? []).some(c => c.writable);
  const byDay = new Map();
  for (const item of items) {
    const day = item.start.slice(0, 10) < isoDay(new Date()) ? isoDay(new Date()) : item.start.slice(0, 10);
    if (!byDay.has(day)) byDay.set(day, []);
    byDay.get(day).push(item);
  }
  for (const [day, dayItems] of [...byDay.entries()].sort()) {
    list.append(ctx.el('h3', 'section-title', dayTitle(day)));
    const group = ctx.el('div', 'group');
    for (const item of dayItems) group.append(row(item));
    list.append(group);
  }
}

function row(item) {
  const button = ctx.el('button', 'row row-button agenda-row');
  const bar = ctx.el('span', 'agenda-bar');
  bar.style.background = calendarOf(item)?.color ?? 'var(--accent)';
  const label = ctx.el('span', 'row-label', item.title || ctx.t('agenda.untitled'));
  const sub = [hours(item), item.location, calendarOf(item)?.name].filter(Boolean).join(' · ');
  label.append(ctx.el('span', 'row-sub', sub));
  button.append(bar, label);
  if (item.meeting) {
    const join = ctx.el('span', 'row-icon');
    join.innerHTML = ctx.svg('video');
    button.append(join);
  }
  button.addEventListener('click', () => openItem(item));
  return button;
}

/** A cita: its details, join, change and delete. */
function openItem(item) {
  navigator.vibrate?.(4);
  const writable = calendarOf(item)?.writable;
  const close = ctx.openSheet(box => {
    box.append(ctx.el('h2', '', item.title || ctx.t('agenda.untitled')));
    const day = item.start.slice(0, 10);
    box.append(ctx.el('p', '', `${dayTitle(day)} · ${hours(item)}`));
    if (item.location) box.append(ctx.el('p', '', item.location));
    if (item.notes) box.append(ctx.el('p', 'agenda-notes', item.notes));
    const buttons = ctx.el('div', 'sheet-buttons');
    if (item.meeting) {
      const join = ctx.el('button', 'btn primary', ctx.t('agenda.join'));
      join.addEventListener('click', () => window.open(item.meeting, '_blank', 'noopener'));
      buttons.append(join);
    }
    if (writable) {
      const edit = ctx.el('button', 'btn', ctx.t('agenda.edit'));
      edit.addEventListener('click', () => {
        close();
        setTimeout(() => openEditor(item), 250);
      });
      const remove = ctx.el('button', 'btn danger', ctx.t('common.delete'));
      remove.addEventListener('click', () => {
        close();
        setTimeout(() => confirmDelete(item), 250);
      });
      buttons.append(edit, remove);
    }
    box.append(buttons);
  });
}

function scopeSelect() {
  const select = ctx.el('select', 'agenda-scope');
  for (const s of SCOPES) {
    const option = ctx.el('option', '', ctx.t({ this: 'agenda.scope.this', following: 'agenda.scope.following', all: 'agenda.scope.all' }[s]));
    option.value = s;
    select.append(option);
  }
  return select;
}

function confirmDelete(item) {
  const close = ctx.openSheet(box => {
    box.append(ctx.el('h2', '', ctx.t('agenda.deleteTitle')));
    box.append(ctx.el('p', '', ctx.t('agenda.deleteText', item.title)));
    let scope = null;
    if (item.repeats) {
      scope = scopeSelect();
      box.append(field(ctx.t('agenda.scope.delete'), scope));
    }
    const buttons = ctx.el('div', 'sheet-buttons');
    const cancel = ctx.el('button', 'btn', ctx.t('common.cancel'));
    cancel.addEventListener('click', () => close());
    const remove = ctx.el('button', 'btn danger', ctx.t('common.delete'));
    remove.addEventListener('click', async () => {
      remove.disabled = true;
      if (await send({ op: 'delete', key: item.key, scope: scope?.value ?? 'this' })) close();
      remove.disabled = false;
    });
    buttons.append(cancel, remove);
    box.append(buttons);
  });
}

function field(label, input) {
  const wrap = ctx.el('label', 'field agenda-field');
  wrap.append(ctx.el('span', '', label), input);
  return wrap;
}

function input(type, value) {
  const i = document.createElement('input');
  i.type = type;
  if (value !== undefined && value !== null) i.value = value;
  return i;
}

const REMINDERS = [-1, 0, 5, 10, 15, 30, 60, 1440];

/** New (<item> null) or changing one. */
function openEditor(item) {
  const now = new Date();
  now.setHours(now.getHours() + 1, 0, 0, 0);
  const startDay = item ? item.start.slice(0, 10) : isoDay(now);
  const startTime = item && !item.allDay ? item.start.slice(11, 16) : `${String(now.getHours()).padStart(2, '0')}:00`;
  const endTime = item && !item.allDay ? item.end.slice(11, 16) : `${String((now.getHours() + 1) % 24).padStart(2, '0')}:00`;
  const close = ctx.openSheet(box => {
    box.append(ctx.el('h2', '', ctx.t(item ? 'agenda.dialogEdit' : 'agenda.dialogNew')));
    const title = input('text', item?.title ?? '');
    title.placeholder = ctx.t('agenda.field.titleHint');
    box.append(field(ctx.t('agenda.field.title'), title));
    const allDay = input('checkbox');
    allDay.className = 'switch';
    allDay.checked = !!item?.allDay;
    const allDayRow = ctx.el('label', 'row agenda-switch');
    allDayRow.append(ctx.el('span', 'row-label', ctx.t('agenda.field.allDay')), allDay);
    box.append(allDayRow);
    const date = input('date', startDay);
    box.append(field(ctx.t('agenda.field.start'), date));
    const from = input('time', startTime);
    const to = input('time', endTime);
    const times = ctx.el('div', 'agenda-times');
    times.append(field(ctx.t('agenda.field.start'), from), field(ctx.t('agenda.field.end'), to));
    box.append(times);
    const syncTimes = () => { times.hidden = allDay.checked; };
    allDay.addEventListener('change', syncTimes);
    syncTimes();
    const place = input('text', item?.location ?? '');
    box.append(field(ctx.t('agenda.field.location'), place));
    const notes = document.createElement('textarea');
    notes.rows = 3;
    notes.value = item?.notes ?? '';
    box.append(field(ctx.t('agenda.field.notes'), notes));
    let calendar = null;
    if (!item) {
      calendar = document.createElement('select');
      for (const c of (data?.calendars ?? []).filter(c => c.writable)) {
        const option = ctx.el('option', '', c.name);
        option.value = c.key;
        option.selected = c.key === data.defaultCalendar;
        calendar.append(option);
      }
      box.append(field(ctx.t('agenda.field.calendar'), calendar));
    }
    const reminder = document.createElement('select');
    for (const m of REMINDERS) {
      const option = ctx.el('option', '', m < 0 ? ctx.t('agenda.reminder.none') : m === 0 ? ctx.t('agenda.reminder.atStart')
        : ctx.t('agenda.reminder.before', m >= 60 ? (m % 1440 === 0 ? ctx.t('calendar.days', m / 1440) : ctx.t('calendar.hours', m / 60)) : ctx.t('calendar.minutes', m)));
      option.value = String(m);
      option.selected = (item?.reminder ?? -1) === m;
      reminder.append(option);
    }
    box.append(field(ctx.t('agenda.field.reminder'), reminder));
    let scope = null;
    if (item?.repeats) {
      scope = scopeSelect();
      box.append(field(ctx.t('agenda.scope.change'), scope));
    }
    const buttons = ctx.el('div', 'sheet-buttons');
    const cancel = ctx.el('button', 'btn', ctx.t('common.cancel'));
    cancel.addEventListener('click', () => close());
    const save = ctx.el('button', 'btn primary', ctx.t('agenda.save'));
    save.addEventListener('click', async () => {
      if (!title.value.trim()) {
        title.focus();
        return;
      }
      let end = allDay.checked ? null : `${date.value} ${to.value}`;
      if (end && to.value < from.value) {
        const next = new Date(date.value);
        next.setDate(next.getDate() + 1);
        end = `${isoDay(next)} ${to.value}`;
      }
      const cita = {
        title: title.value.trim(),
        start: allDay.checked ? date.value : `${date.value} ${from.value}`,
        end,
        allDay: allDay.checked,
        location: place.value,
        notes: notes.value,
        reminder: Number(reminder.value),
        calendar: calendar?.value,
      };
      save.disabled = true;
      const ok = item ? await send({ op: 'change', key: item.key, scope: scope?.value ?? 'this', cita }) : await send({ op: 'add', cita });
      save.disabled = false;
      if (ok) close();
    });
    buttons.append(cancel, save);
    box.append(buttons);
  });
}

async function send(body) {
  try {
    await ctx.api('calendar', { method: 'POST', body });
    navigator.vibrate?.(8);
    load();
    return true;
  } catch (e) {
    ctx.toast('error', ctx.t('agenda.errorTitle'), e.data?.message ?? '', 4000);
    return false;
  }
}
