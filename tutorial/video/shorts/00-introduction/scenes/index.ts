import { ShortEnd } from '../../../src/components/Short';
import { Intro } from '../../../src/scenes/Intro';
import { Overview } from '../../../src/scenes/Overview';
import type { Short } from '../../../src/shorts';
import { MOODS } from '../../../src/theme';

/** Tutorial 00, «Introduction»: the long video's opening and its big picture, without the phone and with today's five languages. */
export const INTRODUCTION: Short = {
  scenes: { intro: Intro, overview: Overview, end: ShortEnd },
  moods: { intro: MOODS.intro, overview: MOODS.overview, end: MOODS.outro },
};
