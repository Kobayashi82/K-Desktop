import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { Bubble } from '../../../src/scenes/assistants/Inside';
import { C, SANS } from '../../../src/theme';
import { blips } from './Memory';

/** Settings › What it knows (knowledge.png): folders and files, read once, scanned pages too. */
const Page: React.FC = () => {
  const B = useBeats();
  const a = B('add');
  const s = B('scanned');
  const q = B('ask');
  const focus: Focus[] = [
    { from: a.cue('knows') - 4, to: a.cue('add') - 4, rect: [261, 1016, 266, 46] },
    { from: a.cue('add') - 4, to: a.cue('read') - 4, rect: [580, 171, 240, 36] },
    { from: a.cue('read') - 4, to: a.cue('nothing') - 4, rect: [580, 222, 942, 108] },
    { from: a.cue('nothing') - 4, to: s.from, rect: [578, 128, 940, 34], label: t('Nothing leaves it'), at: 'bottom' },
    { from: s.cue('pictures') - 4, to: q.from, rect: [580, 350, 942, 40] },
    { from: q.cue('ask') - 4, to: q.cue('answers') - 10, rect: [580, 593, 942, 36] },
  ];
  return (
    <>
      <CaptureShot name="knowledge" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: a.cue('add') - 16, dur: 26, x: 1050, y: 300, s: 1.5 },
          { at: q.cue('ask') - 16, dur: 26, x: 1050, y: 520, s: 1.5 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** The answer, with where it comes from; then «Try a search» on the page. */
const Answer: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const q = B('ask');
  const src = pop(f, q.cue('where') - 6, { damping: 13 });
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <Bubble user text={t('How much was the electricity bill in October?')} show={glide(f, q.cue('answers') - 14, 16)} x={1760} y={300} size={34} />
      <Bubble text={t('October’s electricity bill was 64.20 €, due on November 5.')} show={pop(f, q.cue('answers') - 2, { damping: 14 })} x={160} y={500} size={34} width={1000} />
      <div style={{
        position: 'absolute', left: 160, top: 640, display: 'flex', alignItems: 'center', gap: 14, padding: '14px 24px', borderRadius: 16,
        background: C.accentSoft, color: C.accentText, fontSize: 26, fontWeight: 600, opacity: Math.min(1, src * 1.4), transform: `scale(${0.85 + 0.15 * src})`, transformOrigin: 'left center',
      }}>
        <Fluent name="document_pdf_24_filled" size={30} color={C.accentText} /> electricity-october.pdf · {t('page 1')}
      </div>
      <Sfx at={q.cue('answers') - 2} name="pop-high" volume={0.2} />
      <Sfx at={q.cue('where') - 6} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};

const Try: React.FC = () => {
  const B = useBeats();
  const q = B('ask');
  return (
    <>
      <CaptureShot name="knowledge" focus={[{ from: q.cue('try') - 4, to: q.to + 30, rect: [564, 528, 976, 116] }]}
        shots={[{ at: q.cue('try') - 12, x: 1050, y: 560, s: 1.35 }, { at: q.cue('try') - 8, dur: 24, x: 1050, y: 580, s: 1.5 }]} />
      <Sfx at={q.cue('try') - 4} name="blip" volume={0.12} />
    </>
  );
};

/** Chapter «What it knows». */
export const Knows: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const q = B('ask');
  return (
    <AbsoluteFill>
      <Part from={0} to={q.cue('answers') - 16} first><Page /></Part>
      <Part from={q.cue('answers') - 16} to={q.cue('try') - 10}><Answer /></Part>
      <Part from={q.cue('try') - 10} to={durationInFrames} last><Try /></Part>
    </AbsoluteFill>
  );
};
