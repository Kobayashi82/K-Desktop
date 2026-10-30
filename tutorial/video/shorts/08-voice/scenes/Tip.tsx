import React from 'react';
import { AbsoluteFill } from 'remotion';
import { CaptureShot } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { TalkPart } from '../../../src/scenes/voice/Talk';
import { WizardPart } from '../../../src/scenes/voice/Wizard';

/** Chapter «Hands free»: hold or tap the key, the wake word, and recording your own (the long video's scenes). */
export const Tip: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const own = B('own');
  const wakewords = own.cue('wakewords');
  const wizardFrom = wakewords + 46;
  return (
    <AbsoluteFill>
      <Part from={0} to={own.from - 6} first><TalkPart intro={B('intro')} two={B('two')} wake={B('wake')} /></Part>
      {/* Settings › Wake words, on the real page */}
      <Part from={own.from - 6} to={wizardFrom}>
        <CaptureShot name="settings-wake"
          shots={[{ at: own.from - 6, x: 960, y: 540, s: 1.0 }, { at: own.from, dur: 40, x: 760, y: 420, s: 1.35 }]}
          focus={[{ from: wakewords - 8, to: wizardFrom + 10, rect: [262, 170, 266, 48] }]} />
      </Part>
      <Part from={wizardFrom} to={durationInFrames} last><WizardPart own={own} /></Part>
    </AbsoluteFill>
  );
};
