import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { Toggle } from '../../../src/components/Ui';
import { ease, glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** «Who can write to it» on the real page: someone new waiting, and «Allow». */
const Waiting: React.FC = () => {
  const B = useBeats();
  const n = B('new');
  const focus: Focus[] = [
    { from: n.cue('someone') - 4, to: n.cue('allow') - 4, rect: [578, 835, 944, 60] },
    { from: n.cue('allow') - 4, to: n.to + 30, rect: [1346, 861, 80, 30], label: t('Allow'), at: 'left' },
  ];
  return (
    <>
      <CaptureShot name="telegram-bot" focus={focus}
        shots={[{ at: 0, x: 1050, y: 760, s: 1.3 }, { at: 4, dur: 30, x: 1050, y: 760, s: 1.5 }]} />
      {focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />)}
    </>
  );
};

const Panel: React.FC<{ show: number; children: React.ReactNode; title: string; icon: string; color: string }> = ({ show, children, title, icon, color }) => (
  <div style={{
    position: 'absolute', left: 960, top: 520, width: 1100, padding: '30px 36px', borderRadius: 28, boxSizing: 'border-box', fontFamily: SANS,
    transform: `translate(-50%, -50%) translateY(${(1 - show) * 60}px)`, opacity: show,
    background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
  }}>
    <div style={{ display: 'flex', alignItems: 'center', gap: 18, marginBottom: 22 }}>
      <KdIcon icon={icon} color={color} size={56} />
      <span style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{title}</span>
    </div>
    {children}
  </div>
);

const LEVELS = [
  { cue: '', name: t('Ignore'), hint: t('Their messages are dropped.') },
  { cue: 'chat', name: t('Chat only'), hint: t('Talks with the assistant, without tools.') },
  { cue: 'commands', name: t('Chosen commands'), hint: t('Runs the commands you choose, without AI.') },
  { cue: 'read', name: t('Tools that only read'), hint: t('Chat and tools that change nothing (weather, time, search…).') },
  { cue: 'chosen', name: t('Chosen tools'), hint: t('Chat and the tools and commands you choose.') },
  { cue: 'like', name: t('Like you'), hint: t('The same as you on the PC. Anything needing confirmation is asked to you.') },
];

/** The levels a contact can have, lit as the narrator names them. */
const Levels: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const l = B('levels');
  const hot = LEVELS.reduce((s, x, i) => (x.cue && f >= l.cue(x.cue) - 4 ? i : s), -1);
  const asked = pop(f, l.cue('asked') - 8, { damping: 14 });
  return (
    <AbsoluteFill>
      <Panel show={glide(f, l.from - 6, 22)} title={t('What they can do')} icon="person_key_20_filled" color="purple">
        {LEVELS.map((x, i) => (
          <div key={x.name} style={{
            display: 'flex', alignItems: 'center', gap: 20, height: 62, padding: '0 20px', borderRadius: 12, marginBottom: 6,
            background: i === hot ? C.accentSoft : 'rgba(255,255,255,0.04)', borderLeft: `4px solid ${i === hot ? C.accentText : 'transparent'}`,
            opacity: i === 0 ? 0.5 : 1,
          }}>
            <span style={{ width: 280, fontSize: 25, fontWeight: 700, color: i === hot ? C.text : C.dim }}>{x.name}</span>
            <span style={{ fontSize: 20, color: C.dim }}>{x.hint}</span>
          </div>
        ))}
        <div style={{
          display: 'inline-flex', alignItems: 'center', gap: 12, marginTop: 14, padding: '12px 20px', borderRadius: 14, background: C.accentSoft, color: C.accentText,
          fontSize: 22, fontWeight: 600, opacity: Math.min(1, asked * 1.4), transform: `scale(${0.85 + 0.15 * asked})`,
        }}>
          <Fluent name="shield_lock_24_filled" size={26} color={C.accentText} /> {t('Confirmations always come to you.')}
        </div>
      </Panel>
      {LEVELS.filter(x => x.cue).map(x => <Sfx key={x.cue} at={l.cue(x.cue) - 4} name="blip" volume={0.14} />)}
      <Sfx at={l.cue('asked') - 8} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};

/** The limits and the groups, drawn with their real labels and defaults. */
const Limits: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const l = B('limits');
  const rows = [
    { cue: 'limits', icon: 'timer_24_regular', label: t('Messages a minute from each'), right: <Chip>{t('6 a minute')}</Chip> },
    { cue: 'gaming', icon: 'games_24_regular', label: t('Only me while I’m gaming'), right: <Toggle on={ease(f, l.cue('gaming') - 2, l.cue('gaming') + 8)} scale={1.3} /> },
    { cue: 'groups', icon: 'people_24_regular', label: t('Groups'), right: <Chip>{t('Answers when mentioned')}</Chip> },
  ];
  return (
    <AbsoluteFill>
      <Panel show={glide(f, l.from - 6, 22)} title={t('Limits')} icon="shield_lock_24_filled" color="teal">
        {rows.map(r => {
          const k = pop(f, l.cue(r.cue) - 6, { damping: 14 });
          return (
            <div key={r.cue} style={{
              display: 'flex', alignItems: 'center', gap: 18, height: 76, padding: '0 22px', borderRadius: 14, marginBottom: 10, background: 'rgba(255,255,255,0.05)',
              opacity: Math.min(1, k * 1.4), transform: `translateX(${(1 - k) * -40}px)`,
            }}>
              <Fluent name={r.icon} size={30} color={C.text} />
              <span style={{ fontSize: 26, color: C.text }}>{r.label}</span>
              <div style={{ marginLeft: 'auto' }}>{r.right}</div>
            </div>
          );
        })}
      </Panel>
      {rows.map(r => <Sfx key={r.cue} at={l.cue(r.cue) - 6} name="pop-high" volume={0.16} />)}
    </AbsoluteFill>
  );
};

const Chip: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <span style={{ padding: '10px 18px', borderRadius: 10, background: 'rgba(255,255,255,0.08)', fontFamily: SANS, fontSize: 22, fontWeight: 600, color: C.text }}>{children}</span>
);

/** Chapter «Who can write to it». */
export const People: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const l = B('levels');
  const lim = B('limits');
  return (
    <AbsoluteFill>
      <Part from={0} to={l.from - 4} first><Waiting /></Part>
      <Part from={l.from - 4} to={lim.from - 4}><Levels /></Part>
      <Part from={lim.from - 4} to={durationInFrames} last><Limits /></Part>
    </AbsoluteFill>
  );
};
