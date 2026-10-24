import { loadFont as loadInter } from '@remotion/google-fonts/Inter';
import { loadFont as loadMono } from '@remotion/google-fonts/JetBrainsMono';

export const { fontFamily: SANS } = loadInter('normal', { weights: ['400', '500', '600', '700', '800'], subsets: ['latin', 'latin-ext'] });
export const { fontFamily: MONO } = loadMono('normal', { weights: ['400', '600'], subsets: ['latin', 'latin-ext'] });

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
