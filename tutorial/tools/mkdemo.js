// mkdemo.js <dataDir> <knowledgeFolder> <mcpScript> <demoModels> [en|es]: a demo data folder for the tutorial screenshots,
// in English or Spanish, dark theme. Realistic commands, menus, macros, K-Board, events, history, stats, memory and notes for a made-up
// user «Alex». From the real data (%APPDATA%\K-Desktop 5) only the Lander's game progress and look, the stats counters
// (unlocked achievements), the wake words and hard links to the voice models are taken; never config.json.
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');
const [data, knowledge, mcpScript, demoModels, lang = 'en'] = process.argv.slice(2);
const es = lang === 'es';
/** The text of the demo in its language. */
const T = (en, sp) => (es ? sp : en);
const real = path.join(process.env.APPDATA, 'K-Desktop 5');
fs.mkdirSync(data, { recursive: true });

let n = 0;
const id = () => (0x5a10c0de0000 + (++n) * 7919).toString(16).slice(-12);
const ids = {};
const cmd = (key, name, actions, extra = {}) => {
  ids[key] = id();
  return { id: ids[key], name, enabled: true, remote: false, blockKey: true, ignoreDisabledApps: false, hidden: false,
    hotkeys: [], processes: [], voicePhrases: [], actions: actions.map(a => ({ enabled: true, ...a })), ...extra };
};
const chrome = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const code = path.join(process.env.LOCALAPPDATA, 'Programs', 'Microsoft VS Code', 'Code.exe');

