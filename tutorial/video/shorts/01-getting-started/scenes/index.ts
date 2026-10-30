import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Download } from './Download';
import { First } from './First';
import { Hook } from './Hook';
import { Tip } from './Tip';
import { Tour } from './Tour';
import { Welcome } from './Welcome';

/** Tutorial 01, «Getting started»: download, the welcome, a tour of the pages and the first command. */
export const GETTING_STARTED: Short = {
  scenes: { hook: Hook, download: Download, welcome: Welcome, tour: Tour, first: First, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.intro,
    download: MOODS.launcher,
    welcome: MOODS.overview,
    tour: MOODS.settings,
    first: MOODS.commands,
    tip: MOODS.kboard,
    end: MOODS.outro,
  },
};
