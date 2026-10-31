import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Ask } from './Ask';
import { Builtin } from './Builtin';
import { Hook } from './Hook';
import { Own } from './Own';
import { Tip } from './Tip';

/** Tutorial 14, «The assistant's tools»: what they can do, when they ask first, and your own (files and MCP). */
export const TOOLS: Short = {
  scenes: { hook: Hook, builtin: Builtin, ask: Ask, own: Own, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.assistants,
    builtin: MOODS.settings,
    ask: MOODS.events,
    own: MOODS.commands,
    tip: MOODS.remote,
    end: MOODS.outro,
  },
};