const commands = [
  cmd('morning', T('Morning routine', 'Rutina de la mañana'), [
    { type: 'runApp', path: chrome, arguments: 'https://mail.google.com https://calendar.google.com' },
    { type: 'wait', seconds: 2 },
    { type: 'window', target: 'process', processName: 'chrome', operation: 'snap', snap: 'leftTwoThirds' },
    { type: 'runApp', path: 'C:\\Program Files\\AIMP\\AIMP.exe' },
    { type: 'volume', target: 'system', mode: 'set', value: 35, showOsd: true },
    { type: 'showMessage', text: T('Good morning!', '¡Buenos días!'), seconds: 3 },
  ], { hotkeys: ['CTRL + ALT + G'], voicePhrases: T(['good morning', 'start my day'], ['buenos días', 'empieza mi día']), notes: T('Mail, calendar and some music to start the day.', 'Correo, calendario y algo de música para empezar el día.') }),
  cmd('chrome', 'Chrome', [{ type: 'runApp', path: chrome }], { hotkeys: ['CTRL + ALT + C'], voicePhrases: T(['open the browser'], ['abre el navegador']) }),
  cmd('code', 'VS Code', [{ type: 'runApp', path: code }], { hotkeys: ['CTRL + ALT + V'] }),
  cmd('steam', 'Steam', [{ type: 'runApp', path: 'C:\\Program Files (x86)\\Steam\\steam.exe' }]),
  cmd('vlc', 'VLC', [{ type: 'runApp', path: 'C:\\Program Files\\VideoLAN\\VLC\\vlc.exe' }]),
  cmd('calc', T('Calculator', 'Calculadora'), [{ type: 'runApp', path: 'C:\\Windows\\System32\\calc.exe' }], { voicePhrases: T(['calculator'], ['calculadora']) }),
  cmd('downloads', T('Downloads', 'Descargas'), [{ type: 'openFolder', special: 'Downloads', path: '' }], { hotkeys: ['CTRL + ALT + J'], voicePhrases: T(['open downloads'], ['abre descargas']) }),
  cmd('documents', T('Documents', 'Documentos'), [{ type: 'openFolder', special: 'Documents', path: '' }]),
  cmd('recycle', T('Recycle Bin', 'Papelera'), [{ type: 'openFolder', special: 'RecycleBin', path: '' }]),
  cmd('github', 'GitHub', [{ type: 'openUrl', url: 'https://github.com', browser: 'chrome' }]),
  cmd('youtube', 'YouTube', [{ type: 'openUrl', url: 'https://www.youtube.com', browser: 'chrome' }], { voicePhrases: T(['open youtube'], ['abre youtube']) }),
  cmd('gmail', 'Gmail', [{ type: 'openUrl', url: 'https://mail.google.com', browser: 'chrome' }]),
  cmd('left', T('Snap left', 'Ajustar a la izquierda'), [{ type: 'window', target: 'active', operation: 'snap', snap: 'leftHalf' }], { hotkeys: ['WIN + ALT + Left'] }),
  cmd('right', T('Snap right', 'Ajustar a la derecha'), [{ type: 'window', target: 'active', operation: 'snap', snap: 'rightHalf' }], { hotkeys: ['WIN + ALT + Right'] }),
  cmd('topleft', T('Top-left quarter', 'Cuarto superior izquierdo'), [{ type: 'window', target: 'active', operation: 'snap', snap: 'topLeft' }], { hotkeys: ['WIN + ALT + NumPad7'] }),
  cmd('center', T('Center window', 'Centrar ventana'), [{ type: 'window', target: 'active', operation: 'center' }], { hotkeys: ['WIN + ALT + C'] }),
  cmd('monitor', T('Move to next monitor', 'Al otro monitor'), [{ type: 'window', target: 'active', operation: 'moveToMonitor', monitor: 'next' }], { hotkeys: ['WIN + ALT + Up'] }),
  cmd('ontop', T('Always on top', 'Siempre visible'), [{ type: 'topMost' }], { hotkeys: ['CTRL + ALT + X'] }),
  cmd('shot', T('Screenshot region', 'Capturar una zona'), [{ type: 'screenshot', area: 'region', copy: true, save: true, open: false, showOsd: true }], { hotkeys: ['CTRL + PrintScreen'] }),
  cmd('ocr', T('Copy text from screen', 'Copiar texto de la pantalla'), [{ type: 'screenText', area: 'region', showOsd: true }], { hotkeys: ['CTRL + ALT + E'] }),
  cmd('mute', T('Mute microphone', 'Silenciar micrófono'), [{ type: 'volume', target: 'microphone', mode: 'toggleMute', value: 0, showOsd: true }], { hotkeys: ['CTRL + ALT + M'], voicePhrases: T(['mute the mic'], ['silencia el micro']) }),
  cmd('volup', T('Volume up', 'Subir volumen'), [{ type: 'volume', target: 'system', mode: 'up', value: 5, showOsd: true }], { hotkeys: ['CTRL + ALT + Up'] }),
  cmd('voldown', T('Volume down', 'Bajar volumen'), [{ type: 'volume', target: 'system', mode: 'down', value: 5, showOsd: true }], { hotkeys: ['CTRL + ALT + Down'] }),
  cmd('headphones', T('Switch to headphones', 'Cambiar a los auriculares'), [{ type: 'audioOutput', devices: [{ id: '', name: T('Headphones (Arctis 7)', 'Auriculares (Arctis 7)') }, { id: '', name: T('Speakers (Realtek)', 'Altavoces (Realtek)') }], showOsd: true }], { voicePhrases: T(['headphones'], ['auriculares']) }),
  cmd('dark', T('Dark mode', 'Modo oscuro'), [{ type: 'windowsTheme', mode: 'toggle' }], { voicePhrases: T(['dark mode', 'light mode'], ['modo oscuro', 'modo claro']) }),
  cmd('hidden', T('Show hidden files', 'Ver archivos ocultos'), [{ type: 'hiddenFiles', showOsd: true }], { hotkeys: ['CTRL + ALT + I'] }),
  cmd('cleanurl', T('Clean link in clipboard', 'Limpiar el enlace copiado'), [{ type: 'clipboard', mode: 'cleanUrl' }], { hotkeys: ['CTRL + ALT + U'] }),
  cmd('signature', T('Type my signature', 'Escribir mi firma'), [{ type: 'sendInput', mode: 'text', value: T('Best regards,\nAlex', 'Un saludo,\nAlex') }], { hotkeys: ['CTRL + ALT + N'] }),
  cmd('zoom', T('Zoom lens', 'Lupa'), [{ type: 'magnifier', mode: 'toggle', zoom: 2 }], { hotkeys: ['WIN + ALT + Z'] }),
  cmd('dictate', T('Dictate', 'Dictar'), [{ type: 'dictate', mode: 'toggle' }], { hotkeys: ['CTRL + ALT + F'] }),
  cmd('wifi', T('Toggle Wi-Fi', 'Encender o apagar el Wi-Fi'), [{ type: 'radio', radio: 'wiFi', mode: 'toggle' }]),
  cmd('nas', T('Wake up the NAS', 'Despertar el NAS'), [{ type: 'wakeOnLan', mac: '00:11:32:9A:4C:7E', host: 'vault' }], { voicePhrases: T(['wake up the nas'], ['despierta el nas']) }),
  cmd('lights', T('Living room lights off', 'Apagar las luces del salón'), [{ type: 'httpRequest', method: 'post', contentType: 'application/json', url: 'http://homeassistant.local:8123/api/services/light/turn_off', body: '{"entity_id":"light.living_room"}' }], { voicePhrases: T(['lights off'], ['apaga las luces']) }),
  cmd('monitoroff', T('Turn off monitors', 'Apagar las pantallas'), [{ type: 'display', operation: 'monitorOff' }]),
  cmd('lock', T('Lock PC', 'Bloquear el PC'), [{ type: 'power', mode: 'lock', delaySeconds: 0, showOsd: false }], { hotkeys: ['WIN + ALT + L'] }),
  cmd('sleep', T('Sleep', 'Suspender'), [{ type: 'power', mode: 'sleep', delaySeconds: 10, showOsd: true }]),
  cmd('shutdown', T('Shut down', 'Apagar'), [{ type: 'power', mode: 'shutdown', delaySeconds: 30, showOsd: true }], { voicePhrases: T(['shut down the computer'], ['apaga el ordenador']) }),
  cmd('menu', T('Main menu', 'Menú principal'), [{ type: 'quickMenu', menuId: 'MENUID' }], { hotkeys: ['CTRL + ALT + Q'] }),
  cmd('login', T('Fill in timesheet', 'Rellenar el parte de horas'), [{ type: 'playMacro', macroId: 'MACROID', repeat: 1, showOsd: true }], { hotkeys: ['CTRL + ALT + R'] }),
  cmd('gameboard', T('Gaming keyboard', 'Teclado para jugar'), [{ type: 'kboard', profileId: 'KBID', mode: 'toggle', showOsd: true }], { hotkeys: ['CTRL + ALT + W'] }),
  cmd('gaming', T('Gaming mode', 'Modo juego'), [
    { type: 'powerPlan', mode: 'set', planName: T('High performance', 'Alto rendimiento') },
    { type: 'kboard', profileId: 'KBID', mode: 'activate', showOsd: false },
    { type: 'volume', target: 'system', mode: 'set', value: 60, showOsd: false },
    { type: 'appControl', mode: 'close', processName: 'chrome', force: false },
    { type: 'showMessage', text: T('Gaming mode on', 'Modo juego activado'), seconds: 2 },
  ], { voicePhrases: T(['gaming mode', 'time to play'], ['modo juego', 'hora de jugar']) }),
  cmd('work', T('Work mode', 'Modo trabajo'), [
    { type: 'runApp', path: code },
    { type: 'runApp', path: chrome, arguments: 'https://github.com' },
    { type: 'powerPlan', mode: 'set', planName: T('Balanced', 'Equilibrado') },
    { type: 'volume', target: 'system', mode: 'set', value: 25, showOsd: false },
  ], { voicePhrases: T(['work mode'], ['modo trabajo']) }),
  cmd('askLander', T('Ask Lander', 'Preguntar a Lander'), [{ type: 'assistantAsk', assistant: 'lander', text: '' }], { hotkeys: ['CTRL + ALT + A'] }),
  cmd('landerHi', T('Lander: say hello', 'Lander: saludar'), [{ type: 'assistantSay', assistant: 'lander', text: T('Hi! Your downloads are done and the coffee break starts in 5 minutes.', '¡Hola! Tus descargas ya han terminado y el descanso para el café empieza en 5 minutos.') }]),
  cmd('freeBrain', T('Free the graphics card', 'Liberar la gráfica'), [{ type: 'assistantBrain', op: 'unload' }]),
];
// Hidden, for the clips: they only touch the demo Edge windows (process msedge) and the «ask» ones carry their text, so
// nothing depends on which window is in front (a «this window» order once moved the user's own Chrome).
const edge = { type: 'window', target: 'process', processName: 'msedge' };
for (const [t, op] of [[T('Browser to the left', 'Navegador a la izquierda'), { operation: 'snap', snap: 'leftHalf' }], [T('Browser to the right', 'Navegador a la derecha'), { operation: 'snap', snap: 'rightHalf' }],
  [T('Centre the browser', 'Centra el navegador'), { operation: 'center' }], [T('Minimize the browser', 'Minimiza el navegador'), { operation: 'minimize' }],
  [T('Maximize the browser', 'Maximiza el navegador'), { operation: 'maximize' }]]) {
  commands.push(cmd('clip ' + t, t, [{ ...edge, ...op }], { hidden: true }));
  commands.push(cmd('ask ' + t, 'Clip: ' + t, [{ type: 'assistantAsk', assistant: 'lander', text: t }], { hidden: true }));
}
const byKey = k => commands.find(c => c.id === ids[k]);

