import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);

/** Settings › Calendar (settings-calendars.png): your own calendars and the «Subscribe to an .ics» button. */
const Own: React.FC = () => {
  const B = useBeats();
  const own = B('own');
  const ics = B('ics');
  const focus: Focus[] = [
    { from: own.cue('settings') - 4, to: own.cue('own') - 4, rect: [261, 962, 266, 48] },
    { from: own.cue('own') - 4, to: own.cue('newcal') - 4, rect: [578, 226, 946, 110] },
    { from: own.cue('newcal') - 4, to: own.cue('reminder') - 4, rect: [580, 349, 130, 32] },
    { from: own.cue('reminder') - 4, to: ics.from, rect: [1226, 250, 200, 80], label: t('Reminder for new events'), at: 'bottom' },
    { from: ics.cue('subscribe') - 4, to: ics.to, rect: [718, 349, 163, 32] },
  ];
  return (
    <>
      <CaptureShot name="settings-calendars" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: own.cue('own') - 16, dur: 30, x: 1050, y: 300, s: 1.55 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** Typed text, letter by letter from `at`. */
const typed = (f: number, text: string, at: number, speed = 1.6) => text.slice(0, Math.max(0, Math.floor((f - at) * speed)));

/** The «New subscription» window, drawn with its real texts: a name, the secret address, kept encrypted. */
const Subscription: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const ics = B('ics');
  const at = ics.cue('holidays');
  const card = glide(f, at - 14, 22);
  const lock = pop(f, ics.cue('encrypted') - 10, { damping: 13 });
  const sources = [t('Google Calendar'), t('Outlook'), t('Holidays and sports')];
  const field = (label: string, value: string, extra?: React.ReactNode) => (
    <div style={{ marginBottom: 22 }}>
      <div style={{ fontSize: 20, color: C.dim, marginBottom: 8 }}>{label}</div>
      <div style={{ display: 'flex', alignItems: 'center', height: 56, padding: '0 18px', borderRadius: 10, fontSize: 23, color: C.text, background: 'rgba(255,255,255,0.06)', border: `1px solid ${C.line2}` }}>
        {value}<span style={{ opacity: f % 30 < 15 ? 0.8 : 0 }}>▍</span>{extra}
      </div>
    </div>
  );
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 130, display: 'flex', justifyContent: 'center', gap: 18 }}>
        {sources.map((s, i) => {
          const k = pop(f, at - 30 + i * 10, { damping: 14 });
          return (
            <div key={s} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '12px 22px', borderRadius: 16, background: 'rgba(40,40,48,0.94)', border: `1.5px solid ${C.line2}`, fontSize: 26, fontWeight: 600, color: C.text, opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 30}px)` }}>
              <KdIcon icon={['calendar_ltr_24_filled', 'mail_24_filled', 'calendar_star_24_filled'][i]} color={['blue', 'sky', 'orange'][i]} size={40} /> {s}
            </div>
          );
        })}
      </div>
      <div style={{
        position: 'absolute', left: 960, top: 600, width: 860, padding: '30px 34px', borderRadius: 24, boxSizing: 'border-box', transform: `translate(-50%, -50%) translateY(${(1 - card) * 60}px)`,
        opacity: card, background: 'linear-gradient(180deg, rgba(44,44,52,0.98), rgba(30,30,36,0.98))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
      }}>
        <div style={{ fontSize: 26, fontWeight: 700, color: C.text, marginBottom: 24 }}>{t('New subscription')}</div>
        {field(t('Name (Holidays, Personal Google…)'), typed(f, t('Holidays'), at + 6))}
        {field(t('Address'), typed(f, 'webcal://calendars.example.com/holidays.ics', at + 24, 2.2), (
          <span style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 8, padding: '6px 14px', borderRadius: 10, background: C.accentSoft, color: C.accentText, fontSize: 18, fontWeight: 700, opacity: Math.min(1, lock * 1.4), transform: `scale(${lock})` }}>
            <Fluent name="lock_closed_24_filled" size={20} color={C.accentText} /> {t('Kept encrypted')}
          </span>
        ))}
        <div style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
          <span style={{ fontSize: 19, color: C.accentText, textDecoration: 'underline' }}>{t('Where to find the .ics address')}</span>
          <div style={{ marginLeft: 'auto', padding: '12px 26px', borderRadius: 8, background: C.accentText, color: '#1a1306', fontSize: 21, fontWeight: 600 }}>{t('Subscribe')}</div>
        </div>
      </div>
      <Sfx at={at - 14} name="whoosh-short" volume={0.22} />
      <Sfx at={ics.cue('encrypted') - 10} name="pop-high" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Settings › Accounts › Google (accounts-google.png): what for, «Connect Google», and your own Client ID. */
const Google: React.FC = () => {
  const B = useBeats();
  const g = B('google');
  const focus: Focus[] = [
    { from: g.cue('connect') - 4, to: g.cue('app') - 4, rect: [576, 180, 470, 112] },
    { from: g.cue('app') - 4, to: g.cue('guide') + 4, rect: [580, 458, 942, 48], label: t('Client ID'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="accounts-google" focus={focus}
        shots={[{ at: g.from - 4, x: 960, y: 540, s: 1.0 }, { at: g.from, dur: 30, x: 1050, y: 360, s: 1.45 }]} />
      {blips(focus)}
    </>
  );
};

/** «Your Google Client ID» (guide-google.png): the steps, done only once. */
const Guide: React.FC = () => {
  const B = useBeats();
  const g = B('google');
  const focus: Focus[] = [
    { from: g.cue('guide') + 4, to: g.cue('once') - 4, rect: [16, 84, 568, 300] },
    { from: g.cue('once') - 4, to: g.to + 30, rect: [16, 50, 570, 22] },
  ];
  return (
    <>
      <CaptureShot name="guide-google" frame focus={focus}
        shots={[{ at: g.cue('guide') - 10, x: 300, y: 410, s: 1.05 }, { at: g.cue('guide'), dur: 30, x: 300, y: 380, s: 1.2 }]} />
      {blips(focus)}
    </>
  );
};

/** Chapter «Your calendars». */
export const Connect: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const ics = B('ics');
  const g = B('google');
  const sub = ics.cue('holidays') - 16;
  return (
    <AbsoluteFill>
      <Part from={0} to={sub} first><Own /></Part>
      <Part from={sub} to={g.from - 4}><Subscription /></Part>
      <Part from={g.from - 4} to={g.cue('guide') - 6}><Google /></Part>
      <Part from={g.cue('guide') - 6} to={durationInFrames} last><Guide /></Part>
    </AbsoluteFill>
  );
};
