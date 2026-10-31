import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Gpu } from './Gpu';
import { Hook } from './Hook';
import { Model } from './Model';
import { Servers } from './Servers';
import { Tip } from './Tip';

/** Tutorial 13, «The assistant's brain»: the model, other servers, and sharing the graphics card. */
export const BRAIN: Short = {
  scenes: { hook: Hook, model: Model, servers: Servers, gpu: Gpu, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.assistants,
    model: MOODS.settings,
    servers: MOODS.remote,
    gpu: MOODS.progress,
    tip: MOODS.assistants,
    end: MOODS.outro,
  },
};
