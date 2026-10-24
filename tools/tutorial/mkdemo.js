// mkdemo.js <dataDir> <knowledgeFolder> <mcpScript> <demoModels>: a demo data folder for the tutorial screenshots, in
// English, dark theme. Realistic commands, menus, macros, K-Board, events, history, stats, memory and notes for a made-up
// user «Alex». From the real data (%APPDATA%\K-Desktop 5) only the Lander's game progress and look, the stats counters
// (unlocked achievements), the wake words and hard links to the voice models are taken; never config.json.
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');
const [data, knowledge, mcpScript, demoModels] = process.argv.slice(2);
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
  cmd('morning', 'Morning routine', [
    { type: 'runApp', path: chrome, arguments: 'https://mail.google.com https://calendar.google.com' },
    { type: 'wait', seconds: 2 },
    { type: 'window', target: 'process', processName: 'chrome', operation: 'snap', snap: 'leftTwoThirds' },
    { type: 'runApp', path: 'C:\\Program Files\\AIMP\\AIMP.exe' },
    { type: 'volume', target: 'system', mode: 'set', value: 35, showOsd: true },
    { type: 'showMessage', text: 'Good morning!', seconds: 3 },
  ], { hotkeys: ['CTRL + ALT + G'], voicePhrases: ['good morning', 'start my day'], notes: 'Mail, calendar and some music to start the day.' }),
  cmd('chrome', 'Chrome', [{ type: 'runApp', path: chrome }], { hotkeys: ['CTRL + ALT + C'], voicePhrases: ['open the browser'] }),
  cmd('code', 'VS Code', [{ type: 'runApp', path: code }], { hotkeys: ['CTRL + ALT + V'] }),
  cmd('steam', 'Steam', [{ type: 'runApp', path: 'C:\\Program Files (x86)\\Steam\\steam.exe' }]),
  cmd('vlc', 'VLC', [{ type: 'runApp', path: 'C:\\Program Files\\VideoLAN\\VLC\\vlc.exe' }]),
  cmd('calc', 'Calculator', [{ type: 'runApp', path: 'C:\\Windows\\System32\\calc.exe' }], { voicePhrases: ['calculator'] }),
  cmd('downloads', 'Downloads', [{ type: 'openFolder', special: 'Downloads', path: '' }], { hotkeys: ['CTRL + ALT + J'], voicePhrases: ['open downloads'] }),
  cmd('documents', 'Documents', [{ type: 'openFolder', special: 'Documents', path: '' }]),
  cmd('recycle', 'Recycle Bin', [{ type: 'openFolder', special: 'RecycleBin', path: '' }]),
  cmd('github', 'GitHub', [{ type: 'openUrl', url: 'https://github.com', browser: 'chrome' }]),
  cmd('youtube', 'YouTube', [{ type: 'openUrl', url: 'https://www.youtube.com', browser: 'chrome' }], { voicePhrases: ['open youtube'] }),
  cmd('gmail', 'Gmail', [{ type: 'openUrl', url: 'https://mail.google.com', browser: 'chrome' }]),
  cmd('left', 'Snap left', [{ type: 'window', target: 'active', operation: 'snap', snap: 'leftHalf' }], { hotkeys: ['WIN + ALT + Left'] }),
  cmd('right', 'Snap right', [{ type: 'window', target: 'active', operation: 'snap', snap: 'rightHalf' }], { hotkeys: ['WIN + ALT + Right'] }),
  cmd('topleft', 'Top-left quarter', [{ type: 'window', target: 'active', operation: 'snap', snap: 'topLeft' }], { hotkeys: ['WIN + ALT + NumPad7'] }),
  cmd('center', 'Center window', [{ type: 'window', target: 'active', operation: 'center' }], { hotkeys: ['WIN + ALT + C'] }),
  cmd('monitor', 'Move to next monitor', [{ type: 'window', target: 'active', operation: 'moveToMonitor', monitor: 'next' }], { hotkeys: ['WIN + ALT + Up'] }),
  cmd('ontop', 'Always on top', [{ type: 'topMost' }], { hotkeys: ['CTRL + ALT + X'] }),
  cmd('shot', 'Screenshot region', [{ type: 'screenshot', area: 'region', copy: true, save: true, open: false, showOsd: true }], { hotkeys: ['CTRL + PrintScreen'] }),
  cmd('ocr', 'Copy text from screen', [{ type: 'screenText', area: 'region', showOsd: true }], { hotkeys: ['CTRL + ALT + E'] }),
  cmd('mute', 'Mute microphone', [{ type: 'volume', target: 'microphone', mode: 'toggleMute', value: 0, showOsd: true }], { hotkeys: ['CTRL + ALT + M'], voicePhrases: ['mute the mic'] }),
  cmd('volup', 'Volume up', [{ type: 'volume', target: 'system', mode: 'up', value: 5, showOsd: true }], { hotkeys: ['CTRL + ALT + Up'] }),
  cmd('voldown', 'Volume down', [{ type: 'volume', target: 'system', mode: 'down', value: 5, showOsd: true }], { hotkeys: ['CTRL + ALT + Down'] }),
  cmd('headphones', 'Switch to headphones', [{ type: 'audioOutput', devices: [{ id: '', name: 'Headphones (Arctis 7)' }, { id: '', name: 'Speakers (Realtek)' }], showOsd: true }], { voicePhrases: ['headphones'] }),
  cmd('dark', 'Dark mode', [{ type: 'windowsTheme', mode: 'toggle' }], { voicePhrases: ['dark mode', 'light mode'] }),
  cmd('hidden', 'Show hidden files', [{ type: 'hiddenFiles', showOsd: true }], { hotkeys: ['CTRL + ALT + I'] }),
  cmd('cleanurl', 'Clean link in clipboard', [{ type: 'clipboard', mode: 'cleanUrl' }], { hotkeys: ['CTRL + ALT + U'] }),
  cmd('signature', 'Type my signature', [{ type: 'sendInput', mode: 'text', value: 'Best regards,\nAlex' }], { hotkeys: ['CTRL + ALT + N'] }),
  cmd('zoom', 'Zoom lens', [{ type: 'magnifier', mode: 'toggle', zoom: 2 }], { hotkeys: ['WIN + ALT + Z'] }),
  cmd('dictate', 'Dictate', [{ type: 'dictate', mode: 'toggle' }], { hotkeys: ['CTRL + ALT + F'] }),
  cmd('wifi', 'Toggle Wi-Fi', [{ type: 'radio', radio: 'wiFi', mode: 'toggle' }]),
  cmd('nas', 'Wake up the NAS', [{ type: 'wakeOnLan', mac: '00:11:32:9A:4C:7E', host: 'vault' }], { voicePhrases: ['wake up the nas'] }),
  cmd('lights', 'Living room lights off', [{ type: 'httpRequest', method: 'post', contentType: 'application/json', url: 'http://homeassistant.local:8123/api/services/light/turn_off', body: '{"entity_id":"light.living_room"}' }], { voicePhrases: ['lights off'] }),
  cmd('monitoroff', 'Turn off monitors', [{ type: 'display', operation: 'monitorOff' }]),
  cmd('lock', 'Lock PC', [{ type: 'power', mode: 'lock', delaySeconds: 0, showOsd: false }], { hotkeys: ['WIN + ALT + L'] }),
  cmd('sleep', 'Sleep', [{ type: 'power', mode: 'sleep', delaySeconds: 10, showOsd: true }]),
  cmd('shutdown', 'Shut down', [{ type: 'power', mode: 'shutdown', delaySeconds: 30, showOsd: true }], { voicePhrases: ['shut down the computer'] }),
  cmd('menu', 'Main menu', [{ type: 'quickMenu', menuId: 'MENUID' }], { hotkeys: ['CTRL + ALT + Q'] }),
  cmd('login', 'Fill in timesheet', [{ type: 'playMacro', macroId: 'MACROID', repeat: 1, showOsd: true }], { hotkeys: ['CTRL + ALT + R'] }),
  cmd('gameboard', 'Gaming keyboard', [{ type: 'kboard', profileId: 'KBID', mode: 'toggle', showOsd: true }], { hotkeys: ['CTRL + ALT + W'] }),
  cmd('gaming', 'Gaming mode', [
    { type: 'powerPlan', mode: 'set', planName: 'High performance' },
    { type: 'kboard', profileId: 'KBID', mode: 'activate', showOsd: false },
    { type: 'volume', target: 'system', mode: 'set', value: 60, showOsd: false },
    { type: 'appControl', mode: 'close', processName: 'chrome', force: false },
    { type: 'showMessage', text: 'Gaming mode on', seconds: 2 },
  ], { voicePhrases: ['gaming mode', 'time to play'] }),
  cmd('work', 'Work mode', [
    { type: 'runApp', path: code },
    { type: 'runApp', path: chrome, arguments: 'https://github.com' },
    { type: 'powerPlan', mode: 'set', planName: 'Balanced' },
    { type: 'volume', target: 'system', mode: 'set', value: 25, showOsd: false },
  ], { voicePhrases: ['work mode'] }),
  cmd('askLander', 'Ask Lander', [{ type: 'assistantAsk', assistant: 'lander', text: '' }], { hotkeys: ['CTRL + ALT + A'] }),
  cmd('landerHi', 'Lander: say hello', [{ type: 'assistantSay', assistant: 'lander', text: 'Hi! Your downloads are done and the coffee break starts in 5 minutes.' }]),
  cmd('freeBrain', 'Free the graphics card', [{ type: 'assistantBrain', op: 'unload' }]),
];
// Hidden, for the clips: they only touch the demo Edge windows (process msedge) and the «ask» ones carry their text, so
// nothing depends on which window is in front (a «this window» order once moved the user's own Chrome).
const edge = { type: 'window', target: 'process', processName: 'msedge' };
for (const [t, op] of [['Browser to the left', { operation: 'snap', snap: 'leftHalf' }], ['Browser to the right', { operation: 'snap', snap: 'rightHalf' }],
  ['Centre the browser', { operation: 'center' }], ['Minimize the browser', { operation: 'minimize' }], ['Maximize the browser', { operation: 'maximize' }]]) {
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
  id: qid, name: 'Main menu', size: 1, items: [
    item('chrome'), item('code'), item('steam'), sep(),
    sub('Windows', 'kd:Window24:blue', [item('left'), item('right'), item('topleft'), item('center'), sep(), item('monitor'), item('ontop')]),
    sub('Web', 'kd:Globe24:green', [item('github'), item('youtube'), item('gmail')]),
    sub('Folders', 'kd:Folder24:orange', [item('downloads'), item('documents'), item('recycle')]),
    sub('System', 'kd:Settings24:purple', [item('dark'), item('hidden'), item('monitoroff'), sep(), item('lock'), item('sleep'), item('shutdown')]),
    sep(), item('shot'), item('ocr'),
  ],
}];
byKey('menu').actions[0].menuId = qid;

