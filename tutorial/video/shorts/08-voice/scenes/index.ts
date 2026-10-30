import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Commands } from './Commands';
import { Dictation } from './Dictation';
import { Hook } from './Hook';
import { Setup } from './Setup';
import { Tip } from './Tip';

/** Tutorial 08, «Voice control»: setting it up, commands by voice, dictation, and talking hands free. */
export const VOICE: Short = {
  scenes: { hook: Hook, setup: Setup, commands: Commands, dictation: Dictation, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.voice,
    setup: MOODS.settings,
    commands: MOODS.voice,
    dictation: MOODS.launcher,
    tip: MOODS.voice,
    end: MOODS.outro,
  },
};
