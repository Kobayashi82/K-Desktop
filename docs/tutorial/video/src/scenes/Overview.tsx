import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../components/Camera';
import { CommandCard, WORK_MODE } from '../components/CommandCard';
import { Kinetic, Node, Wire } from '../components/Diagram';
import { Fluent } from '../components/Icon';
import { Sfx } from '../components/Sfx';
import { ease, glide, inOut, OUT, pop } from '../lib/anim';
import { useBeats } from '../lib/timeline';
import { C, SANS } from '../theme';

const TRIGGERS = [
  { cue: 'hotkey', icon: 'keyboard_24_filled', color: 'gold', title: 'Hotkey', sub: 'Ctrl + Alt + W' },
  { cue: 'mouse', icon: 'cursor_click_24_filled', color: 'indigo', title: 'Mouse button', sub: 'Mouse 5' },
  { cue: 'quickmenu', icon: 'text_bullet_list_tree_24_filled', color: 'purple', title: 'QuickMenu', sub: 'At the pointer' },
  { cue: 'launcher', icon: 'search_24_filled', color: 'yellow', title: 'Launcher', sub: 'Type a few letters' },
  { cue: 'voice', icon: 'mic_24_filled', color: 'teal', title: 'Voice', sub: '“Work mode”' },
  { cue: 'phone', icon: 'phone_24_filled', color: 'sky', title: 'Phone', sub: 'Web app' },
  { cue: 'network', icon: 'plug_connected_24_filled', color: 'pink', title: 'Network', sub: 'UDP · command line' },
  { cue: 'event', icon: 'arrow_forward_down_lightning_24_filled', color: 'orange', title: 'Event', sub: 'When something happens' },
  { cue: 'assistant', icon: 'bot_24_filled', color: 'purple', title: 'Assistant', sub: '“Lander, work mode”' },
];

const HELPERS = [
  { cue: 'macros', icon: 'record_24_filled', color: 'red', title: 'Macros', sub: 'Replay what you do' },
  { cue: 'kboard', icon: 'keyboard_layout_float_24_filled', color: 'indigo', title: 'K-Board', sub: 'Remap keys & buttons' },
  { cue: 'history', icon: 'history_24_filled', color: 'gray', title: 'History', sub: 'Every run, remembered' },
];

