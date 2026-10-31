import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Osd } from '../../../src/components/Osd';
import { Phone } from '../../../src/components/Phone';
import { Sfx } from '../../../src/components/Sfx';
import { glide, inOut, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { REMINDER, SaysBubble, Toast } from './Notices';

type R = Focus['rect'];
const blips = (focus: Focus[]) => focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />);

/** The real Agenda (tutorial/captures/en/agenda.png), list view. */
const List: React.FC = () => {
  const B = useBeats();
  const l = B('list');
  const focus: Focus[] = [
    { from: l.cue('agenda') - 4, to: l.cue('all') - 4, rect: [12, 470, 216, 40] },
    { from: l.cue('all') - 4, to: l.cue('today') - 4, rect: [261, 193, 163, 28] },
    { from: l.cue('today') - 4, to: l.cue('now') - 4, rect: [262, 245, 1620, 140], label: t('Today'), at: 'bottom' },
    { from: l.cue('now') - 4, to: l.cue('join') - 4, rect: [319, 326, 1563, 53] },
    { from: l.cue('join') - 4, to: l.to + 30, rect: [1798, 433, 72, 32], label: t('Join'), at: 'left' },
  ];
  return (
    <>
      <CaptureShot name="agenda" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: l.cue('all') - 14, dur: 30, x: 900, y: 360, s: 1.4 },
          { at: l.cue('join') - 18, dur: 24, x: 1450, y: 420, s: 1.5 },
        ]} />
      {blips(focus)}
    </>
  );
};

/** The month view (agenda-month.png) and the calendar buttons. */
const Month: React.FC = () => {
  const B = useBeats();
  const m = B('month');
  const chips: R = [261, 193, 163, 28];
  const focus: Focus[] = [
    { from: m.cue('month') - 4, to: m.cue('colours') - 4, rect: [338, 148, 92, 34] },
    { from: m.cue('colours') - 4, to: m.cue('hide') - 4, rect: chips },
    { from: m.cue('hide') - 4, to: m.to + 30, rect: chips, label: t('A click hides or shows it'), at: 'right' },
  ];
  return (
    <>
      <CaptureShot name="agenda-month" focus={focus}
        shots={[
          { at: m.from - 4, x: 960, y: 540, s: 1.0 },
          { at: m.cue('month') - 10, dur: 30, x: 1000, y: 560, s: 1.25 },
          { at: m.cue('colours') - 12, dur: 26, x: 740, y: 300, s: 1.7 },
        ]} />
      {blips(focus)}
    </>
  );
};

const REPEATS = [t('Doesn’t repeat'), t('Daily'), t('Every weekday (Monday to Friday)'), t('Weekly'), t('Monthly on day 30'), t('Yearly on October 30')];

/** The real «New event» window (event-dialog.png), field by field, with the «Repeats» list open. */
const NewEvent: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const n = B('new');
  const field = (y: number): R => [20, y, 640, 36];
  const focus: Focus[] = [
    { from: n.cue('new') - 2, to: n.cue('title') - 4, rect: [16, 10, 300, 30], label: t('New event'), at: 'right' },
    { from: n.cue('title') - 4, to: n.cue('calendar') - 4, rect: field(72) },
    { from: n.cue('calendar') - 4, to: n.cue('starts') - 4, rect: [20, 140, 640, 38] },
    { from: n.cue('starts') - 4, to: n.cue('repeats') - 6, rect: [20, 230, 618, 52] },
    { from: n.cue('reminder') - 4, to: n.cue('place') - 4, rect: field(381) },
    { from: n.cue('place') - 4, to: n.cue('link') - 4, rect: field(450) },
    { from: n.cue('link') - 4, to: n.to + 30, rect: field(517) },
  ];
  const list = inOut(f, n.cue('repeats') - 6, n.cue('reminder') - 6, 6);
  const hot = Math.min(4, Math.max(1, Math.floor((f - n.cue('repeats') + 4) / 14) + 1));
  return (
    <>
      <CaptureShot name="event-dialog" frame focus={focus}
        shots={[
          { at: n.from - 4, x: 340, y: 366, s: 1.2 },
          { at: n.from, dur: 30, x: 340, y: 366, s: 1.3 },
        ]}>
        <div style={{
          position: 'absolute', left: 20, top: 356, width: 640, padding: 6, borderRadius: 8, boxSizing: 'border-box', background: '#2c2c30',
          border: '1px solid rgba(255,255,255,0.12)', boxShadow: '0 16px 40px rgba(0,0,0,0.55)', opacity: list, transform: `translateY(${(1 - list) * -10}px)`,
        }}>
          {REPEATS.map((r, i) => (
            <div key={r} style={{
              height: 36, display: 'flex', alignItems: 'center', padding: '0 12px', borderRadius: 5, fontFamily: SANS, fontSize: 15, color: C.text,
              background: i === hot ? 'rgba(255,255,255,0.09)' : 'transparent', borderLeft: `3px solid ${i === hot ? C.accentText : 'transparent'}`,
            }}>{r}</div>
          ))}
        </div>
      </CaptureShot>
      {blips(focus)}
      <Sfx at={n.cue('repeats') - 6} name="pop-low" volume={0.18} />
    </>
  );
};

