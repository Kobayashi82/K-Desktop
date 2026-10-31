import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Hook } from './Hook';
import { Personality } from './Personality';
import { Profiles } from './Profiles';
import { Tip } from './Tip';
import { Voices } from './Voices';

/** Tutorial 16, «Voice and personality»: the voices, voice profiles with effects, and each assistant's personality. */
export const VOICE_PERSONALITY: Short = {
  scenes: { hook: Hook, voices: Voices, profiles: Profiles, personality: Personality, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.voice,
    voices: MOODS.settings,
    profiles: MOODS.quickmenu,
    personality: MOODS.assistants,
    tip: MOODS.voice,
    end: MOODS.outro,
  },
};