// QuickMenu
const qid = id();
const item = (key, kind = 'command') => ({ id: id(), kind, commandId: ids[key], children: [] });
const sep = () => ({ id: id(), kind: 'separator', children: [] });
const sub = (label, icon, children) => ({ id: id(), kind: 'submenu', label, icon, children });
const quickMenus = [{
  id: qid, name: T('Main menu', 'Menú principal'), size: 1, items: [
    item('chrome'), item('code'), item('steam'), sep(),
    sub(T('Windows', 'Ventanas'), 'kd:Window24:blue', [item('left'), item('right'), item('topleft'), item('center'), sep(), item('monitor'), item('ontop')]),
    sub('Web', 'kd:Globe24:green', [item('github'), item('youtube'), item('gmail')]),
    sub(T('Folders', 'Carpetas'), 'kd:Folder24:orange', [item('downloads'), item('documents'), item('recycle')]),
    sub(T('System', 'Sistema'), 'kd:Settings24:purple', [item('dark'), item('hidden'), item('monitoroff'), sep(), item('lock'), item('sleep'), item('shutdown')]),
    sep(), item('shot'), item('ocr'),
  ],
}];
byKey('menu').actions[0].menuId = qid;

// Macro: a recorded timesheet form
const mid = id();
const step = (kind, delay, extra = {}) => ({ kind, delay, vk: 0, extended: false, button: 'left', x: 0, y: 0, wheel: 0, horizontal: false, raw: false, ...extra });
const key = (vk, delay) => [step('keyDown', delay, { vk }), step('keyUp', 60, { vk })];
const macros = [{
  id: mid, name: T('Fill in timesheet', 'Rellenar el parte de horas'), notes: T('Recorded on the company portal: date, hours, project and send.', 'Grabada en el portal de la empresa: fecha, horas, proyecto y enviar.'),
  created: '2026-10-20T09:12:40+02:00', gameMode: false, steps: [
    step('click', 0, { x: 812, y: 344 }),
    step('text', 420, { text: T('Friday 24 October', 'Viernes 24 de octubre') }),
    ...key(9, 180),
    step('text', 240, { text: '8' }),
    ...key(9, 160),
    step('click', 520, { x: 905, y: 462 }),
    step('wheel', 300, { x: 905, y: 520, wheel: -240 }),
    step('click', 380, { x: 880, y: 588 }),
    ...key(9, 200),
    step('text', 260, { text: T('K-Desktop tutorial video', 'Vídeo tutorial de K-Desktop') }),
    step('pause', 800),
    step('mouseMove', 0, { x: 1210, y: 712 }),
    step('click', 140, { x: 1210, y: 712 }),
  ],
}];
byKey('login').actions[0].macroId = mid;