// Macro: a recorded timesheet form
const mid = id();
const step = (kind, delay, extra = {}) => ({ kind, delay, vk: 0, extended: false, button: 'left', x: 0, y: 0, wheel: 0, horizontal: false, raw: false, ...extra });
const key = (vk, delay) => [step('keyDown', delay, { vk }), step('keyUp', 60, { vk })];
const macros = [{
  id: mid, name: 'Fill in timesheet', notes: 'Recorded on the company portal: date, hours, project and send.',
  created: '2026-10-20T09:12:40+02:00', gameMode: false, steps: [
    step('click', 0, { x: 812, y: 344 }),
    step('text', 420, { text: 'Friday 24 October' }),
    ...key(9, 180),
    step('text', 240, { text: '8' }),
    ...key(9, 160),
    step('click', 520, { x: 905, y: 462 }),
    step('wheel', 300, { x: 905, y: 520, wheel: -240 }),
    step('click', 380, { x: 880, y: 588 }),
    ...key(9, 200),
    step('text', 260, { text: 'K-Desktop tutorial video' }),
    step('pause', 800),
    step('mouseMove', 0, { x: 1210, y: 712 }),
    step('click', 140, { x: 1210, y: 712 }),
  ],
}];
byKey('login').actions[0].macroId = mid;

