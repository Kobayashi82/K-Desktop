// Screenshots of the real web app (src/KDesktop.Core/Web) as a phone shows it, with a simulated K-Desktop behind it
// (made-up data of a user called Alex). They land in public/webapp/<lang>/<view>.png.
//   node tools/webapp.mjs [--lang en]
import { createRequire } from 'node:module';
import { mkdirSync, readFileSync, existsSync } from 'node:fs';
import { dirname, extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { browserExecutable } from './common.mjs';

// Playwright is the one already installed on the machine; its Chromium, or any headless shell it downloaded before.
const { chromium } = createRequire(import.meta.url)('playwright');

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const repo = join(root, '..', '..');
const web = join(repo, 'src', 'KDesktop.Core', 'Web');
const lang = process.argv.includes('--lang') ? process.argv[process.argv.indexOf('--lang') + 1] : 'en';
const out = join(root, 'public', 'webapp', lang);
mkdirSync(out, { recursive: true });

const strings = file => Object.fromEntries(Object.entries(JSON.parse(readFileSync(join(repo, 'src', 'KDesktop.Core', 'Languages', file), 'utf8'))).filter(([k]) => k.startsWith('pwa.')));
const texts = { ...strings('en.json'), ...strings(`${lang}.json`) };
const L = lang === 'es';

// Icons: K-Desktop tiles (gradient, shine, white Fluent symbol), drawn by the browser itself.
const PALETTE = { gold: ['#FFD45A', '#E09A0B'], blue: ['#5AA9FF', '#1E62DA'], sky: ['#55D2FF', '#0C8FD6'], teal: ['#3EDCC1', '#0C9A87'], green: ['#6BDE79', '#209E3E'], indigo: ['#8C93FF', '#4A4FD9'], purple: ['#BC8CFF', '#7B3FE4'], pink: ['#FF8CC6', '#DB3F86'], red: ['#FF7A7A', '#D9363E'], orange: ['#FFB061', '#F06A12'], yellow: ['#FFE066', '#F2B807'], gray: ['#A9B2BD', '#606A77'], dark: ['#5B6573', '#1E2530'], lime: ['#C3EB5C', '#7CB518'] };
const ICONS = {
  morning: ['weather_sunny', 'orange'], work: ['flash', 'gold'], music: ['music_note_2', 'pink'], snapl: ['panel_left', 'sky'], snapr: ['panel_right', 'sky'],
  shot: ['screenshot', 'green'], lights: ['lightbulb_filament', 'yellow'], phones: ['headphones_sound_wave', 'purple'], game: ['xbox_controller', 'indigo'],
  sign: ['text_t', 'brown'], link: ['link', 'teal'], nas: ['desktop_pulse', 'green'], chrome: ['globe', 'blue'], code: ['code', 'blue'], steam: ['games', 'indigo'],
  windows: ['window', 'sky'], web: ['globe', 'sky'], folders: ['folder', 'yellow'], system: ['settings', 'gray'], downloads: ['arrow_download', 'teal'], lock: ['lock_closed', 'orange'],
  recycle: ['delete', 'gray'], volume: ['speaker_2', 'teal'], power: ['power', 'red'], dark: ['dark_theme', 'dark'], calc: ['calculator', 'gray'],
};
PALETTE.brown = ['#D2A375', '#8F5C34'];
const svgOf = name => readFileSync(join(root, 'node_modules', '@fluentui', 'svg-icons', 'icons', `${name}_24_filled.svg`), 'utf8').replace(/fill="[^"]*"/g, '').replace('<svg', '<svg fill="white"');

const T = (en, es) => (L ? es : en);
const cmd = (id, name, icon, runs) => ({ id, name, icon, runs, hidden: false });
const commands = [
  cmd('c1', T('Morning routine', 'Rutina de mañana'), 'morning', 61), cmd('c2', T('Work mode', 'Modo trabajo'), 'work', 92), cmd('c3', T('Music', 'Música'), 'music', 48),
  cmd('c4', T('Snap left', 'Mitad izquierda'), 'snapl', 130), cmd('c5', T('Snap right', 'Mitad derecha'), 'snapr', 118), cmd('c6', T('Screenshot region', 'Captura de una región'), 'shot', 40),
  cmd('c7', T('Living room lights off', 'Apagar luces del salón'), 'lights', 22), cmd('c8', T('Switch to headphones', 'Cambiar a auriculares'), 'phones', 35),
  cmd('c9', T('Gaming mode', 'Modo juego'), 'game', 17), cmd('c10', T('Type my signature', 'Escribir mi firma'), 'sign', 12), cmd('c11', T('Clean link in clipboard', 'Limpiar enlace del portapapeles'), 'link', 9),
  cmd('c12', T('Wake up the NAS', 'Encender el NAS'), 'nas', 6), cmd('c13', 'Chrome', 'chrome', 70), cmd('c14', 'VS Code', 'code', 55), cmd('c15', 'Steam', 'steam', 30),
  cmd('c16', T('Downloads', 'Descargas'), 'downloads', 28), cmd('c17', T('Lock PC', 'Bloquear PC'), 'lock', 14), cmd('c18', T('Empty Recycle Bin', 'Vaciar papelera'), 'recycle', 11),
];
const node = (id, kind, label, commandId, icon, children) => ({ id, kind, label, commandId, icon, children });
const menus = [
  { id: 'm1', name: T('Main', 'Principal'), items: [
    node('i0', 'title', T('Apps', 'Aplicaciones')), node('i1', 'command', 'Chrome', 'c13', 'chrome'), node('i2', 'command', 'VS Code', 'c14', 'code'), node('i3', 'command', 'Steam', 'c15', 'steam'),
    node('i4', 'separator'), node('i5', 'submenu', T('Windows', 'Ventanas'), null, 'windows', [node('j1', 'command', T('Snap left', 'Mitad izquierda'), 'c4', 'snapl'), node('j2', 'command', T('Snap right', 'Mitad derecha'), 'c5', 'snapr')]),
    node('i6', 'submenu', 'Web', null, 'web', []), node('i7', 'submenu', T('Folders', 'Carpetas'), null, 'folders', [node('j3', 'command', T('Downloads', 'Descargas'), 'c16', 'downloads')]),
    node('i8', 'submenu', T('System', 'Sistema'), null, 'system', [node('j4', 'command', T('Lock PC', 'Bloquear PC'), 'c17', 'lock')]),
    node('i9', 'command', T('Screenshot region', 'Captura de una región'), 'c6', 'shot'), node('i10', 'command', T('Work mode', 'Modo trabajo'), 'c2', 'work'),
  ] },
  { id: 'm2', name: T('Home', 'Casa'), items: [node('k1', 'command', T('Living room lights off', 'Apagar luces del salón'), 'c7', 'lights'), node('k2', 'command', T('Wake up the NAS', 'Encender el NAS'), 'c12', 'nas')] },
  { id: 'm3', name: T('Media', 'Multimedia'), items: [node('k3', 'command', T('Music', 'Música'), 'c3', 'music'), node('k4', 'command', T('Switch to headphones', 'Cambiar a auriculares'), 'c8', 'phones')] },
];
// The web app shows submenus that hold something only.
menus[0].items[6].children = [node('j5', 'command', 'Chrome', 'c13', 'chrome')];

const now = new Date('2026-10-24T09:41:00').toISOString();
const chat = [
  { role: 'user', turn: 't1', text: T('What do I have tomorrow?', '¿Qué tengo mañana?'), final: true, at: now },
  { role: 'assistant', turn: 't1', text: T('Dentist at 17:30, and you told me to remind you about Ana’s birthday. Shall I set a reminder at 16:45?', 'Dentista a las 17:30, y me pediste que te recordara el cumpleaños de Ana. ¿Te pongo un aviso a las 16:45?'), final: true, at: now },
  { role: 'user', turn: 't2', text: T('Yes, and turn off the living room lights', 'Sí, y apaga las luces del salón'), final: true, at: now },
  { role: 'assistant', turn: 't2', text: T('Done: reminder at 16:45. Lights off. 🌙', 'Hecho: aviso a las 16:45. Luces apagadas. 🌙'), final: true, at: now },
];

const json = (route, data) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(data) });
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.json': 'application/json' };

