import { continueRender, delayRender, staticFile } from 'remotion';

// The fonts travel with the project (public/fonts, SIL OFL): renders never depend on a font service.
const LATIN = 'U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD';
const LATIN_EXT = 'U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C4, U+2113, U+2C60-2C7F, U+A720-A7FF';
const FACES: Array<[string, string, string, string]> = [
  ['KD Inter', 'fonts/inter-latin.woff2', '400 800', LATIN],
  ['KD Inter', 'fonts/inter-latin-ext.woff2', '400 800', LATIN_EXT],
  ['KD Mono', 'fonts/jetbrains-mono-latin.woff2', '400 600', LATIN],
];
if (typeof document !== 'undefined') {
  const handle = delayRender('Loading fonts');
  Promise.all(FACES.map(([family, file, weight, unicodeRange]) => {
    const face = new FontFace(family, `url(${staticFile(file)}) format('woff2')`, { weight, unicodeRange });
    document.fonts.add(face);
    return face.load();
  })).then(() => continueRender(handle), () => continueRender(handle));
}

export const SANS = "'KD Inter', 'Segoe UI', sans-serif";
export const MONO = "'KD Mono', Consolas, monospace";

/** K-Desktop's dark theme (src/KDesktop/Themes/KDark.xaml) and the web app's ambient gold. */
export const C = {
  bg: '#0b0b0e',
  bg2: '#121216',
  window: '#1b1b1f',
  sidebar: '#202024',
  card: '#26262b',
  card2: '#2c2c32',
  row: '#303036',
  line: 'rgba(255,255,255,0.08)',
  line2: 'rgba(255,255,255,0.14)',
  chip: 'rgba(255,255,255,0.1)',
  text: '#f4f4f6',
  dim: 'rgba(255,255,255,0.62)',
  faint: 'rgba(255,255,255,0.38)',
  accent: '#E0A815',
  accentText: '#F2C14A',
  accentSoft: 'rgba(224,168,21,0.2)',
  gold1: '#FFD970',
  gold2: '#F7B32B',
  success: '#6CCB5F',
  danger: '#FF6B6B',
  warning: '#FCB33F',
  teal: '#3EDCC1',
  sky: '#55D2FF',
  violet: '#BC8CFF',
};

/** The K-Desktop icon palette (src/KDesktop/Services/KdIcons.cs): key → gradient top, bottom. */
export const PALETTE: Record<string, [string, string]> = {
  gold: ['#FFD45A', '#E09A0B'],
  yellow: ['#FFE066', '#F2B807'],
  orange: ['#FFB061', '#F06A12'],
  red: ['#FF7A7A', '#D9363E'],
  pink: ['#FF8CC6', '#DB3F86'],
  purple: ['#BC8CFF', '#7B3FE4'],
  indigo: ['#8C93FF', '#4A4FD9'],
  blue: ['#5AA9FF', '#1E62DA'],
  sky: ['#55D2FF', '#0C8FD6'],
  teal: ['#3EDCC1', '#0C9A87'],
  green: ['#6BDE79', '#209E3E'],
  lime: ['#C3EB5C', '#7CB518'],
  brown: ['#D2A375', '#8F5C34'],
  gray: ['#A9B2BD', '#606A77'],
  dark: ['#5B6573', '#1E2530'],
};

export type PaletteKey = keyof typeof PALETTE;

/** The mood of each chapter: the colours the background light drifts towards. */
export const MOODS: Record<string, [string, string, string]> = {
  intro: ['#E0A815', '#7a4a10', '#1d2a5a'],
  overview: ['#E0A815', '#5a3a9a', '#14406a'],
  commands: ['#E0A815', '#1E62DA', '#3a1f6a'],
  quickmenu: ['#E0A815', '#7B3FE4', '#0C8FD6'],
  launcher: ['#F2B807', '#0C8FD6', '#4A4FD9'],
  events: ['#F06A12', '#E0A815', '#1E62DA'],
  macros: ['#D9363E', '#E0A815', '#4A4FD9'],
  kboard: ['#E0A815', '#4A4FD9', '#0C9A87'],
  voice: ['#0C9A87', '#E0A815', '#0C8FD6'],
  remote: ['#0C8FD6', '#E0A815', '#7B3FE4'],
  progress: ['#E0A815', '#209E3E', '#DB3F86'],
  settings: ['#A9B2BD', '#E0A815', '#1E62DA'],
  assistants: ['#7B3FE4', '#E0A815', '#0C9A87'],
  outro: ['#E0A815', '#7a4a10', '#1d2a5a'],
};