// K-Board: only inside a game that is not running, so nothing changes while the demo is open
const kid = id();
const kBoards = [{
  id: kid, name: T('Gaming', 'Juegos'), active: true, ignoreDisabledApps: true, processes: ['eldenring.exe'], notes: T('Only while Elden Ring is in front.', 'Solo con Elden Ring delante.'),
  remaps: [
    { from: 'CapsLock', kind: 'keys', to: 'SHIFT', enabled: true },
    { from: 'WIN', kind: 'disable', to: '', enabled: true },
    { from: 'F1', kind: 'command', to: '', commandId: ids.mute, enabled: true },
    { from: 'Mouse 4', kind: 'keys', to: 'R', enabled: true },
    { from: 'Mouse 5', kind: 'keys', to: 'F', enabled: true },
    { from: 'F12', kind: 'text', to: 'gg wp!', enabled: true },
  ],
}];
for (const c of commands) for (const a of c.actions) if (a.profileId === 'KBID') a.profileId = kid;

// Events
const ev = (name, icon, trigger, run, extra = {}) => ({ id: id(), name, icon, enabled: true, cooldownSeconds: 0, trigger,
  commands: run.map(k => ids[k]), endCommands: [], conditions: { timeFrom: '', timeTo: '', days: 'none', processRunning: '', processNotRunning: '' }, ...extra });
const events = [
  ev(T('When Elden Ring opens', 'Al abrir Elden Ring'), 'kd:Games24:purple', { type: 'processStart', processes: ['eldenring.exe'], includeRunningAtStart: false }, ['gaming'],
    { endCommands: [ids.work], notes: T('Gaming mode while it runs, back to work mode when it closes.', 'Modo juego mientras está abierto y modo trabajo al cerrarlo.') }),
  ev(T('Every weekday at 8:30', 'Laborables a las 8:30'), 'kd:Clock24:orange', { type: 'time', time: '08:30', days: 'workdays', runIfMissed: true }, ['morning']),
  ev(T('When I step away', 'Cuando me levanto'), 'kd:PersonWalking24:blue', { type: 'idle', minutes: 20 }, ['monitoroff'], { enabled: false }),
  ev(T('Headphones connected', 'Auriculares conectados'), 'kd:Headphones24:green', { type: 'audioDevice', event: 'connected', name: 'Arctis' }, ['headphones']),
  ev(T('New file in Downloads', 'Archivo nuevo en Descargas'), 'kd:ArrowDownload24:teal', { type: 'file', folder: '%USERPROFILE%\\Downloads', filter: '*.torrent', subfolders: false }, ['downloads'], { enabled: false }),
  ev(T('CPU above 90%', 'CPU por encima del 90 %'), 'kd:Gauge24:red', { type: 'load', resource: 'cpu', comparison: 'above', percent: 90, seconds: 120 }, ['landerHi'], { enabled: false }),
];

// Textos: abbreviations that become text (typed after «;»)
const snippet = (abbreviation, text, extra = {}) => ({ id: id(), abbreviation, text, enabled: true, created: '2026-10-12T10:00:00', processes: [], ...extra });
const snippets = [
  snippet('addr', T('42 Baker Street\nLondon NW1 6XE', 'Calle Mayor 42, 3.º B\n28013 Madrid')),
  snippet('mail', 'alex.morgan@example.com'),
  snippet('thx', T('Thanks a lot, I\'ll get back to you as soon as I can.', 'Muchas gracias, te contesto en cuanto pueda.')),
  snippet('meet', T('Hi! Shall we have a quick call tomorrow at 10:00? Here is the link: https://meet.example.com/alex', '¡Hola! ¿Hacemos una llamada rápida mañana a las 10:00? Este es el enlace: https://meet.example.com/alex'),
    { notes: T('For setting up calls with clients.', 'Para quedar en llamadas con clientes.') }),
  snippet('sig', T('Best regards,\nAlex Morgan\nWeb developer', 'Un saludo,\nAlex Morgan\nDesarrollador web')),
  snippet('log', 'console.log(\'%c\', \'color: orange\');', { processes: ['code'] }),
  snippet('date', T('Friday, 30 October 2026', 'Viernes, 30 de octubre de 2026'), { enabled: false }),
];

