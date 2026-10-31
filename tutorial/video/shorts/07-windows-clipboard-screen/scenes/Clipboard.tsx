import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Cursor } from '../../../src/components/Cursor';
import { Keycap } from '../../../src/components/Keys';
import { Sfx } from '../../../src/components/Sfx';
import { ease, inOut, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

type R = Focus['rect'];
// The real clipboard history (tutorial/captures/en/clipboard.png) and Settings › Clipboard (settings-clipboard.png).
const POPUP: R = [835, 622, 555, 418];
const ROWS: R = [840, 712, 545, 295];
const SEARCH: R = [845, 638, 530, 30];
const FILTERS: R = [846, 678, 250, 22];
const FIRST: R = [840, 712, 545, 46];
const HINTS: R = [846, 1014, 500, 20];
const NUMBERS: R = [1334, 716, 48, 280];
const PIN: R = [1310, 722, 22, 22];
const INTRO: R = [578, 126, 934, 24];
const EXCLUDED: R = [578, 515, 944, 80];

/** The history over the windows: what it keeps, how it opens, searching, filters and its keys. */
const History: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const intro = B('intro');
  const open = B('open');
  const k = B('keys');
  const focus: Focus[] = [
    { from: intro.cue('history') - 4, to: intro.cue('many') - 4, rect: POPUP },
    { from: intro.cue('many') - 4, to: open.cue('mouse') - 4, rect: ROWS, label: t('The last 200'), at: 'left' },
    { from: open.cue('mouse') - 4, to: open.cue('type') - 4, rect: POPUP },
    { from: open.cue('type') - 4, to: open.cue('filter') - 4, rect: SEARCH },
    { from: open.cue('filter') - 4, to: open.cue('enter') - 4, rect: FILTERS },
    { from: open.cue('enter') - 4, to: k.from, rect: FIRST, label: t('Enter pastes it'), at: 'left' },
    { from: k.cue('plain') - 4, to: k.cue('number') - 4, rect: HINTS },
    { from: k.cue('number') - 4, to: k.cue('pin') - 4, rect: NUMBERS, label: t('Ctrl + 1…9'), at: 'right' },
    { from: k.cue('pin') - 4, to: k.to + 20, rect: PIN, label: t('Pinned'), at: 'top' },
  ];
  const combo = pop(f, open.cue('winv') - 8, { damping: 14 });
  const comboOut = 1 - ease(f, open.cue('mouse') - 10, open.cue('mouse'));
  const tap = inOut(f, open.cue('winv') + 6, open.cue('winv') + 16, 4);
  const pointer = inOut(f, open.cue('mouse') - 8, open.cue('type'), 8);
  return (
    <>
      <CaptureShot name="clipboard" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: intro.cue('history') - 16, dur: 30, x: 1112, y: 760, s: 1.65 },
        ]}>
        <div style={{
          position: 'absolute', left: 1112, top: 520, transform: `translate(-50%, -50%) scale(${combo * 0.8})`, opacity: Math.min(1, combo * 1.4) * comboOut,
          display: 'flex', alignItems: 'center', gap: 14,
        }}>
          <Keycap label="Win" down={tap} />
          <span style={{ fontFamily: SANS, fontSize: 30, color: C.text }}>+</span>
          <Keycap label="V" down={tap} />
        </div>
        <Cursor path={[{ at: 0, x: 880, y: 600 }]} show={pointer} />
      </CaptureShot>
      {focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />)}
      <Sfx at={open.cue('winv') + 6} name="key" volume={0.3} />
    </>
  );
};

/** Settings › Clipboard: encrypted on this PC, and the programs whose copies are never kept. */
const Private: React.FC = () => {
  const B = useBeats();
  const p = B('private');
  const focus: Focus[] = [
    { from: p.cue('encrypted') - 4, to: p.cue('skips') - 4, rect: INTRO },
    { from: p.cue('skips') - 4, to: p.to + 40, rect: EXCLUDED },
  ];
  return (
    <>
      <CaptureShot name="settings-clipboard" focus={focus}
        shots={[
          { at: p.from - 4, x: 960, y: 540, s: 1.0 },
          { at: p.from, dur: 30, x: 1050, y: 330, s: 1.5 },
          { at: p.cue('skips') - 18, dur: 28, x: 1050, y: 540, s: 1.5 },
        ]} />
      {focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />)}
    </>
  );
};

/** Chapter «Clipboard history». */
export const Clipboard: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const p = B('private');
  return (
    <AbsoluteFill>
      <Part from={0} to={p.from - 4} first><History /></Part>
      <Part from={p.from - 4} to={durationInFrames} last><Private /></Part>
    </AbsoluteFill>
  );
};
