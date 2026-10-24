import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Cursor } from '../components/Cursor';
import { AppWindow, Wallpaper } from '../components/Desktop';
import { Kinetic } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Keycap, Mono } from '../components/Keys';
import { Sfx } from '../components/Sfx';
import { Toggle } from '../components/Ui';
import { ease, glide, inOut, pop } from '../lib/anim';
import { BeatTimes, useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';
import { t as tr } from '../lib/i18n';

type Step = { text: string; delay: number; icon: string; kind?: 'down' | 'up' | 'move' };

/** K-Desktop's recorder bar (RecorderBar.xaml): the red dot, «Recording», time · steps · how to stop. */
const RecorderBar: React.FC<{ steps: number; seconds: number; done?: string; show: number }> = ({ steps, seconds, done, show }) => {
  const f = useCurrentFrame();
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 18, padding: '14px 26px 14px 18px', borderRadius: 40, background: 'rgba(32,32,34,0.94)', border: '1px solid rgba(255,255,255,0.15)',
      boxShadow: '0 16px 40px rgba(0,0,0,0.5)', opacity: show, transform: `translateY(${(1 - show) * -40}px)`,
    }}>
      <div style={{ position: 'relative', width: 34, height: 34 }}>
        {!done && <div style={{ position: 'absolute', inset: 0, borderRadius: 17, background: 'rgba(229,72,77,0.35)', transform: `scale(${0.8 + 0.3 * Math.abs(Math.sin(f / 10))})` }} />}
        <div style={{ position: 'absolute', left: 9, top: 9, width: 16, height: 16, borderRadius: 8, background: done ? C.success : '#E5484D' }} />
      </div>
      <span style={{ fontFamily: SANS, fontSize: 24, fontWeight: 700, color: 'white' }}>{done ?? tr('Recording')}</span>
      {!done && <span style={{ fontFamily: SANS, fontSize: 20, color: 'rgba(255,255,255,0.7)' }}>{`00:${String(Math.floor(seconds)).padStart(2, '0')}  ·  ${steps} ${tr('steps')}  ·  ${tr('Shift + Esc finishes')}`}</span>}
    </div>
  );
};

const Again: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const loop = 40;
  const t = (f - b.from) % loop;
  const round = Math.max(0, Math.floor((f - b.from) / loop));
  const path = [
    { at: 0, x: 700, y: 420 },
    ...Array.from({ length: 12 }, (_, k) => [
      { at: b.from + k * loop, x: 700, y: 420, dur: 10 },
      { at: b.from + k * loop + 12, x: 980, y: 560, dur: 10 },
      { at: b.from + k * loop + 24, x: 1240, y: 420, dur: 10 },
    ]).flat(),
  ];
  const clicks = Array.from({ length: 12 }, (_, k) => [10, 22, 34].map(d => ({ at: b.from + k * loop + d }))).flat();
  const rec = pop(f, b.cue('macros') - 4, { damping: 12 });
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', inset: 0, opacity: 1 - 0.6 * rec }}>
        <Wallpaper hue={-60} />
        <AppWindow kind="explorer" width={1200} height={700} style={{ left: 360, top: 160 }} />
        <Cursor path={path} clicks={clicks} />
        <div style={{ position: 'absolute', left: 1480, top: 110, fontFamily: SANS, fontSize: 80, fontWeight: 800, color: C.text, opacity: t >= 0 ? 1 : 0 }}>×{round + 1}</div>
      </div>
      <div style={{ position: 'absolute', left: 960, top: 520, transform: `translate(-50%, -50%) scale(${rec})`, opacity: rec }}>
        <KdIcon icon="record_24_filled" color="red" size={220} glow={0.6} />
      </div>
      <Kinetic text={tr('Macros')} at={b.cue('macros')} x={960} y={760} size={110} />
      {Array.from({ length: 6 }, (_, k) => <Sfx key={k} at={b.from + k * loop + 10} name="click" volume={0.12} />)}
      <Sfx at={b.cue('macros') - 4} name="impact" volume={0.3} />
    </AbsoluteFill>
  );
};

