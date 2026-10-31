import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Clipboard } from './Clipboard';
import { Hook } from './Hook';
import { Layouts } from './Layouts';
import { Screen } from './Screen';
import { Tip } from './Tip';

/** Tutorial 07, «Windows, clipboard and screen»: window layouts, the clipboard history and copying text from the screen. */
export const WINDOWS_CLIPBOARD_SCREEN: Short = {
  scenes: { hook: Hook, layouts: Layouts, clipboard: Clipboard, screen: Screen, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.intro,
    layouts: MOODS.kboard,
    clipboard: MOODS.macros,
    screen: MOODS.launcher,
    tip: MOODS.macros,
    end: MOODS.outro,
  },
};