/** Settings › Calendar › «How it reminds you», then the three ways side by side. */
const Remind: React.FC = () => {
  const B = useBeats();
  const r = B('remind');
  return (
    <>
      <CaptureShot name="settings-calendars" focus={[{ from: r.cue('reminds') - 6, to: r.cue('osd'), rect: [580, 495, 942, 40] }]}
        shots={[{ at: r.from - 4, x: 960, y: 540, s: 1.0 }, { at: r.from, dur: 30, x: 1050, y: 500, s: 1.45 }]} />
      <Sfx at={r.cue('reminds') - 6} name="blip" volume={0.12} />
    </>
  );
};

const Ways: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const r = B('remind');
  const rows = [
    { cue: 'osd', label: t('On-screen message (OSD)'), y: 230 },
    { cue: 'notification', label: t('Notification (with “Join”)'), y: 470 },
    { cue: 'assistant', label: t('An assistant says it'), y: 790 },
  ];
  const k = (cue: string) => pop(f, r.cue(cue) - 8, { damping: 14 });
  return (
    <AbsoluteFill>
      {rows.map(row => (
        <div key={row.cue} style={{
          position: 'absolute', left: 170, top: row.y, transform: `translateY(-50%) translateX(${(1 - glide(f, r.cue(row.cue) - 10, 18)) * -40}px)`,
          opacity: glide(f, r.cue(row.cue) - 10, 18), fontFamily: SANS, fontSize: 34, fontWeight: 700, color: C.accentText, width: 560,
        }}>{row.label}</div>
      ))}
      <div style={{ position: 'absolute', left: 820, top: 230, transform: 'translateY(-50%)' }}>
        <Osd icon="calendar_clock_24_filled" color="blue" text={REMINDER.text} show={k('osd')} scale={1.7} />
      </div>
      <Toast {...REMINDER} icon="calendar_clock_24_filled" color="blue" show={k('notification')} scale={1.45} style={{ left: 820, top: 380 }} />
      <SaysBubble text={t('Reminder: Team stand-up · in 10 min (9:30 AM)')} show={k('assistant')} style={{ left: 820, top: 740 }} />
      {rows.map(row => <Sfx key={row.cue} at={r.cue(row.cue) - 8} name="pop" volume={0.2} />)}
    </AbsoluteFill>
  );
};

const DAYS: Array<[string, Array<[string, string, string]>]> = [
  [t('Today'), [['8:00 AM', t('Buy a birthday present'), '#5AA9FF'], ['9:00 PM', t('Dentist'), '#5AA9FF']]],
  [t('Tomorrow'), [['9:30 AM', t('Team stand-up'), '#F2B807'], ['12:00 PM', t('Client call: Atlas project'), '#F2B807'], ['7:00 PM', t('Gym'), '#5AA9FF']]],
];

/** The Agenda on the phone: a drawn phone with the same appointments as the real page. */
const OnPhone: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const p = B('phone');
  const k = pop(f, p.cue('phone') - 10, { damping: 15 });
  let row = 0;
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 540, transform: `translate(-50%, -50%) scale(${0.8 + 0.2 * k})`, opacity: Math.min(1, k * 1.4) }}>
        <Phone width={420}>
          <div style={{ position: 'absolute', inset: 0, background: '#121216', padding: '70px 22px 0', fontFamily: SANS, boxSizing: 'border-box' }}>
            <div style={{ fontSize: 34, fontWeight: 800, color: C.text, marginBottom: 18 }}>{t('Agenda')}</div>
            {DAYS.map(([day, items]) => (
              <div key={day} style={{ marginBottom: 16 }}>
                <div style={{ fontSize: 18, fontWeight: 700, color: C.accentText, marginBottom: 8 }}>{day}</div>
                {items.map(([time, title, color]) => {
                  const c = pop(f, p.cue('phone') + 2 * row++, { damping: 15 });
                  return (
                    <div key={title} style={{ display: 'flex', gap: 12, alignItems: 'center', padding: '12px 14px', marginBottom: 8, borderRadius: 12, background: 'rgba(255,255,255,0.06)', opacity: Math.min(1, c * 1.4) }}>
                      <div style={{ width: 4, alignSelf: 'stretch', borderRadius: 2, background: color }} />
                      <div>
                        <div style={{ fontSize: 19, fontWeight: 600, color: C.text }}>{title}</div>
                        <div style={{ fontSize: 15, color: C.dim }}>{time}</div>
                      </div>
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
        </Phone>
      </div>
      <Sfx at={p.cue('phone') - 10} name="whoosh-short" volume={0.22} />
    </AbsoluteFill>
  );
};

/** Chapter «The Agenda». */
export const Agenda: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const month = B('month');
  const n = B('new');
  const r = B('remind');
  const ways = r.cue('osd') - 14;
  return (
    <AbsoluteFill>
      <Part from={0} to={month.from - 4} first><List /></Part>
      <Part from={month.from - 4} to={n.from - 4}><Month /></Part>
      <Part from={n.from - 4} to={r.from - 4}><NewEvent /></Part>
      <Part from={r.from - 4} to={ways}><Remind /></Part>
      <Part from={ways} to={B('phone').from - 4}><Ways /></Part>
      <Part from={B('phone').from - 4} to={durationInFrames} last><OnPhone /></Part>
    </AbsoluteFill>
  );
};
