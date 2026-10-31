import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { Bubble } from '../../../src/scenes/assistants/Inside';
import { C, SANS } from '../../../src/theme';

export const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);
const NAV: Focus['rect'] = [261, 1016, 266, 46];

/** Settings › Memory (memory.png): shared by all, and learning from what you talk about. */
const Learn: React.FC = () => {
  const B = useBeats();
  const l = B('learn');
  const focus: Focus[] = [
    { from: l.cue('settings') - 4, to: l.cue('know') - 4, rect: NAV },
    { from: l.cue('know') - 4, to: l.cue('learn') - 4, rect: [578, 128, 410, 20] },
    { from: l.cue('learn') - 4, to: l.to + 20, rect: [580, 165, 942, 40] },
  ];
  return (
    <>
      <CaptureShot name="memory" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: l.cue('know') - 16, dur: 26, x: 1050, y: 330, s: 1.5 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** What you can say: remember, what do you remember, forget; and the new memory it keeps. */
const Say: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const s = B('say');
  const row = pop(f, s.cue('remember') + 24, { damping: 14 });
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <Bubble user text={t('Remember that I’m allergic to seafood')} show={pop(f, s.cue('remember') - 6, { damping: 14 })} x={1760} y={220} size={34} />
      <div style={{
        position: 'absolute', left: 160, top: 320, width: 900, display: 'flex', alignItems: 'center', gap: 18, padding: '20px 24px', borderRadius: 18,
        background: 'rgba(30,30,36,0.95)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, row * 1.4), transform: `translateY(${(1 - row) * 30}px)`,
      }}>
        <KdIcon icon="person_voice_24_filled" color="purple" size={50} />
        <div>
          <div style={{ fontSize: 28, fontWeight: 600, color: C.text }}>{t('Alex is allergic to seafood.')}</div>
          <div style={{ fontSize: 19, color: C.dim }}>{t('You asked · today')}</div>
        </div>
      </div>
      <Bubble user text={t('What do you remember about me?')} show={pop(f, s.cue('what') - 6, { damping: 14 })} x={1760} y={560} size={34} />
      <Bubble user text={t('Forget that')} show={pop(f, s.cue('forget') - 6, { damping: 14 })} x={1760} y={720} size={34} />
      <Sfx at={s.cue('remember') - 6} name="pop" volume={0.2} />
      <Sfx at={s.cue('remember') + 24} name="chime" volume={0.16} />
      <Sfx at={s.cue('what') - 6} name="pop" volume={0.2} />
      <Sfx at={s.cue('forget') - 6} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Editing and deleting on the Memory page. */
const Edit: React.FC = () => {
  const B = useBeats();
  const s = B('say');
  return (
    <>
      <CaptureShot name="memory" focus={[{ from: s.cue('edit') - 4, to: s.to + 20, rect: [1452, 272, 72, 340], label: t('Edit · Delete'), at: 'left' }]}
        shots={[{ at: s.cue('edit') - 10, x: 1200, y: 440, s: 1.4 }, { at: s.cue('edit') - 6, dur: 24, x: 1250, y: 440, s: 1.6 }]} />
      <Sfx at={s.cue('edit') - 4} name="blip" volume={0.12} />
    </>
  );
};

/** Settings › Notes (notes.png): what you asked them to note down. */
const Notes: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const n = B('notes');
  const focus: Focus[] = [
    { from: n.cue('notes') - 2, to: n.cue('say') - 4, rect: [578, 128, 580, 20] },
    { from: n.cue('say') + 16, to: n.to + 30, rect: [580, 258, 942, 40] },
  ];
  return (
    <AbsoluteFill>
      <CaptureShot name="notes" focus={focus}
        shots={[{ at: n.from - 6, x: 1050, y: 300, s: 1.3 }, { at: n.from, dur: 26, x: 1050, y: 300, s: 1.5 }]} />
      <Bubble user text={t('Note that: call the landlord')} show={pop(f, n.cue('say') - 6, { damping: 14 })} x={1780} y={820} size={34} />
      {blips(focus)}
      <Sfx at={n.cue('say') - 6} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Chapter «Memory and notes». */
export const Memory: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const s = B('say');
  const n = B('notes');
  return (
    <AbsoluteFill>
      <Part from={0} to={s.from - 4} first><Learn /></Part>
      <Part from={s.from - 4} to={s.cue('edit') - 8}><Say /></Part>
      <Part from={s.cue('edit') - 8} to={n.from - 4}><Edit /></Part>
      <Part from={n.from - 4} to={durationInFrames} last><Notes /></Part>
    </AbsoluteFill>
  );
};
