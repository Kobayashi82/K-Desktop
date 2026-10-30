// addarrangements.js <dataDir> [en|es]: adds made-up Disposiciones to the demo config, on the monitors the demo already
// knows (settings.knownMonitors: run the demo once before). The biggest monitor gets the work layouts.
const fs = require('fs');
const path = require('path');
const [data, lang = 'en'] = process.argv.slice(2);
const T = (en, sp) => (lang === 'es' ? sp : en);
const file = path.join(data, 'config.json');
const config = JSON.parse(fs.readFileSync(file, 'utf8'));
const monitors = [...(config.settings.knownMonitors || [])].sort((a, b) => b.width * b.height - a.width * a.height);
if (monitors.length === 0) { console.log('the demo does not know the monitors yet: run it once'); process.exit(1); }
const [big, small = big] = monitors;
let n = 0;
const zone = (m, left, top, width, height, programs) => ({ monitorName: m.name, monitorDevice: m.device, left, top, width, height, programs });
const app = (process, name, p = '') => ({ process, name, title: '', path: p });
const code = path.join(process.env.LOCALAPPDATA, 'Programs', 'Microsoft VS Code', 'Code.exe');
const chrome = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const arrangement = (name, zones, extra = {}) => ({ id: 'a1b2c3d4e5f' + (++n), name, created: '2026-10-18T11:20:00', openMissing: false, zones, ...extra });
config.arrangements = [
  arrangement(T('Coding', 'Programar'), [
    zone(big, 0, 0, 0.6, 1, [app('code', 'Visual Studio Code', code)]),
    zone(big, 0.6, 0, 0.4, 0.6, [app('chrome', 'Google Chrome', chrome)]),
    zone(big, 0.6, 0.6, 0.4, 0.4, [app('windowsterminal', 'Terminal')]),
    zone(small, 0, 0, 1, 1, [app('spotify', 'Spotify'), app('discord', 'Discord')]),
  ], { openMissing: true, notes: T('Editor, browser and terminal on the big screen; music and chat on the small one.', 'Editor, navegador y terminal en la pantalla grande; música y chat en la pequeña.') }),
  arrangement(T('Mail and calendar', 'Correo y calendario'), [
    zone(big, 0, 0, 0.5, 1, [app('outlook', 'Outlook')]),
    zone(big, 0.5, 0, 0.5, 1, [app('chrome', 'Google Chrome', chrome)]),
  ]),
  arrangement(T('Three columns', 'Tres columnas'), [
    zone(big, 0, 0, 0.3333, 1, []), zone(big, 0.3333, 0, 0.3334, 1, []), zone(big, 0.6667, 0, 0.3333, 1, []),
  ], { notes: T('Empty zones: windows snap into them when dragged.', 'Zonas vacías: las ventanas encajan en ellas al arrastrarlas.') }),
  arrangement(T('Movie night', 'Noche de peli'), [zone(big, 0, 0, 1, 1, [app('vlc', 'VLC', 'C:\\Program Files\\VideoLAN\\VLC\\vlc.exe')])]),
];
fs.writeFileSync(file, JSON.stringify(config, null, 2));
console.log('ok', config.arrangements.length, 'arrangements on', big.name, '/', small.name);
