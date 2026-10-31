import React from 'react';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { blips } from './Voices';

const row = (i: number): Focus['rect'] => [595, 212 + 86 * i, 912, 74];

/** Chapter «Voice profiles» (voice-profiles.png): the built-in effects, duplicating one, «New at random». */
export const Profiles: React.FC = () => {
  const B = useBeats();
  const e = B('effects');
  const o = B('own');
  const names = ['robot', 'martian', 'radio', 'bit', 'beeps'];
  const focus: Focus[] = [
    { from: e.cue('profiles') - 2, to: e.cue('effects') - 4, rect: [261, 1036, 266, 28] },
    { from: e.cue('effects') - 4, to: e.cue('robot') - 4, rect: [580, 206, 942, 515] },
    ...names.map((n, i) => ({ from: e.cue(n) - 4, to: names[i + 1] ? e.cue(names[i + 1]) - 4 : o.from, rect: row(i + 1) })),
    { from: o.cue('duplicate') - 4, to: o.cue('random') - 4, rect: [1407, 232, 32, 464], label: t('Duplicate'), at: 'left' },
    { from: o.cue('random') - 4, to: o.to + 30, rect: [580, 149, 170, 36], label: t('New at random'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="voice-profiles" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: e.cue('effects') - 16, dur: 26, x: 1050, y: 460, s: 1.4 },
          { at: o.cue('random') - 18, dur: 24, x: 900, y: 260, s: 1.6 },
        ]} />
      {blips(focus)}
    </>
  );
};
