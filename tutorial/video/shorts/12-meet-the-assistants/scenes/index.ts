import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Hook } from './Hook';
import { Install } from './Install';
import { Own } from './Own';
import { Talk } from './Talk';
import { Tip } from './Tip';
import { Work } from './Work';

/** Tutorial 12, «Meet the assistants»: adding one, talking to it, how it works, and what each one brings. */
export const MEET_THE_ASSISTANTS: Short = {
  scenes: { hook: Hook, install: Install, talk: Talk, work: Work, own: Own, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.assistants,
    install: MOODS.settings,
    talk: MOODS.voice,
    work: MOODS.commands,
    own: MOODS.assistants,
    tip: MOODS.quickmenu,
    end: MOODS.outro,
  },
};
