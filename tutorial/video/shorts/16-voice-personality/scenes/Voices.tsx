import React from 'react';
import { AbsoluteFill } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Sfx } from '../../../src/components/Sfx';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';

export const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);

/** Assistant module › Voice (assistant-voice.png): who speaks, natural voices offline. */
const Who: React.FC = () => {
  const B = useBeats();
  const w = B('who');
  const row: Focus['rect'] = [580, 217, 942, 40];
  const focus: Focus[] = [
    { from: w.cue('settings') - 4, to: w.cue('who') - 4, rect: [261, 955, 266, 46] },
    { from: w.cue('who') - 4, to: w.cue('kokoro') - 4, rect: row },
    { from: w.cue('kokoro') - 4, to: w.cue('piper') - 4, rect: row, label: t('1 · Kokoro'), at: 'bottom' },
    { from: w.cue('piper') - 4, to: w.cue('windows') - 4, rect: row, label: t('2 · Piper'), at: 'bottom' },
    { from: w.cue('windows') - 4, to: w.cue('offline') - 4, rect: row, label: t('3 · Windows voices'), at: 'bottom' },
    { from: w.cue('offline') - 4, to: w.to + 20, rect: [578, 130, 640, 18] },
  ];
  return (
    <>
      <CaptureShot name="assistant-voice" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: w.cue('who') - 16, dur: 26, x: 1050, y: 260, s: 1.6 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** Voice › Voices (assistant-voices.png): the Kokoro voice, mixing two, your own folders. */
const Pick: React.FC = () => {
  const B = useBeats();
  const p = B('pick');
  const focus: Focus[] = [
    { from: p.cue('page') - 4, to: p.cue('kokoro') - 4, rect: [261, 1005, 266, 28] },
    { from: p.cue('kokoro') - 4, to: p.cue('mix') - 4, rect: [580, 285, 942, 40] },
    { from: p.cue('mix') - 4, to: p.cue('folders') - 4, rect: [580, 332, 942, 40] },
    { from: p.cue('folders') - 4, to: p.to + 20, rect: [580, 222, 942, 52] },
  ];
  return (
    <>
      <CaptureShot name="assistant-voices" focus={focus}
        shots={[{ at: p.from - 6, x: 960, y: 540, s: 1.0 }, { at: p.cue('kokoro') - 18, dur: 26, x: 1050, y: 300, s: 1.6 }]} />
      {blips(focus)}
    </>
  );
};

/** «When they speak aloud» (assistant-voice.png). */
const When: React.FC = () => {
  const B = useBeats();
  const w = B('when');
  const focus: Focus[] = [
    { from: w.cue('when') - 4, to: w.cue('quiet') - 4, rect: [564, 648, 976, 220] },
    { from: w.cue('quiet') - 4, to: w.cue('night') - 4, rect: [580, 707, 942, 40] },
    { from: w.cue('night') - 4, to: w.cue('side') - 4, rect: [580, 759, 942, 40] },
    { from: w.cue('side') - 4, to: w.to + 30, rect: [580, 811, 942, 40] },
  ];
  return (
    <>
      <CaptureShot name="assistant-voice" focus={focus}
        shots={[{ at: w.from - 6, x: 1050, y: 740, s: 1.3 }, { at: w.from, dur: 26, x: 1050, y: 760, s: 1.55 }]} />
      {blips(focus)}
    </>
  );
};

/** Chapter «The voices». */
export const Voices: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const p = B('pick');
  const w = B('when');
  return (
    <AbsoluteFill>
      <Part from={0} to={p.from - 4} first><Who /></Part>
      <Part from={p.from - 4} to={w.from - 4}><Pick /></Part>
      <Part from={w.from - 4} to={durationInFrames} last><When /></Part>
    </AbsoluteFill>
  );
};
