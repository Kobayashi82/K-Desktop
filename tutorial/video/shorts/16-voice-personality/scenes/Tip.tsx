import React from 'react';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { blips } from './Voices';

/** Chapter «One more thing»: «Try it» in Assistant module › Voice (assistant-voice.png). */
export const Tip: React.FC = () => {
  const B = useBeats();
  const tip = B('tip');
  const focus: Focus[] = [
    { from: tip.cue('try') - 4, to: tip.cue('who') - 4, rect: [580, 490, 942, 62], label: t('Try it'), at: 'top' },
    { from: tip.cue('who') - 4, to: tip.cue('how') - 4, rect: [580, 560, 466, 58], label: t('Who says it'), at: 'bottom' },
    { from: tip.cue('how') - 4, to: tip.to + 30, rect: [1055, 560, 466, 58], label: t('How it says it'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="assistant-voice" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: tip.cue('try') - 16, dur: 26, x: 1050, y: 560, s: 1.6 },
        ]} />
      {blips(focus)}
    </>
  );
};
