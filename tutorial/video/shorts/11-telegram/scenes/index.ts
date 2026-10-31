import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Connect } from './Connect';
import { Hook } from './Hook';
import { People } from './People';
import { Tip } from './Tip';
import { Use } from './Use';

/** Tutorial 11, «Telegram»: the bot or the account, who can write to it, and what it can do. */
export const TELEGRAM: Short = {
  scenes: { hook: Hook, connect: Connect, people: People, use: Use, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.remote,
    connect: MOODS.settings,
    people: MOODS.quickmenu,
    use: MOODS.remote,
    tip: MOODS.launcher,
    end: MOODS.outro,
  },
};
