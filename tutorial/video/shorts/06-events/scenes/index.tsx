import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../../src/components/Capture';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { ShortAgenda, ShortEnd, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, inOut, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { Events } from '../../../src/scenes/Events';
import type { Short } from '../../../src/shorts';
import { C, MOODS, SANS } from '../../../src/theme';

/** Something happens on the PC (a program opens) and a command fires on its own; the title and the plan. */
const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const at = idea.cue('something');
  const happen = pop(f, at - 10, { damping: 13 });
  const fire = pop(f, at + 10, { damping: 12, stiffness: 160 });
  const away = ease(f, idea.to - 8, idea.to + 12, IN);
  const you = pop(f, idea.from + 4, { damping: 14 }) * (1 - ease(f, at - 16, at - 4));
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 470, transform: `translate(-50%, -50%) scale(${you})`, opacity: you, display: 'flex', alignItems: 'center', gap: 24 }}>
        <Fluent name="cursor_click_24_filled" size={120} color={C.dim} />
        <Fluent name="arrow_right_24_filled" size={70} color={C.faint} />
        <KdIcon icon="flash_24_filled" color="gold" size={130} />
      </div>
      <div style={{ position: 'absolute', left: 960, top: 470, transform: 'translate(-50%, -50%)', opacity: 1 - away, display: 'flex', alignItems: 'center', gap: 34 }}>
        <div style={{ transform: `scale(${happen})`, display: 'flex', alignItems: 'center', gap: 18, padding: '20px 30px', borderRadius: 24, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${C.line2}` }}>
          <KdIcon icon="xbox_controller_24_filled" color="indigo" size={80} />
          <span style={{ fontFamily: SANS, fontSize: 40, fontWeight: 700, color: C.text }}>{t('Steam opens')}</span>
        </div>
        <Fluent name="arrow_right_24_filled" size={70} color={C.accentText} style={{ opacity: happen }} />
        <div style={{ transform: `scale(${fire})` }}><KdIcon icon="flash_24_filled" color="gold" size={130} glow={inOut(f, at + 10, at + 50, 6)} /></div>
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('How events work'), icon: 'arrow_forward_down_lightning_24_filled', color: 'orange', at: plan.cue('how') },
          { label: t('Real events'), icon: 'calendar_ltr_24_filled', color: 'blue', at: plan.cue('real') },
          { label: t('Conditions'), icon: 'filter_24_filled', color: 'purple', at: plan.cue('cond') },
        ]} />
      )}
      <Sfx at={at - 10} name="pop" volume={0.22} />
      <Sfx at={at + 10} name="chime" volume={0.22} />
    </AbsoluteFill>
  );
};

// The real Events page (tutorial/captures/en/events.png): the list rows and the parts of the editor.
const ROW = (i: number): [number, number, number, number] => [262, 214 + 75 * i, 282, 68];

/** Chapter «Real events»: the list of the real page, then the parts of one event. */
const Real: React.FC = () => {
  const B = useBeats();
  const list = B('list');
  const d = B('detail');
  const names = ['elden', 'weekday', 'away', 'headphones', 'file', 'cpu'];
  return (
    <CaptureShot name="events"
      shots={[
        { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
        { at: 6, dur: 40, x: 700, y: 440, s: 1.45, rx: 0 },
        { at: d.from - 10, dur: 30, x: 1100, y: 400, s: 1.3 },
        { at: d.cue('runs') - 20, dur: 26, x: 1200, y: 760, s: 1.3 },
        { at: d.cue('test') - 20, dur: 26, x: 1400, y: 300, s: 1.4 },
      ]}
      focus={[
        ...names.map((n, i) => ({ from: list.cue(n) - 4, to: (names[i + 1] ? list.cue(names[i + 1]) : d.from) - 4, rect: ROW(i) })),
        { from: d.cue('when') - 4, to: d.cue('exe') - 4, rect: [576, 258, 322, 38] as [number, number, number, number] },
        { from: d.cue('exe') - 4, to: d.cue('runs') - 4, rect: [578, 355, 112, 26] as [number, number, number, number] },
        { from: d.cue('runs') - 4, to: d.cue('ends') - 4, rect: [578, 666, 1302, 56] as [number, number, number, number] },
        { from: d.cue('ends') - 4, to: d.cue('test') - 6, rect: [562, 934, 1334, 60] as [number, number, number, number] },
        { from: d.cue('test') - 6, to: d.to + 30, rect: [1676, 96, 72, 34] as [number, number, number, number], label: t('Test'), at: 'bottom' },
      ]} />
  );
};

/** Chapter «One more thing»: «Only if» with two conditions, all of them, and the test. */
const Tip: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const tip = B('tip');
  const card = pop(f, tip.cue('only') - 8, { damping: 14 });
  const rows = [
    { cue: 'weekday', icon: 'calendar_ltr_24_filled', color: 'orange', name: t('Day and time'), detail: t('Monday to Friday') },
    { cue: 'home', icon: 'wifi_1_24_filled', color: 'teal', name: t('Network'), detail: t('Connected to «Home»') },
  ];
  const all = f > tip.cue('all') - 4;
  const met = pop(f, tip.cue('check') + 10, { damping: 12 });
  return (
    <AbsoluteFill>
      <div style={{
        position: 'absolute', left: 960, top: 480, transform: `translate(-50%, -50%) scale(${0.85 + 0.15 * card})`, opacity: Math.min(1, card * 1.4),
        width: 980, padding: 34, borderRadius: 28, background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`,
        boxShadow: '0 30px 80px rgba(0,0,0,0.55)', fontFamily: SANS,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 24 }}>
          <Fluent name="filter_24_regular" size={34} color={C.text} />
          <span style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{t('Only if')}</span>
          <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 12, fontSize: 20, color: C.dim }}>
            {t('Must be met')}
            {[t('All of them'), t('Any of them')].map((o, i) => (
              <span key={o} style={{ padding: '8px 16px', borderRadius: 18, fontWeight: 600, border: `1.5px solid ${(i === 0) === all || (!all && i === 0) ? C.accentText : C.line2}`, color: i === 0 ? C.accentText : C.dim, background: i === 0 && all ? C.accentSoft : 'transparent' }}>{o}</span>
            ))}
          </div>
        </div>
        {rows.map(r => {
          const k = pop(f, tip.cue(r.cue) - 6, { damping: 14 });
          return (
            <div key={r.cue} style={{
              display: 'flex', alignItems: 'center', gap: 18, height: 74, padding: '0 20px', borderRadius: 14, marginBottom: 10, background: 'rgba(255,255,255,0.05)',
              opacity: Math.min(1, k * 1.4), transform: `translateX(${(1 - k) * -40}px)`,
            }}>
              <KdIcon icon={r.icon} color={r.color} size={46} />
              <span style={{ fontSize: 26, fontWeight: 600, color: C.text }}>{r.name}</span>
              <span style={{ marginLeft: 'auto', fontSize: 22, color: C.dim }}>{r.detail}</span>
              <Fluent name="checkmark_circle_24_filled" size={34} color={C.success} style={{ opacity: met }} />
            </div>
          );
        })}
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginTop: 18 }}>
          <div style={{
            display: 'flex', alignItems: 'center', gap: 10, padding: '12px 20px', borderRadius: 10, fontSize: 20, fontWeight: 600, color: C.text,
            background: `rgba(255,255,255,${0.06 + 0.12 * inOut(f, tip.cue('check') - 2, tip.cue('check') + 8, 3)})`, border: `1px solid ${C.line2}`,
          }}>
            <Fluent name="play_24_regular" size={20} color={C.text} /> {t('Test whether it is met now')}
          </div>
          <span style={{ fontSize: 24, fontWeight: 700, color: C.success, opacity: met, transform: `scale(${met})` }}>{t('It is met')}</span>
        </div>
      </div>
      <Sfx at={tip.cue('only') - 8} name="pop" volume={0.22} />
      {rows.map(r => <Sfx key={r.cue} at={tip.cue(r.cue) - 6} name="pop-high" volume={0.18} />)}
      <Sfx at={tip.cue('check') + 10} name="chime" volume={0.22} />
    </AbsoluteFill>
  );
};

/** Tutorial 06, «Events»: the long video's chapter with every trigger of today, real events and conditions. */
export const EVENTS: Short = {
  scenes: { hook: Hook, events: Events, real: Real, tip: Tip, end: ShortEnd },
  moods: { hook: MOODS.intro, events: MOODS.events, real: MOODS.settings, tip: MOODS.events, end: MOODS.outro },
};
