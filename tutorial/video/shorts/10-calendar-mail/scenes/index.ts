import { ShortEnd } from '../../../src/components/Short';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';
import { Agenda } from './Agenda';
import { Connect } from './Connect';
import { Hook } from './Hook';
import { Mail } from './Mail';
import { Tip } from './Tip';

/** Tutorial 10, «Calendar and mail»: the Agenda, your calendars (own, .ics, Google) and your mail accounts. */
export const CALENDAR_MAIL: Short = {
  scenes: { hook: Hook, agenda: Agenda, connect: Connect, mail: Mail, tip: Tip, end: ShortEnd },
  moods: {
    hook: MOODS.intro,
    agenda: MOODS.events,
    connect: MOODS.settings,
    mail: MOODS.remote,
    tip: MOODS.events,
    end: MOODS.outro,
  },
};