// K-Board: only inside a game that is not running, so nothing changes while the demo is open
const kid = id();
const kBoards = [{
  id: kid, name: 'Gaming', active: true, ignoreDisabledApps: true, processes: ['eldenring.exe'], notes: 'Only while Elden Ring is in front.',
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
  ev('When Elden Ring opens', 'kd:Games24:purple', { type: 'processStart', processes: ['eldenring.exe'], includeRunningAtStart: false }, ['gaming'],
    { endCommands: [ids.work], notes: 'Gaming mode while it runs, back to work mode when it closes.' }),
  ev('Every weekday at 8:30', 'kd:Clock24:orange', { type: 'time', time: '08:30', days: 'workdays', runIfMissed: true }, ['morning']),
  ev('When I step away', 'kd:PersonWalking24:blue', { type: 'idle', minutes: 20 }, ['monitoroff'], { enabled: false }),
  ev('Headphones connected', 'kd:Headphones24:green', { type: 'audioDevice', event: 'connected', name: 'Arctis' }, ['headphones']),
  ev('New file in Downloads', 'kd:ArrowDownload24:teal', { type: 'file', folder: '%USERPROFILE%\\Downloads', filter: '*.torrent', subfolders: false }, ['downloads'], { enabled: false }),
  ev('CPU above 90%', 'kd:Gauge24:red', { type: 'load', resource: 'cpu', comparison: 'above', percent: 90, seconds: 120 }, ['landerHi'], { enabled: false }),
];

const settings = {
  language: 'en', theme: 'dark', updateMode: 'off', startWithWindows: false, showWindowOnStart: true,
  windowBounds: '-1920,278,1920,1080', windowPage: 'commands', commandsMenuFilter: 'all',
  showWindowHotkey: 'CTRL + ALT + SHIFT + Home', exitHotkey: 'CTRL + ALT + SHIFT + End',
  launcherHotkey: 'CTRL + ALT + Space', launcherPosition: '0.5;0.18',
  voiceHotkey: 'Pause', voiceDuck: false, voiceMedia: false, voiceWakeListen: true, voiceListenSound: null,
  udpEnabled: true, udpPort: 2601, webEnabled: true, webPort: 2602,
  achievementNotifications: true, achievementSound: false, autoBackup: false,
  assistantEnabled: true, assistantKDesktopAdded: true,
  assistantsOnDesktop: ['kdesktop'],
  assistantSound: true, assistantVolume: 60, assistantAdorn: false, assistantAskBefore: 'risky',
  assistantVoiceEngine: 'auto', assistantVoiceKokoro: 'af_heart', assistantVoiceVolume: 80,
  assistantBrainServers: [
    'E:\\ProjectAI\\Open WebUI\\Llama-CPP\\llama-server.exe',
    'E:\\ProjectAI\\Open WebUI\\Bonsai\\Bonsai-demo\\bin\\cuda\\llama-server.exe'],
  // Only the demo folder (hard links to models with clean names): the real folders hold names that do not belong in a video.
  assistantBrainFolders: [demoModels || 'E:\\AI Models'],
  assistantBrainKeepMinutes: 10, assistantBrainOneAtATime: true,
  assistantKnowledgeFolders: knowledge ? [knowledge] : [],
  assistantHomePlace: 'London',
  assistantTalkHotkeys: { lander: 'F9' },
  assistantTrustedTools: ['living_room_light'],
  kDesktopWakeWords: ['9afe60f69a9f'], assistantWakeWords: { lander: '97495dda6ab0', kobii: '6b9368ae8811' },
};
fs.writeFileSync(path.join(data, 'config.json'), JSON.stringify({ schemaVersion: 3, settings, commands, quickMenus, macros, kBoards, events }, null, 2));

// History: today's runs, spread between 8:30 and a few minutes ago (whatever the time)
const now = new Date();
const start = new Date(now); start.setHours(8, 30, 0, 0);
const end = new Date(Math.max(now.getTime() - 4 * 60000, start.getTime() + 3600e3));
const rows = [
  ['morning', 'event', { detail: 'Every weekday at 8:30', milliseconds: 2480 }], ['left', 'hotkey'], ['right', 'hotkey'], ['code', 'quickMenu'],
  ['downloads', 'voice', { detail: '«Open downloads.»' }], ['shot', 'hotkey'], ['github', 'launcher'], ['mute', 'hotkey'], ['mute', 'hotkey'],
  ['cleanurl', 'hotkey'], ['nas', 'voice', { detail: '«Wake up the NAS.»' }], ['monitor', 'hotkey'], ['ocr', 'hotkey'],
  ['youtube', 'assistant', { detail: 'Lander' }], ['lights', 'web', { outcome: 'error', message: 'homeassistant.local did not answer' }],
  ['lights', 'web'], ['signature', 'hotkey'], ['dark', 'voice', { detail: '«Dark mode.»' }], ['center', 'quickMenu'],
  ['login', 'hotkey', { milliseconds: 6230 }], ['calc', 'launcher'], ['headphones', 'event', { detail: 'Headphones connected' }],
  ['shutdown', 'quickMenu', { outcome: 'cancelled', message: 'Cancelled with Esc' }], ['gaming', 'voice', { detail: '«Time to play.»', milliseconds: 412 }],
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
fs.writeFileSync(path.join(data, 'events-state.json'), JSON.stringify({ lastFired: { [evId('Every weekday at 8:30')]: morning.toISOString(),
  [evId('When Elden Ring opens')]: ago(19), [evId('Headphones connected')]: ago(1.6), [evId('When I step away')]: ago(70), [evId('CPU above 90%')]: ago(150) } }, null, 2));

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
  mem('The user is called Alex and works as a web developer.', 'fact', 0.95, 'said', 30),
  mem('Alex likes synthwave and lo-fi music while working.', 'like', 0.7, 'learned', 21),
  mem('When Alex says "movie night", they want the lights off and VLC open.', 'fact', 0.9, 'said', 14),
  mem('Alex\'s NAS is a Synology called "vault".', 'fact', 0.6, 'learned', 12),
  mem('Alex prefers short answers.', 'like', 0.8, 'said', 9),
  mem('Dentist appointment on Thursday 30 October at 17:30.', 'event', 0.85, 'added', 3),
  mem('Alex is learning Japanese; practises on Duolingo every evening.', 'fact', 0.5, 'learned', 2),
], null, 2));
const note = (text, days) => ({ id: id(), text, at: new Date(Date.now() - days * 864e5).toISOString() });
fs.writeFileSync(path.join(A, 'notes.json'), JSON.stringify([
  note('Buy coffee beans and oat milk', 0.2), note('Call the landlord about the heating', 1),
  note('Ideas for the video: show the QuickMenu, the Lander towing a window, the launcher with sums', 2),
  note('Wi-Fi password for guests is on the fridge', 6),
], null, 2));
const tool = (name, obj) => fs.writeFileSync(path.join(A, 'Tools', name + '.json'), JSON.stringify(obj, null, 2));
tool('living_room_light', { name: 'living_room_light', type: 'http', description: 'Turns the living room light on or off (Home Assistant).',
  examples: ['turn on the living room light', 'lights off in the living room'], parameters: { power: { type: 'string', enum: ['on', 'off'], description: 'on or off' } },
  request: { method: 'POST', url: 'http://homeassistant.local:8123/api/services/light/turn_{{power}}', headers: { Authorization: 'Bearer {{secret:ha_token}}' }, body: { entity_id: 'light.living_room' } },
  intent: 'turn the living room light {{power}}', say: { ok: 'Living room light {{power}}.', error: 'Home Assistant is not answering.' }, risk: 'safe' });
tool('movie_night', { name: 'movie_night', type: 'routine', description: 'Gets the living room ready for a film: lights off and VLC open.',
  examples: ['movie night', 'let\'s watch a film'], steps: [{ tool: 'living_room_light', arguments: { power: 'off' } }, { tool: 'open_app', arguments: { name: 'VLC' } }],
  say: { ok: 'Popcorn time.' } });
tool('room_temperature', { name: 'room_temperature', type: 'http', description: 'Reads the living room temperature from Home Assistant.', readsOnly: true,
  examples: ['how warm is it at home?', 'living room temperature'], request: { method: 'GET', url: 'http://homeassistant.local:8123/api/states/sensor.living_room_temperature',
    headers: { Authorization: 'Bearer {{secret:ha_token}}' }, pick: 'state' }, intent: 'read the living room temperature', say: { ok: 'It is {{result}} °C at home.' } });
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