export const Overview: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const command = B('command');
  const triggers = B('triggers');
  const helpers = B('helpers');
  const local = B('local');

  const cardIn = pop(f, idea.cue('command') - 6, { damping: 13, stiffness: 120 });
  const rowCues = ['open', 'wait', 'snap', 'volume'].map(c => command.cue(c));
  const shown = rowCues.map(at => glide(f, at - 4, 16));
  // «Step by step, in order»: the play head walks down the rows.
  const steps = command.cue('steps');
  const active = f >= steps && f < steps + 4 * 12 ? Math.floor((f - steps) / 12) : -1;
  const done = f >= steps ? Math.min(3, Math.floor((f - steps) / 12)) : -1;
  const toSide = glide(f, triggers.from - 6, 30);
  const cardX = 960 + (1010 - 960) * toSide;
  const cardY = 540;
  const cardScale = 1.15 - 0.3 * toSide;
  const fire = TRIGGERS.reduce((m, t) => Math.max(m, inOut(f, triggers.cue(t.cue) + 10, triggers.cue(t.cue) + 26, 6)), 0);

  const trigX = 270;
  const trigY = (i: number) => 120 + i * 105;
  const helpX = 1640;
  const helpY = (i: number) => 360 + i * 170;

  const shots = [
    { at: 0, x: 960, y: 560, s: 1.25, ry: -8 },
    { at: 4, dur: idea.dur + 30, x: 960, y: 540, s: 1.08, ry: 0 },
    { at: triggers.from - 8, dur: 40, x: 860, y: 530, s: 1.0, ry: 4 },
    { at: triggers.cue('event'), dur: 60, x: 900, y: 560, s: 0.98, ry: 0 },
    { at: helpers.from - 6, dur: 40, x: 1080, y: 540, s: 1.0, ry: -4 },
    { at: local.from - 6, dur: 46, x: 960, y: 690, s: 0.64, ry: 0, rx: 4 },
  ];
  const frameDraw = ease(f, local.from, local.from + 40, OUT);
  const badges = [
    { cue: 'portable', icon: 'window_apps_24_filled', text: 'KDesktop.exe · portable' },
    { cue: 'windows', icon: 'desktop_24_filled', text: 'Windows 10 · 11' },
    { cue: 'languages', icon: 'translate_24_filled', text: 'ES · EN · FR · DE · IT · PT' },
    { cue: 'local', icon: 'cloud_off_24_filled', text: 'Runs locally' },
    { cue: 'data', icon: 'folder_24_filled', text: '%APPDATA%\\K-Desktop 5' },
  ];

  return (
    <AbsoluteFill>
      <Camera shots={shots}>
        <Kinetic text="One simple idea:" at={idea.from + 2} x={960} y={175} size={60} out={idea.cue('command') + 30} color={C.dim} weight={700} />

        {/* Triggers and their wires */}
        {TRIGGERS.map((t, i) => {
          const at = triggers.cue(t.cue);
          const show = pop(f, at - 6, { damping: 14, stiffness: 160 });
          const lit = inOut(f, at - 4, at + 30, 6);
          return (
            <React.Fragment key={t.cue}>
              <Wire from={[trigX + 165, trigY(i)]} to={[cardX - 235 * cardScale, cardY]} draw={ease(f, at, at + 16, OUT)} flow={ease(f, at + 10, at + 20)} seed={i} />
              <Node x={trigX} y={trigY(i)} icon={t.icon} color={t.color} title={t.title} sub={t.sub} show={show} lit={lit} size={0.82} width={330} />
            </React.Fragment>
          );
        })}

        {/* Companions */}
        {HELPERS.map((h, i) => {
          const at = helpers.cue(h.cue);
          const show = pop(f, at - 6, { damping: 14, stiffness: 160 });
          return (
            <React.Fragment key={h.cue}>
              <Wire from={[cardX + 235 * cardScale, cardY]} to={[helpX - 160, helpY(i)]} draw={ease(f, at - 2, at + 14, OUT)} flow={0} color="rgba(255,255,255,0.6)" />
              <Node x={helpX} y={helpY(i)} icon={h.icon} color={h.color} title={h.title} sub={h.sub} show={show} lit={inOut(f, at - 4, at + 30, 6)} size={0.9} width={330} />
            </React.Fragment>
          );
        })}

        {/* The command */}
        <div style={{ position: 'absolute', left: cardX, top: cardY, transform: `translate(-50%, -50%) scale(${cardScale * (0.7 + 0.3 * cardIn)})`, opacity: Math.min(1, cardIn * 2) }}>
          <CommandCard name="Work mode" rows={WORK_MODE.slice(0, 4)} shown={shown} active={active} done={done} nameShow={ease(f, command.cue('named') - 4, command.cue('named') + 10)}
            glow={Math.max(fire, inOut(f, steps, steps + 50, 8) * 0.8)} />
        </div>

        {/* Everything inside your PC */}
        {frameDraw > 0 && (
          <svg style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }} width={1} height={1}>
            <rect x={-250} y={-60} width={2420} height={1210} rx={60} fill="none" stroke={C.accentText} strokeOpacity={0.5} strokeWidth={4}
              strokeDasharray={`${7300 * frameDraw} 7300`} />
          </svg>
        )}
        <div style={{ position: 'absolute', left: 960, top: -60, transform: 'translate(-50%, -50%)', display: 'flex', alignItems: 'center', gap: 18, padding: '10px 30px', borderRadius: 40, background: '#121216', border: `2px solid rgba(242,193,74,0.5)`, opacity: frameDraw }}>
          <Img src={staticFile('brand/icon-192.png')} style={{ width: 54, height: 54 }} />
          <span style={{ fontFamily: SANS, fontWeight: 800, fontSize: 42, color: C.text }}>Your PC</span>
        </div>
        <div style={{ position: 'absolute', left: -150, right: -150, top: 1150, transform: 'translateY(-50%)', display: 'flex', justifyContent: 'center', gap: 26 }}>
          {badges.map(b => {
            const k = pop(f, local.cue(b.cue) - 6, { damping: 13, stiffness: 160 });
            return (
              <div key={b.cue} style={{
                display: 'flex', alignItems: 'center', gap: 14, padding: '16px 26px', borderRadius: 40, background: 'rgba(36,36,42,0.96)', border: `1.5px solid ${C.line2}`,
                fontFamily: SANS, fontSize: 30, fontWeight: 600, color: C.text, opacity: Math.min(1, k * 2), transform: `translateY(${(1 - k) * 60}px)`, whiteSpace: 'nowrap',
              }}>
                <Fluent name={b.icon} size={32} color={C.accentText} />{b.text}
              </div>
            );
          })}
        </div>
      </Camera>

      <Sfx at={idea.cue('command') - 6} name="impact" volume={0.3} />
      {rowCues.map(at => <Sfx key={at} at={at - 4} name="pop" volume={0.25} />)}
      {[0, 1, 2, 3].map(i => <Sfx key={i} at={steps + i * 12} name="blip" volume={0.2} />)}
      {TRIGGERS.map(t => <Sfx key={t.cue} at={triggers.cue(t.cue) - 4} name="pop-high" volume={0.16} />)}
      {HELPERS.map(h => <Sfx key={h.cue} at={helpers.cue(h.cue) - 4} name="pop-low" volume={0.25} />)}
      <Sfx at={local.from - 4} name="whoosh" volume={0.3} />
      {badges.map(b => <Sfx key={b.cue} at={local.cue(b.cue) - 4} name="blip-low" volume={0.16} />)}
    </AbsoluteFill>
  );
};
