import React from 'react';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { blips } from './Builtin';

/** Chapter «One more thing»: Tools › Share (tools-share.png), for Claude Desktop, VS Code or Open WebUI. */
export const Tip: React.FC = () => {
  const B = useBeats();
  const tip = B('tip');
  const focus: Focus[] = [
    { from: tip.cue('share') - 4, to: tip.cue('programs') + 4, rect: [261, 888, 266, 30] },
    { from: tip.cue('programs') + 4, to: tip.cue('copy') - 4, rect: [578, 134, 706, 18] },
    { from: tip.cue('copy') - 4, to: tip.to + 30, rect: [580, 313, 188, 36], label: t('Copy the configuration'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="tools-share" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: tip.cue('programs') - 6, dur: 26, x: 1000, y: 260, s: 1.6 },
        ]} />
      {blips(focus)}
    </>
  );
};