const settings = {
  language: lang, theme: 'dark', updateMode: 'off', startWithWindows: false, showWindowOnStart: true,
  windowBounds: '-1920,278,1920,1080', windowPage: 'commands', commandsMenuFilter: 'all',
  showWindowHotkey: 'CTRL + ALT + SHIFT + Home', exitHotkey: 'CTRL + ALT + SHIFT + End',
  launcherHotkey: 'CTRL + ALT + Space', launcherPosition: '0.5;0.18',
  voiceHotkey: 'Pause', voiceDuck: false, voiceMedia: false, voiceWakeListen: true, voiceListenSound: null,
  udpEnabled: true, udpPort: 2601, webEnabled: true, webPort: 2602,
  achievementNotifications: true, achievementSound: false, autoBackup: false,
  assistantEnabled: true, assistantKDesktopAdded: true,
  assistantsOnDesktop: ['kdesktop'],
  assistantSound: true, assistantVolume: 60, assistantAdorn: false, assistantAskBefore: 'risky',
  assistantVoiceEngine: 'auto', assistantVoiceKokoro: T('af_heart', 'ef_dora'), assistantVoiceVolume: 80,
  assistantBrainServers: [
    'E:\\ProjectAI\\Open WebUI\\Llama-CPP\\llama-server.exe',
    'E:\\ProjectAI\\Open WebUI\\Bonsai\\Bonsai-demo\\bin\\cuda\\llama-server.exe'],
  // Only the demo folder (hard links to models with clean names): the real folders hold names that do not belong in a video.
  assistantBrainFolders: [demoModels || 'E:\\AI Models'],
  assistantBrainKeepMinutes: 10, assistantBrainOneAtATime: true,
  assistantKnowledgeFolders: knowledge ? [knowledge] : [],
  assistantHomePlace: T('London', 'Madrid'),
  assistantTalkHotkeys: { lander: 'F9' },
  assistantTrustedTools: ['living_room_light'],
  kDesktopWakeWords: ['9afe60f69a9f'], assistantWakeWords: { lander: '97495dda6ab0', kobii: '6b9368ae8811' },
};
fs.writeFileSync(path.join(data, 'config.json'), JSON.stringify({ schemaVersion: 3, settings, commands, quickMenus, macros, kBoards, events, snippets }, null, 2));

