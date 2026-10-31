import React from 'react';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { blips } from './Install';

/** Chapter «One more thing»: «Also show the commands you launch» in Settings › Assistant (assistant-settings.png). */
export const Tip: React.FC = () => {
  const B = useBeats();
  const tip = B('tip');
  const focus: Focus[] = [
    { from: tip.cue('show') - 4, to: tip.cue('how') - 4, rect: [575, 550, 950, 40], label: t('Also show the commands you launch'), at: 'top' },
    { from: tip.cue('how') - 4, to: tip.cue('nodelay') - 4, rect: [575, 570, 590, 20], label: t('A hotkey, a QuickMenu, your phone…'), at: 'bottom' },
    { from: tip.cue('nodelay') - 4, to: tip.to + 30, rect: [575, 570, 590, 20], label: t('Without delaying anything'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="assistant-settings" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: tip.cue('show') - 16, dur: 26, x: 1050, y: 560, s: 1.6 },
        ]} />
      {blips(focus)}
    </>
  );
};
