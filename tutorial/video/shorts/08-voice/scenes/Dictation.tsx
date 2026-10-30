import React from 'react';
import { AbsoluteFill } from 'remotion';
import { CaptureShot } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { Dictate } from '../../../src/scenes/voice/Uses';
import { VOICE_ROWS } from './Setup';

/** Chapter «Dictation»: the long video's dictation, then how to start it on the real Settings › Voice page. */
export const Dictation: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const start = B('start');
  const from = start.from - 6;
  return (
    <AbsoluteFill>
      <Part from={0} to={from} first><Dictate b={B('dictate')} /></Part>
      <Part from={from} to={durationInFrames} last>
        <CaptureShot name="settings-voice"
          shots={[{ at: from, x: 1050, y: 470, s: 1.2 }, { at: from + 4, dur: 40, x: 1050, y: 500, s: 1.5 }]}
          focus={[
            { from: start.cue('hotkey') - 6, to: start.cue('say') - 6, rect: VOICE_ROWS.dictate, label: t('Its own hotkey'), at: 'top' },
            { from: start.cue('say') - 6, to: durationInFrames, rect: VOICE_ROWS.dictateVoice, label: t('«start dictating»'), at: 'bottom' },
          ]} />
      </Part>
    </AbsoluteFill>
  );
};
