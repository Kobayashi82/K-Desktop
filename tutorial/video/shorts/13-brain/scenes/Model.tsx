import React from 'react';
import { AbsoluteFill } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Sfx } from '../../../src/components/Sfx';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';

type R = Focus['rect'];
// The real Settings › Brain (tutorial/captures/en/brain.png).
export const BRAIN = {
  nav: [261, 782, 266, 46] as R,
  status: [580, 156, 942, 48] as R,
  model: [580, 222, 942, 80] as R,
  think: [580, 318, 942, 82] as R,
  unload: [580, 607, 942, 40] as R,
  games: [580, 660, 942, 44] as R,
};
export const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);

/** Settings › Brain: the model in memory, loading by itself, and «Automatic». */
const Brain: React.FC = () => {
  const B = useBeats();
  const w = B('where');
  const focus: Focus[] = [
    { from: w.cue('settings') - 4, to: w.cue('loads') - 4, rect: BRAIN.nav },
    { from: w.cue('loads') - 4, to: w.cue('auto') - 4, rect: BRAIN.status, label: t('It loads by itself'), at: 'bottom' },
    { from: w.cue('auto') - 4, to: w.to + 20, rect: BRAIN.model, label: t('The biggest one that fits'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="brain" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: w.cue('loads') - 16, dur: 30, x: 1050, y: 300, s: 1.55 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** «Add a model from Hugging Face» (add-model.png). */
const Add: React.FC = () => {
  const B = useBeats();
  const a = B('add');
  const focus: Focus[] = [
    { from: a.cue('add') - 4, to: a.cue('hf') - 4, rect: [16, 10, 330, 30], label: t('Add model'), at: 'right' },
    { from: a.cue('hf') - 4, to: a.cue('fits') - 4, rect: [24, 92, 552, 36] },
    { from: a.cue('fits') - 4, to: a.cue('vision') - 4, rect: [24, 136, 456, 22], label: t('The biggest that fits is already chosen'), at: 'bottom' },
    { from: a.cue('vision') - 4, to: a.cue('download') - 4, rect: [32, 594, 354, 28] },
    { from: a.cue('download') - 4, to: a.to + 30, rect: [467, 640, 109, 32] },
  ];
  return (
    <>
      <CaptureShot name="add-model" frame focus={focus}
        shots={[{ at: a.from - 6, x: 300, y: 346, s: 1.2 }, { at: a.from, dur: 30, x: 300, y: 346, s: 1.4 }]} />
      {blips(focus)}
    </>
  );
};

/** «To think harder». */
const Think: React.FC = () => {
  const B = useBeats();
  const th = B('think');
  const focus: Focus[] = [
    { from: th.cue('think') - 4, to: th.cue('over') - 4, rect: BRAIN.think },
    { from: th.cue('over') - 4, to: th.to + 30, rect: BRAIN.think, label: t('«Think it over»'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="brain" focus={focus}
        shots={[{ at: th.from - 6, x: 1050, y: 380, s: 1.3 }, { at: th.from, dur: 30, x: 1050, y: 380, s: 1.55 }]} />
      {blips(focus)}
    </>
  );
};

/** Chapter «Choose a model». */
export const Model: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const a = B('add');
  const th = B('think');
  return (
    <AbsoluteFill>
      <Part from={0} to={a.from - 4} first><Brain /></Part>
      <Part from={a.from - 4} to={th.from - 4}><Add /></Part>
      <Part from={th.from - 4} to={durationInFrames} last><Think /></Part>
    </AbsoluteFill>
  );
};
