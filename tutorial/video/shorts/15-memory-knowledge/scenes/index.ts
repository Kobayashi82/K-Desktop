import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Hook } from './Hook';
import { Knows } from './Knows';
import { Memory } from './Memory';
import { Tip } from './Tip';

/** Tutorial 15, «Memory, notes and what it knows»: what they remember, your notes, your documents, and dropped files. */
export const MEMORY_KNOWLEDGE: Short = {
  scenes: { hook: Hook, memory: Memory, knows: Knows, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.assistants,
    memory: MOODS.quickmenu,
    knows: MOODS.settings,
    tip: MOODS.assistants,
    end: MOODS.outro,
  },
};