// Agenda: two own calendars with citas around today (whatever the date), so both the list and the month have life
{
  const pad = x => String(x).padStart(2, '0');
  const at = (days, h, m = 0) => { const d = new Date(); d.setHours(0, 0, 0, 0); d.setDate(d.getDate() + days); d.setHours(h, m);
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}:00`; };
  const hour = new Date().getHours();
  let e = 0;
  const cita = (cal, title, start, end, extra = {}) => ({ id: 'demo' + (++e), uid: 'demo' + e + '@k-desktop', sourceId: 'local', calendarId: cal, title, start, end, ...extra });
  const weekday = d => ['SU', 'MO', 'TU', 'WE', 'TH', 'FR', 'SA'][new Date(Date.now() + d * 864e5).getDay()];
  const citas = [
    cita('personal', T('Dentist', 'Dentista'), at(0, Math.max(hour - 1, 0)), at(0, Math.min(hour + 1, 23), 30), { location: T('Smile Clinic', 'Clínica Sonrisa'), reminders: [30] }),
    cita('personal', T('Buy a birthday present', 'Comprar el regalo de cumpleaños'), at(0, 8), at(0, 8, 30)),
    cita('work', T('Team stand-up', 'Reunión de equipo'), at(1, 9, 30), at(1, 9, 45), { meetingUrl: 'https://meet.example.com/standup', recurrence: 'FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR' }),
    cita('work', T('Client call: Atlas project', 'Llamada con el cliente: proyecto Atlas'), at(1, 12), at(1, 13), { meetingUrl: 'https://meet.example.com/atlas' }),
    cita('personal', T('Gym', 'Gimnasio'), at(1, 19), at(1, 20), { recurrence: 'FREQ=WEEKLY;BYDAY=' + weekday(1) }),
    cita('work', T('Atlas release', 'Entrega de Atlas'), at(3, 0), at(4, 0), { allDay: true }),
    cita('personal', T('Dinner with Sam', 'Cena con Sam'), at(3, 21), at(3, 23), { location: T('The Green Fork', 'La Tagliatella') }),
    cita('work', T('Code review', 'Revisión de código'), at(3, 16), at(3, 17)),
    cita('personal', T('Japanese class', 'Clase de japonés'), at(2, 18), at(2, 19), { recurrence: 'FREQ=WEEKLY;BYDAY=' + weekday(2) }),
    cita('personal', T('Holidays in Lisbon', 'Vacaciones en Lisboa'), at(9, 0), at(14, 0), { allDay: true }),
    cita('work', T('Quarterly review', 'Revisión trimestral'), at(-2, 16), at(-2, 17)),
    cita('personal', T('Mum\'s birthday', 'Cumpleaños de mamá'), at(17, 0), at(18, 0), { allDay: true, reminders: [1440] }),
    cita('work', T('Sprint planning', 'Planificación del sprint'), at(7, 10), at(7, 11, 30)),
    cita('personal', T('Car service', 'Revisión del coche'), at(11, 9), at(11, 10), { location: T('Main Street Garage', 'Taller Hermanos García') }),
    cita('personal', T('Concert: Tycho', 'Concierto: Tycho'), at(20, 21), at(20, 23, 30)),
  ];
  fs.mkdirSync(path.join(data, 'Calendar'), { recursive: true });
  fs.writeFileSync(path.join(data, 'Calendar', 'local.json'), JSON.stringify({
    calendars: [
      { id: 'personal', sourceId: 'local', name: T('Personal', 'Personal'), color: '#4F8EF7', visible: true },
      { id: 'work', sourceId: 'local', name: T('Work', 'Trabajo'), color: '#E8A33D', visible: true, defaultReminder: 10 },
    ],
    events: citas,
  }));
}

// Telegram: the bot switched off (no network, no token) with its owner, contacts and someone waiting, to show the lists
{
  const day = d => new Date(Date.now() - d * 864e5).toISOString();
  const contact = (cid, name, username, level, extra = {}) => ({ id: cid, name, username, level, mayWrite: true, commands: [], tools: [], added: day(20), lastWrote: day(1), ...extra });
  fs.mkdirSync(path.join(data, 'Telegram'), { recursive: true });
  fs.writeFileSync(path.join(data, 'Telegram', 'settings.json'), JSON.stringify({
    bot: { tokenProtected: '', id: 7700112233, name: T('Alex\'s PC', 'El PC de Alex'), username: 'alex_desk_bot', enabled: false },
    owner: { id: 5511223344, name: 'Alex Morgan', username: 'alexm', linked: day(25) },
    contacts: [
      contact(5511000001, 'Sam Rivera', 'samr', 'chatOnly'),
      contact(5511000002, T('Mum', 'Mamá'), null, 'chosenCommands', { commands: [ids.lights, ids.nas], phone: '+44 7700 900123' }),
      contact(5511000003, 'Jordan Lee', 'jordanlee', 'readOnlyTools', { lastWrote: day(4) }),
    ],
    pending: [{ id: 5511000009, name: 'Chris P.', username: 'chrisp', when: day(0.1), text: T('Hi! Is this Alex\'s bot?', '¡Hola! ¿Es el bot de Alex?') }],
    groups: [{ id: -1001122334455, title: T('Family', 'Familia'), ignore: false, seen: day(2) }],
  }, null, 2));
}

// History: today's runs, spread between 8:30 and a few minutes ago (whatever the time)
const now = new Date();
const start = new Date(now); start.setHours(8, 30, 0, 0);
const end = new Date(Math.max(now.getTime() - 4 * 60000, start.getTime() + 3600e3));
const rows = [
  ['morning', 'event', { detail: T('Every weekday at 8:30', 'Laborables a las 8:30'), milliseconds: 2480 }], ['left', 'hotkey'], ['right', 'hotkey'], ['code', 'quickMenu'],
  ['downloads', 'voice', { detail: T('«Open downloads.»', '«Abre descargas.»') }], ['shot', 'hotkey'], ['github', 'launcher'], ['mute', 'hotkey'], ['mute', 'hotkey'],
  ['cleanurl', 'hotkey'], ['nas', 'voice', { detail: T('«Wake up the NAS.»', '«Despierta el NAS.»') }], ['monitor', 'hotkey'], ['ocr', 'hotkey'],
  ['youtube', 'assistant', { detail: 'Lander' }], ['lights', 'web', { outcome: 'error', message: T('homeassistant.local did not answer', 'homeassistant.local no ha contestado') }],
  ['lights', 'web'], ['signature', 'hotkey'], ['dark', 'voice', { detail: T('«Dark mode.»', '«Modo oscuro.»') }], ['center', 'quickMenu'],
  ['login', 'hotkey', { milliseconds: 6230 }], ['calc', 'launcher'], ['headphones', 'event', { detail: T('Headphones connected', 'Auriculares conectados') }],
  ['shutdown', 'quickMenu', { outcome: 'cancelled', message: T('Cancelled with Esc', 'Cancelado con Esc') }], ['gaming', 'voice', { detail: T('«Time to play.»', '«Hora de jugar.»'), milliseconds: 412 }],
];
const iso = d => { const off = -d.getTimezoneOffset(), p = x => String(Math.abs(Math.trunc(x))).padStart(2, '0');
  return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + 'T' + p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds()) +
    '.1234567' + (off >= 0 ? '+' : '-') + p(off / 60) + ':' + p(off % 60); };
const history = rows.map(([k, source, extra], i) => ({ time: iso(new Date(start.getTime() + (end - start) / rows.length * i + ((i * 7919) % 97) * 1000)),
  commandId: ids[k], name: byKey(k).name, source, outcome: 'ok', milliseconds: 18 + (i * 37) % 90, depth: 0, ...extra }));
fs.writeFileSync(path.join(data, 'history.json'), JSON.stringify(history, null, 2));

// When each event last fired
const ago = h => new Date(now.getTime() - h * 3600e3).toISOString();
const evId = name => events.find(e => e.name === name).id;
const morning = new Date(now); morning.setHours(8, 30, 1, 0);
fs.writeFileSync(path.join(data, 'events-state.json'), JSON.stringify({ lastFired: { [evId(T('Every weekday at 8:30', 'Laborables a las 8:30'))]: morning.toISOString(),
  [evId(T('When Elden Ring opens', 'Al abrir Elden Ring'))]: ago(19), [evId(T('Headphones connected', 'Auriculares conectados'))]: ago(1.6),
  [evId(T('When I step away', 'Cuando me levanto'))]: ago(70), [evId(T('CPU above 90%', 'CPU por encima del 90 %'))]: ago(150) } }, null, 2));

// Stats: a few weeks of use; 999 commands so the next one unlocks «1,000 commands»
const stats = JSON.parse(fs.readFileSync(path.join(real, 'stats.json'), 'utf8'));
stats.firstUse = '2026-09-02T18:20:11.4979091+02:00';
Object.assign(stats.counters, { 'commands.run': 999, 'actions.run': 1412, 'commands.owned': commands.length, 'menus.opened': 214, 'launcher.used': 96,
  'voice.orders': 143, 'macros.recorded': 3, 'macros.played': 41, 'events.created': events.length, 'events.fired': 188, 'kboard.created': 2,
  'menus.created': 2, 'commands.created': 58, 'commands.deleted': 12, 'actions.created': 71 });
const runs = { morning: 31, chrome: 54, code: 77, left: 120, right: 112, mute: 96, shot: 88, downloads: 61, youtube: 45, monitor: 70, ocr: 34,
  cleanurl: 29, signature: 22, gaming: 18, dark: 15, lights: 27, login: 41, nas: 9, calc: 14, center: 30 };
stats.commandRuns = Object.fromEntries(Object.entries(runs).map(([k, v]) => [ids[k], v]));
delete stats.unlocked['cmd-run-1000'];
for (const k of Object.keys(stats.unlocked)) stats.unlocked[k] = stats.unlocked[k].replace('2026-10-15T22:46', '2026-09-02T18:20');
fs.writeFileSync(path.join(data, 'stats.json'), JSON.stringify(stats, null, 2));

// The assistant's memory, notes and its tools
const A = path.join(data, 'Assistant');
fs.mkdirSync(path.join(A, 'Tools'), { recursive: true });
const mem = (text, kind, importance, source, days) => ({ id: 'm-' + id().slice(0, 10), text, kind, importance, createdAt: new Date(Date.now() - days * 864e5).toISOString(),
  source, lastRecalled: new Date(Date.now() - days * 3e7).toISOString(), recallCount: Math.round(importance * 20) });
fs.writeFileSync(path.join(A, 'memory.json'), JSON.stringify([
  mem(T('The user is called Alex and works as a web developer.', 'El usuario se llama Alex y es desarrollador web.'), 'fact', 0.95, 'said', 30),
  mem(T('Alex likes synthwave and lo-fi music while working.', 'A Alex le gusta el synthwave y el lo-fi para trabajar.'), 'like', 0.7, 'learned', 21),
  mem(T('When Alex says "movie night", they want the lights off and VLC open.', 'Cuando Alex dice «noche de peli», quiere las luces apagadas y VLC abierto.'), 'fact', 0.9, 'said', 14),
  mem(T('Alex\'s NAS is a Synology called "vault".', 'El NAS de Alex es un Synology llamado «vault».'), 'fact', 0.6, 'learned', 12),
  mem(T('Alex prefers short answers.', 'Alex prefiere las respuestas cortas.'), 'like', 0.8, 'said', 9),
  mem(T('Dentist appointment on Thursday 30 October at 17:30.', 'Cita con el dentista el jueves 30 de octubre a las 17:30.'), 'event', 0.85, 'added', 3),
  mem(T('Alex is learning Japanese; practises on Duolingo every evening.', 'Alex está aprendiendo japonés; practica con Duolingo cada tarde.'), 'fact', 0.5, 'learned', 2),
], null, 2));
const note = (text, days) => ({ id: id(), text, at: new Date(Date.now() - days * 864e5).toISOString() });
fs.writeFileSync(path.join(A, 'notes.json'), JSON.stringify([
  note(T('Buy coffee beans and oat milk', 'Comprar café en grano y bebida de avena'), 0.2), note(T('Call the landlord about the heating', 'Llamar al casero por la calefacción'), 1),
  note(T('Ideas for the video: show the QuickMenu, the Lander towing a window, the launcher with sums', 'Ideas para el vídeo: enseñar el QuickMenu, el Lander remolcando una ventana, el lanzador con cuentas'), 2),
  note(T('Wi-Fi password for guests is on the fridge', 'La contraseña del Wi-Fi de invitados está en la nevera'), 6),
], null, 2));
const tool = (name, obj) => fs.writeFileSync(path.join(A, 'Tools', name + '.json'), JSON.stringify(obj, null, 2));
tool('living_room_light', { name: 'living_room_light', type: 'http', description: T('Turns the living room light on or off (Home Assistant).', 'Enciende o apaga la luz del salón (Home Assistant).'),
  examples: T(['turn on the living room light', 'lights off in the living room'], ['enciende la luz del salón', 'apaga la luz del salón']), parameters: { power: { type: 'string', enum: ['on', 'off'], description: T('on or off', 'on u off') } },
  request: { method: 'POST', url: 'http://homeassistant.local:8123/api/services/light/turn_{{power}}', headers: { Authorization: 'Bearer {{secret:ha_token}}' }, body: { entity_id: 'light.living_room' } },
  intent: T('turn the living room light {{power}}', 'luz del salón: {{power}}'), say: { ok: T('Living room light {{power}}.', 'Luz del salón: {{power}}.'), error: T('Home Assistant is not answering.', 'Home Assistant no contesta.') }, risk: 'safe' });
tool('movie_night', { name: 'movie_night', type: 'routine', description: T('Gets the living room ready for a film: lights off and VLC open.', 'Prepara el salón para ver una peli: luces apagadas y VLC abierto.'),
  examples: T(['movie night', 'let\'s watch a film'], ['noche de peli', 'vamos a ver una película']), steps: [{ tool: 'living_room_light', arguments: { power: 'off' } }, { tool: 'open_app', arguments: { name: 'VLC' } }],
  say: { ok: T('Popcorn time.', 'A por las palomitas.') } });
tool('room_temperature', { name: 'room_temperature', type: 'http', description: T('Reads the living room temperature from Home Assistant.', 'Lee la temperatura del salón en Home Assistant.'), readsOnly: true,
  examples: T(['how warm is it at home?', 'living room temperature'], ['¿qué temperatura hace en casa?', 'temperatura del salón']), request: { method: 'GET', url: 'http://homeassistant.local:8123/api/states/sensor.living_room_temperature',
    headers: { Authorization: 'Bearer {{secret:ha_token}}' }, pick: 'state' }, intent: T('read the living room temperature', 'leer la temperatura del salón'), say: { ok: T('It is {{result}} °C at home.', 'En casa hace {{result}} °C.') } });
// Made-up secrets (so «Secrets» shows none missing); K-Desktop encrypts them with DPAPI when it reads them.
fs.writeFileSync(path.join(A, 'secrets.json'), JSON.stringify({ ha_token: 'demo-home-assistant-token', 'github.GITHUB_TOKEN': 'demo-github-token' }, null, 2));
if (mcpScript) {
  const srv = (name, extra = {}) => ({ command: 'node', args: [mcpScript, name], ...extra });
  fs.writeFileSync(path.join(A, 'mcp.json'), JSON.stringify({ mcpServers: {
    github: srv('github', { env: { GITHUB_TOKEN: '{{secret:github.GITHUB_TOKEN}}' } }),
    calendar: srv('calendar'), spotify: srv('spotify'), filesystem: srv('filesystem'), homeassistant: srv('homeassistant', { disabled: true }),
  } }, null, 2));
}

// The Lander keeps its game progress and look (no private data)
for (const [from, to] of [['Assistants/Data/lander/progress.json', 'Assistants/Data/lander/progress.json'], ['Assistants/Settings/lander.json', 'Assistants/Settings/lander.json']]) {
  fs.mkdirSync(path.dirname(path.join(data, to)), { recursive: true });
  fs.copyFileSync(path.join(real, from), path.join(data, to));
}

// Models by hard links (voice, embeddings, Kokoro): nothing is downloaded again
const link = (rel) => {
  const src = path.join(real, 'Models', rel), dst = path.join(data, 'Models', rel);
  if (!fs.existsSync(src)) return;
  if (fs.statSync(src).isDirectory()) { for (const f of fs.readdirSync(src)) link(path.join(rel, f)); return; }
  fs.mkdirSync(path.dirname(dst), { recursive: true });
  if (!fs.existsSync(dst)) execSync(`cmd /c mklink /H "${dst}" "${src}"`, { stdio: 'ignore' });
};
for (const m of ['embeddinggemma', 'parakeet-tdt-0.6b-v3', 'silero_vad.onnx', 'openwakeword', 'kokoro', 'espeak-ng', 'piper']) link(m);

// The wake words (only listed in Settings › Voice: the microphone is always simulated) and their calibration
const copyDir = (src, dst) => { fs.mkdirSync(dst, { recursive: true }); for (const e of fs.readdirSync(src, { withFileTypes: true })) {
  const a = path.join(src, e.name), b = path.join(dst, e.name); if (e.isDirectory()) copyDir(a, b); else fs.copyFileSync(a, b); } };
for (const w of ['6b9368ae8811', '97495dda6ab0', '9afe60f69a9f'])
  if (fs.existsSync(path.join(real, 'Voice', 'WakeWords', w))) copyDir(path.join(real, 'Voice', 'WakeWords', w), path.join(data, 'Voice', 'WakeWords', w));
if (fs.existsSync(path.join(real, 'Voice', 'WakeCalibration'))) copyDir(path.join(real, 'Voice', 'WakeCalibration'), path.join(data, 'Voice', 'WakeCalibration'));
console.log('ok', data, Object.keys(ids).length, 'commands');