const browser = await chromium.launch(browserExecutable ? { executablePath: browserExecutable } : {});
// 1) The icons, drawn as PNG.
const iconPage = await browser.newPage({ deviceScaleFactor: 1 });
const iconPngs = {};
for (const [key, [symbol, color]] of Object.entries(ICONS)) {
  const [top, bottom] = PALETTE[color];
  await iconPage.setContent(`<body style="margin:0;background:transparent"><div id="t" style="width:96px;height:96px;position:relative;border-radius:22px;background:linear-gradient(${top},${bottom});box-shadow:inset 0 0 0 2px rgba(0,0,0,.24);display:flex;align-items:center;justify-content:center"><div style="position:absolute;inset:0;border-radius:22px;background:linear-gradient(rgba(255,255,255,.28),rgba(255,255,255,0) 55%)"></div><div style="width:50px;height:50px;position:relative">${svgOf(symbol).replace('<svg', '<svg width="50" height="50"')}</div></div></body>`);
  iconPngs[key] = await iconPage.locator('#t').screenshot({ omitBackground: true });
}
for (const a of ['lander', 'kobii', 'pip']) iconPngs[a] = readFileSync(join(root, 'public', 'brand', a, 'icon.png'));

// 2) The app.
const shoot = async (name, { tab, paired = true, after } = {}) => {
  const context = await browser.newContext({ viewport: { width: 412, height: 892 }, deviceScaleFactor: 3, colorScheme: 'dark', isMobile: true, hasTouch: true, locale: lang });
  await context.addInitScript(([paired, tab, lang]) => {
    localStorage.setItem('kd.lang', JSON.stringify(lang));
    if (paired) localStorage.setItem('kd.token', JSON.stringify('demo'));
    if (tab) localStorage.setItem('kd.tab', JSON.stringify(tab));
  }, [paired, tab, lang]);
  const page = await context.newPage();
  await page.route('http://kdesktop.local/**', async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    if (path.startsWith('/api/')) {
      const api = path.slice(5);
      if (api === 'i18n') return json(route, { lang, languages: [{ code: 'en', name: 'English' }, { code: 'es', name: 'Español' }], strings: texts });
      if (!paired && api !== 'pair') return route.fulfill({ status: 401, contentType: 'application/json', body: '{"error":"unpaired"}' });
      if (api === 'session') return json(route, { app: 'K-Desktop 5.0', version: '5.0.188', machine: 'ALEX-PC', device: { id: 'd1', name: T('Alex’s phone', 'Móvil de Alex') }, local: false, input: true, voice: true, assistants: true, addresses: ['http://192.168.1.150:2502/'], android: null, cookie: false });
      if (api === 'catalog') return json(route, { machine: 'ALEX-PC', commands, menus });
      if (api === 'audio') return json(route, { volume: 42, muted: false, devices: [
        { id: 'a1', name: T('Speakers', 'Altavoces'), adapter: 'Realtek(R) Audio', active: true, default: true, kind: 'speakers' },
        { id: 'a2', name: T('Headphones', 'Auriculares'), adapter: 'Arctis 7', active: true, default: false, kind: 'headphones' },
        { id: 'a3', name: 'LG TV', adapter: 'NVIDIA High Definition Audio', active: true, default: false, kind: 'digital' },
      ] });
      if (api === 'assistants') return json(route, { enabled: true, assistants: [{ id: 'lander', name: 'Lander', busy: false, icon: 'lander' }, { id: 'kobii', name: 'Kobii', busy: false, icon: 'kobii' }, { id: 'pip', name: 'Pip', busy: false, icon: 'pip' }] });
      if (api === 'assistants/chat') return json(route, { lines: url.searchParams.get('id') === 'lander' ? chat : [] });
      if (api === 'events') return route.fulfill({ status: 200, contentType: 'text/event-stream', body: ': hi\n\n' });
      if (api.startsWith('icon/')) {
        const key = api.slice(5).replace('.png', '');
        return iconPngs[key] ? route.fulfill({ status: 200, contentType: 'image/png', body: iconPngs[key] }) : route.fulfill({ status: 404 });
      }
      return json(route, {});
    }
    if (path.endsWith('manifest.webmanifest')) return json(route, { name: 'K-Desktop', start_url: '.', display: 'standalone' });
    const file = join(web, path === '/' ? 'index.html' : path.slice(1));
    if (!existsSync(file)) return route.fulfill({ status: 404 });
    return route.fulfill({ status: 200, contentType: TYPES[extname(file)] ?? 'application/octet-stream', body: readFileSync(file) });
  });
  await page.goto('http://kdesktop.local/');
  await page.waitForTimeout(1500);
  if (after) await after(page);
  await page.waitForTimeout(700);
  await page.screenshot({ path: join(out, `${name}.png`) });
  console.log(name);
  await context.close();
};

await shoot('pair', { paired: false });
await shoot('all', { tab: 'all' });
await shoot('menus', { tab: 'menus' });
await shoot('menus-sub', { tab: 'menus', after: async p => { await p.getByText(T('Windows', 'Ventanas'), { exact: true }).first().click().catch(() => {}); } });
await shoot('pad', { tab: 'pad' });
await shoot('pad-keys', { tab: 'pad', after: async p => { await p.click('#keysButton').catch(() => {}); } });
await shoot('audio', { tab: 'audio' });
await shoot('assistant', { tab: 'assistant', after: async p => { await p.getByText('Lander', { exact: true }).first().click().catch(() => {}); } });
await shoot('settings', { tab: 'settings' });
await browser.close();
