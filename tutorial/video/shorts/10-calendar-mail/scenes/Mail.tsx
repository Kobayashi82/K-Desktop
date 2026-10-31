import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);

/** Settings › Mail (settings-mail.png): what it is for, and «Add account». */
const Intro: React.FC = () => {
  const B = useBeats();
  const w = B('what');
  const add = B('add');
  const focus: Focus[] = [
    { from: w.cue('settings') - 4, to: w.cue('matters') - 4, rect: [261, 913, 266, 48] },
    { from: w.cue('matters') - 4, to: add.from - 6, rect: [578, 128, 566, 22] },
    { from: add.from - 6, to: add.from + 30, rect: [580, 181, 124, 32], label: t('Add account'), at: 'right' },
  ];
  return (
    <>
      <CaptureShot name="settings-mail" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: w.cue('matters') - 16, dur: 30, x: 1000, y: 260, s: 1.6 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** «New mail account» (mail-account.png): the address, the servers, the app password and «Test». */
const Account: React.FC = () => {
  const B = useBeats();
  const a = B('add');
  const focus: Focus[] = [
    { from: a.cue('address') - 4, to: a.cue('servers') - 4, rect: [20, 100, 566, 36] },
    { from: a.cue('servers') - 4, to: a.cue('password') - 4, rect: [20, 505, 566, 125] },
    { from: a.cue('password') - 4, to: a.cue('guide') - 4, rect: [20, 352, 566, 76] },
    { from: a.cue('guide') - 4, to: a.cue('test') - 4, rect: [20, 434, 156, 16], label: t('How to get an app password'), at: 'right' },
    { from: a.cue('test') - 4, to: a.to + 30, rect: [20, 808, 70, 32], label: t('Test'), at: 'right' },
  ];
  return (
    <>
      <CaptureShot name="mail-account" frame focus={focus}
        shots={[{ at: a.from, x: 310, y: 430, s: 1.05 }, { at: a.from + 6, dur: 30, x: 310, y: 430, s: 1.15 }]} />
      {blips(focus)}
    </>
  );
};

const NOTIFY = [
  { cue: 'only', label: t('Only important ones') },
  { cue: 'every', label: t('Every new mail') },
  { cue: '', label: t('Nothing (only events and assistant)') },
];

/** Each account's «Notify about», drawn with its real choices. */
const Notify: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const imp = B('important');
  const card = glide(f, imp.from - 4, 22);
  const hot = f >= imp.cue('only') - 4 ? 0 : f >= imp.cue('every') - 4 ? 1 : -1;
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{
        position: 'absolute', left: 960, top: 520, width: 860, padding: '30px 34px', borderRadius: 24, boxSizing: 'border-box',
        transform: `translate(-50%, -50%) translateY(${(1 - card) * 60}px)`, opacity: card,
        background: 'linear-gradient(180deg, rgba(44,44,52,0.98), rgba(30,30,36,0.98))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 18, marginBottom: 26 }}>
          <KdIcon icon="mail_24_filled" color="blue" size={56} />
          <div>
            <div style={{ fontSize: 28, fontWeight: 700, color: C.text }}>{t('Personal')}</div>
            <div style={{ fontSize: 20, color: C.dim }}>alex.morgan@gmail.com</div>
          </div>
        </div>
        <div style={{ fontSize: 20, color: C.dim, marginBottom: 10 }}>{t('Notify about')}</div>
        <div style={{ borderRadius: 12, padding: 6, background: 'rgba(255,255,255,0.05)', border: `1px solid ${C.line2}` }}>
          {NOTIFY.map((n, i) => (
            <div key={n.label} style={{
              display: 'flex', alignItems: 'center', height: 56, padding: '0 18px', borderRadius: 8, fontSize: 24, color: i === hot ? C.text : C.dim,
              background: i === hot ? C.accentSoft : 'transparent', borderLeft: `4px solid ${i === hot ? C.accentText : 'transparent'}`,
            }}>
              {n.label}
              {i === hot && <Fluent name="checkmark_24_filled" size={24} color={C.accentText} style={{ marginLeft: 'auto' }} />}
            </div>
          ))}
        </div>
      </div>
      <Sfx at={imp.cue('every') - 4} name="blip" volume={0.14} />
      <Sfx at={imp.cue('only') - 4} name="blip" volume={0.14} />
    </AbsoluteFill>
  );
};

/** Settings › Mail: the important senders and words, then how a new mail is told. */
const Important: React.FC = () => {
  const B = useBeats();
  const imp = B('important');
  const n = B('notice');
  const focus: Focus[] = [
    { from: imp.cue('senders') - 4, to: imp.cue('words') - 4, rect: [580, 367, 942, 36] },
    { from: imp.cue('words') - 4, to: n.from, rect: [580, 431, 942, 36] },
    { from: n.cue('how') - 4, to: n.to + 40, rect: [580, 588, 942, 40] },
  ];
  return (
    <>
      <CaptureShot name="settings-mail" focus={focus}
        shots={[
          { at: imp.cue('senders') - 14, x: 1050, y: 400, s: 1.3 },
          { at: imp.cue('senders') - 10, dur: 26, x: 1050, y: 420, s: 1.45 },
          { at: n.cue('how') - 18, dur: 24, x: 1050, y: 560, s: 1.45 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** Chapter «Your mail». */
export const Mail: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const add = B('add');
  const imp = B('important');
  const lists = imp.cue('senders') - 14;
  return (
    <AbsoluteFill>
      <Part from={0} to={add.from + 6} first><Intro /></Part>
      <Part from={add.from + 6} to={imp.from - 4}><Account /></Part>
      <Part from={imp.from - 4} to={lists}><Notify /></Part>
      <Part from={lists} to={durationInFrames} last><Important /></Part>
    </AbsoluteFill>
  );
};