const STEPS: Step[] = [
  { text: tr('Press and release Ctrl + C'), delay: 0, icon: 'keyboard_24_filled' },
  { text: tr('Left click at 820, 440'), delay: 640, icon: 'cursor_click_24_filled' },
  { text: tr('Wheel down ×3'), delay: 420, icon: 'arrow_sort_down_lines_24_filled' },
  { text: tr('Move to 1210, 560'), delay: 180, icon: 'arrow_move_24_filled', kind: 'move' },
  { text: tr('Move to 1260, 590'), delay: 40, icon: 'arrow_move_24_filled', kind: 'move' },
  { text: tr('Press Ctrl'), delay: 2300, icon: 'keyboard_24_filled', kind: 'down' },
  { text: tr('Press V'), delay: 90, icon: 'keyboard_24_filled', kind: 'down' },
  { text: tr('Release V'), delay: 70, icon: 'keyboard_24_filled', kind: 'up' },
  { text: tr('Release Ctrl'), delay: 60, icon: 'keyboard_24_filled', kind: 'up' },
  { text: tr('Type “Done ✓”'), delay: 850, icon: 'text_t_24_filled' },
];

/** Recording, then the steps: edit, simplify. */
const Recording: React.FC<{ record: BeatTimes; edit: BeatTimes }> = ({ record, edit }) => {
  const f = useCurrentFrame();
  const stepAt = [record.cue('key'), record.cue('click'), record.cue('scroll'), record.cue('move'), record.cue('move') + 6, record.cue('timing') - 6, record.cue('timing') - 3, record.cue('timing'), record.cue('timing') + 3, record.cue('stop') - 8];
  const stopAt = record.cue('stop');
  const recorded = f > stopAt + 12;
  const count = stepAt.filter(t => f >= t).length;
  // Editing and simplifying.
  const merged = f > edit.cue('merge') + 4;
  const noMoves = f > edit.cue('drop') + 4;
  const halved = f > edit.cue('halve') + 4;
  const capped = f > edit.cue('cap') + 4;
  let steps = STEPS.map((s, i) => ({ ...s, i }));
  if (merged) steps = steps.filter(s => s.kind !== 'up').map(s => (s.kind === 'down' ? { ...s, text: s.text.replace(tr('Press'), tr('Press and release')), kind: undefined } : s));
  if (noMoves) steps = steps.filter(s => s.kind !== 'move');
  const delayOf = (d: number) => Math.round((capped ? Math.min(d / (halved ? 2 : 1), 1000) : d / (halved ? 2 : 1)));
  const editRow = edit.cue('edited');
  const simplifyOpen = inOut(f, edit.cue('simplify') - 4, edit.cue('cap') + 30, 8);
  const toList = glide(f, edit.from - 10, 26);
  return (
    <AbsoluteFill>
      {/* The task being done */}
      <div style={{ position: 'absolute', left: 80, top: 190, width: 900, height: 560, borderRadius: 20, overflow: 'hidden', border: `2px solid ${C.line2}`, opacity: 1 - toList, transform: `translateX(${-toList * 300}px)` }}>
        <div style={{ position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, transform: 'scale(0.469)', transformOrigin: '0 0' }}>
          <Wallpaper hue={-60} />
          <AppWindow kind="explorer" width={1500} height={860} style={{ left: 200, top: 100 }} />
          <Cursor path={[{ at: 0, x: 900, y: 700 }, { at: stepAt[1] - 14, x: 820, y: 440, dur: 12 }, { at: stepAt[3] - 6, x: 1210, y: 560, dur: 8 }, { at: stepAt[4], x: 1260, y: 590, dur: 6 }]} clicks={[{ at: stepAt[1] }]} scale={1.8} />
        </div>
      </div>
      <div style={{ position: 'absolute', left: 960, top: 100, transform: 'translateX(-50%)' }}>
        <RecorderBar steps={count} seconds={(f - record.from) / 30} done={recorded ? `${tr('Macro recorded')}: ${STEPS.length} ${tr('steps')} · 6.2 s` : undefined} show={inOut(f, record.cue('record') - 6, edit.from + 10, 8)} />
      </div>
      {/* Shift + Esc */}
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 120, display: 'flex', justifyContent: 'center', gap: 18, opacity: inOut(f, stopAt - 10, stopAt + 30, 6) }}>
        <Keycap label={tr('Shift')} height={96} width={180} size={32} down={inOut(f, stopAt - 2, stopAt + 12, 3)} />
        <Keycap label={tr('Esc')} height={96} width={120} size={32} down={inOut(f, stopAt, stopAt + 12, 3)} />
      </div>
      {/* The steps */}
      <div style={{
        position: 'absolute', left: 1020 - toList * 480, top: 200, width: 840, padding: 24, borderRadius: 24, background: 'rgba(30,30,36,0.97)', border: `1px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 14 }}>
          <KdIcon icon="record_24_filled" color="red" size={44} />
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>{tr('Copy report')}</span>
          <span style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 20, color: C.accentText, fontWeight: 700 }}>{f > edit.from ? `${steps.length} ${tr('steps')}` : ''}</span>
        </div>
        {steps.map((s, k) => {
          const visible = f >= stepAt[s.i] || f > edit.from;
          if (!visible) return null;
          const editing = s.i === 1 && inOut(f, editRow - 4, editRow + 26, 4) > 0.3;
          const dragged = s.i === 2 ? inOut(f, edit.cue('dragged') - 2, edit.cue('dragged') + 22, 6) : 0;
          const inserted = s.i === 9 && f > edit.cue('inserted') - 4 && f < edit.cue('deleted') ? 1 : 0;
          const deleted = s.i === 9 ? ease(f, edit.cue('deleted'), edit.cue('deleted') + 10) : 0;
          return (
            <div key={s.i} style={{
              display: 'flex', alignItems: 'center', gap: 14, height: 50 * (1 - deleted), overflow: 'hidden', padding: '0 14px', borderRadius: 10, marginBottom: 4,
              background: editing || inserted ? 'rgba(242,193,74,0.14)' : deleted > 0 ? 'rgba(255,107,107,0.2)' : 'rgba(255,255,255,0.04)',
              transform: `translateX(${dragged * 30}px) scale(${1 + dragged * 0.03})`, boxShadow: dragged > 0.1 ? '0 10px 30px rgba(0,0,0,0.5)' : 'none',
              opacity: 1 - deleted,
            }}>
              <Fluent name="re_order_dots_vertical_24_regular" size={18} color={C.faint} />
              <span style={{ width: 70, fontFamily: SANS, fontSize: 16, color: C.dim, textAlign: 'right' }}>{k === 0 ? '' : `${delayOf(s.delay)} ms`}</span>
              <Fluent name={s.icon} size={22} color={C.accentText} />
              <Mono size={19}>{editing ? tr('Left click at 860, 452') : s.text}</Mono>
            </div>
          );
        })}
      </div>
      {/* Simplify */}
      <div style={{ position: 'absolute', left: 1410, top: 230, width: 430, padding: 18, borderRadius: 20, background: 'rgba(40,40,48,0.99)', border: `1.5px solid rgba(242,193,74,0.6)`, boxShadow: '0 20px 60px rgba(0,0,0,0.6)', opacity: simplifyOpen, transform: `translateY(${(1 - simplifyOpen) * -20}px)` }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
          <Fluent name="sparkle_24_filled" size={24} color={C.accentText} />
          <span style={{ fontFamily: SANS, fontSize: 22, fontWeight: 800, color: C.text }}>{tr('Simplify')}</span>
        </div>
        {[
          ['merge', tr('Merge presses and clicks')], ['drop', tr('Remove mouse movements')], ['halve', tr('Halve the pauses')], ['cap', tr('Limit pauses to 1 second')],
        ].map(([c, t]) => {
          const on = f >= edit.cue(c) - 2;
          return (
            <div key={c} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 12px', borderRadius: 10, background: on && f < edit.cue(c) + 20 ? C.accentSoft : 'transparent' }}>
              <Fluent name={on ? 'checkmark_circle_24_filled' : 'circle_24_regular'} size={22} color={on ? C.success : C.faint} />
              <span style={{ fontFamily: SANS, fontSize: 19, color: C.text }}>{t}</span>
            </div>
          );
        })}
      </div>
      {stepAt.map((t, i) => <Sfx key={i} at={t} name={i === 1 ? 'click' : 'key'} volume={0.18} />)}
      <Sfx at={stopAt} name="key" volume={0.45} />
      <Sfx at={stopAt + 12} name="chime" volume={0.22} />
      {['merge', 'drop', 'halve', 'cap'].map(c => <Sfx key={c} at={edit.cue(c) + 4} name="whoosh-short" volume={0.16} />)}
    </AbsoluteFill>
  );
};

/** Playback options, and game mode. */
const Playback: React.FC<{ play: BeatTimes; game: BeatTimes }> = ({ play, game }) => {
  const f = useCurrentFrame();
  const opts = [
    { cue: 'once', label: tr('Once') }, { cue: 'several', label: '×3' }, { cue: 'loop', label: tr('Loop') }, { cue: 'speed', label: tr('Speed 2×') },
  ];
  const mode = opts.reduce((m, o, i) => (f >= play.cue(o.cue) - 4 ? i : m), 0);
  const speed = mode === 3 ? 2 : 1;
  const t = ((f - play.from) * speed) / 40;
  const head = mode === 1 ? Math.min(3, t) % 1 : t % 1;
  const passes = mode === 1 ? Math.min(3, Math.floor(t)) : Math.floor(t);
  const gameOn = f > game.cue('game') + 4;
  const toGame = glide(f, game.from - 10, 26);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 440, transform: `translate(-50%, -50%) translateY(${-toGame * 200}px) scale(${1 - 0.2 * toGame})`, width: 1300, padding: 34, borderRadius: 28, background: 'rgba(30,30,36,0.97)', border: `1px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 26 }}>
          <KdIcon icon="record_24_filled" color="red" size={56} />
          <div>
            <div style={{ fontFamily: SANS, fontSize: 16, fontWeight: 700, letterSpacing: 2, color: C.accentText }}>{tr('ACTION · PLAY MACRO')}</div>
            <div style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text }}>{tr('Copy report')}</div>
          </div>
          <div style={{ marginLeft: 'auto', display: 'flex', gap: 10 }}>
            {opts.map((o, i) => (
              <span key={o.cue} style={{ fontFamily: SANS, fontSize: 22, fontWeight: 700, padding: '10px 18px', borderRadius: 14, background: i === mode ? C.accentText : 'rgba(255,255,255,0.06)', color: i === mode ? '#1a1306' : C.dim, display: 'flex', alignItems: 'center', gap: 8 }}>
                {i === 2 && <Fluent name="arrow_repeat_all_24_filled" size={22} color={i === mode ? '#1a1306' : C.dim} />}{o.label}
              </span>
            ))}
          </div>
        </div>
        <div style={{ position: 'relative', height: 70, borderRadius: 14, background: 'rgba(255,255,255,0.04)', overflow: 'hidden' }}>
          {STEPS.map((s, i) => <div key={i} style={{ position: 'absolute', left: `${4 + i * 9.5}%`, top: 16, width: 30, height: 38, borderRadius: 8, background: head * 100 > 4 + i * 9.5 ? C.accentText : 'rgba(255,255,255,0.14)' }} />)}
          <div style={{ position: 'absolute', left: `${head * 100}%`, top: 0, bottom: 0, width: 4, background: 'white', boxShadow: '0 0 14px white' }} />
        </div>
        <div style={{ marginTop: 16, fontFamily: SANS, fontSize: 22, color: C.dim }}>
          {mode === 0 ? tr('Plays it once.') : mode === 1 ? `Pass ${Math.min(3, passes + 1)} of 3` : mode === 2 ? tr('Until you press Shift + Esc') : tr('Pauses halved: twice as fast')}
        </div>
      </div>
      {/* Game mode */}
      <div style={{ position: 'absolute', left: 960, top: 760, transform: `translate(-50%, -50%) scale(${0.9 + 0.1 * toGame})`, opacity: toGame, display: 'flex', gap: 40, alignItems: 'center' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '20px 28px', borderRadius: 22, background: 'rgba(34,34,40,0.97)', border: `1.5px solid ${gameOn ? 'rgba(242,193,74,0.7)' : C.line2}` }}>
          <KdIcon icon="xbox_controller_24_filled" color="indigo" size={56} />
          <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>{tr('Game mode')}</span>
          <Toggle on={gameOn ? 1 : 0} scale={1.5} />
        </div>
        <div style={{ position: 'relative', width: 640, height: 300, borderRadius: 22, overflow: 'hidden', background: 'radial-gradient(ellipse at 50% 60%, #2a3b2a, #0a0f0a)', border: `1.5px solid ${C.line2}` }}>
          <div style={{ position: 'absolute', left: -100 - ((f * 6) % 200), top: 170, width: 1000, height: 3, background: 'rgba(255,255,255,0.12)' }} />
          {Array.from({ length: 6 }, (_, i) => <div key={i} style={{ position: 'absolute', left: ((i * 160 - f * 5) % 900 + 900) % 900 - 100, top: 110, width: 40, height: 60, borderRadius: 4, background: 'rgba(255,255,255,0.1)' }} />)}
          <div style={{ position: 'absolute', left: 320, top: 150, transform: 'translate(-50%, -50%)' }}>
            <svg width="60" height="60" viewBox="0 0 60 60"><g stroke={C.gold1} strokeWidth="3"><path d="M30 6v16M30 38v16M6 30h16M38 30h16" /></g></svg>
          </div>
          <div style={{ position: 'absolute', right: 18, top: 16, display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6, opacity: ease(f, game.cue('raw'), game.cue('raw') + 10) }}>
            <Mono size={20} color={C.accentText}>{`Δx ${Math.round(Math.sin(f / 7) * 14) >= 0 ? '+' : ''}${Math.round(Math.sin(f / 7) * 14)}  Δy ${Math.round(Math.cos(f / 9) * 5)}`}</Mono>
            <Mono size={16} color={C.dim}>{tr('raw · relative')}</Mono>
          </div>
          <div style={{ position: 'absolute', left: 18, bottom: 14, display: 'flex', alignItems: 'center', gap: 8, opacity: ease(f, game.cue('lock'), game.cue('lock') + 10) }}>
            <Fluent name="lock_closed_24_filled" size={20} color={C.dim} /><Mono size={16} color={C.dim}>{tr('pointer locked')}</Mono>
          </div>
        </div>
      </div>
      {opts.map(o => <Sfx key={o.cue} at={play.cue(o.cue) - 4} name="blip" volume={0.18} />)}
      <Sfx at={game.cue('game') + 4} name="pop" volume={0.25} />
    </AbsoluteFill>
  );
};

export const Macros: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const intro = B('intro');
  const record = B('record');
  const play = B('play');
  return (
    <AbsoluteFill>
      <Part from={0} to={record.from - 4} first><Again b={intro} /></Part>
      <Part from={record.from - 4} to={play.from - 6}><Recording record={record} edit={B('edit')} /></Part>
      <Part from={play.from - 6} to={durationInFrames} last><Playback play={play} game={B('game')} /></Part>
    </AbsoluteFill>
  );
};

