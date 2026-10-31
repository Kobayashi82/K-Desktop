import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Code } from './Code';
import { Hook } from './Hook';
import { NoCode } from './NoCode';
import { Share } from './Share';
import { Tip } from './Tip';
import { Try } from './Try';

/** Tutorial 17, «Create your assistant»: without code, with the SDK, trying it with --assistant-dev, signing and sharing it. */
export const CREATE_YOUR_ASSISTANT: Short = {
  scenes: { hook: Hook, nocode: NoCode, code: Code, try: Try, share: Share, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.assistants,
    nocode: MOODS.voice,
    code: MOODS.commands,
    try: MOODS.kboard,
    share: MOODS.events,
    tip: MOODS.settings,
    end: MOODS.outro,
  },
};
