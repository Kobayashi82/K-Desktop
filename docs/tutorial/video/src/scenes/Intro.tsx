import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../components/Camera';
import { Cursor } from '../components/Cursor';
import { AppWindow, ContextMenu, Taskbar, VolumeFlyout, Wallpaper } from '../components/Desktop';
import { Fluent, KdIcon } from '../components/Icon';
import { Keycap } from '../components/Keys';
import { MiniMenu, MouseShape } from '../components/Props';
import { Sfx } from '../components/Sfx';
import { VoiceBars } from '../components/VoiceBars';
import { ease, glide, IN, inOut, keys, OUT, pop, SMOOTH, tween } from '../lib/anim';
import { useBeats } from '../lib/timeline';
import { C, SANS } from '../theme';

const TASKBAR = [
  { icon: 'folder_24_filled', color: 'yellow' },
  { icon: 'globe_24_filled', color: 'blue' },
  { icon: 'document_text_24_filled', color: 'sky' },
  { icon: 'music_note_2_24_filled', color: 'pink' },
  { icon: 'xbox_controller_24_filled', color: 'indigo' },
];
// Centres of the taskbar icons (centred row: start button + 5 apps, 44 px + 10 px gap).
const iconX = (i: number) => 960 - (6 * 44 + 5 * 10) / 2 + 54 * (i + 1) + 22;
const BAR_Y = 1080 - 28;

export const Intro: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const h1 = B('hook1');
  const h2 = B('hook2');
  const h3 = B('hook3');
  const title = B('title');
  const promise = B('promise');

  const open = h2.seg(0);
  const arrange = h2.seg(1);
  const volume = h2.seg(2);
  const menus = h2.seg(3);
  const keyAt = h3.cue('key');
  const pressAt = h3.seg(1) - 10;
  const mouseAt = h3.cue('mouse');
  const voiceAt = h3.cue('voice');
  const logoAt = title.from;

  // ── the desktop, the slow way ─────────────────────────────────────
  const opened = [open + 6, open + 20, open + 34];
  const winPop = opened.map(at => pop(f, at, { damping: 15, stiffness: 160 }));
  // Notepad dragged to the left, Explorer to the right while «arranging»; at the key press, all snap into place.
  const drag1 = ease(f, arrange + 8, arrange + 34);
  const drag2 = ease(f, arrange + 40, arrange + 64);
  const snap = pop(f, pressAt, { damping: 18, stiffness: 170 });
  const np = { x: tween(f, arrange + 8, arrange + 34, 330, 140) + (8 - tween(f, arrange + 8, arrange + 34, 330, 140)) * snap, y: 120 + (8 - 120) * snap, w: 720 + (944 - 720) * snap, h: 470 + (1008 - 470) * snap };
  const ex = { x: tween(f, arrange + 40, arrange + 64, 900, 1060) + (968 - tween(f, arrange + 40, arrange + 64, 900, 1060)) * snap, y: 230 + (8 - 230) * snap, w: 760 + (944 - 760) * snap, h: 500 + (496 - 500) * snap };
  const br = { x: 520 + (968 - 520) * snap, y: 330 + (512 - 330) * snap, w: 860 + (944 - 860) * snap, h: 520 + (504 - 520) * snap };

  const cursorPath = [
    { at: 0, x: 1100, y: 640 },
    // hook 1: the same loop, again and again
    ...[0, 1, 2].flatMap(k => [
      { at: 8 + k * 40, x: iconX(0), y: BAR_Y, dur: 18 },
      { at: 26 + k * 40, x: 760, y: 420, dur: 16 },
    ]),
    { at: open - 6, x: iconX(2), y: BAR_Y, dur: 10 },
    { at: opened[1] - 10, x: iconX(0), y: BAR_Y, dur: 9 },
    { at: opened[2] - 10, x: iconX(1), y: BAR_Y, dur: 9 },
    { at: arrange, x: 650, y: 145, dur: 8 },
    { at: arrange + 8, x: 460, y: 145, dur: 26 },
    { at: arrange + 34, x: 1250, y: 255, dur: 6 },
    { at: arrange + 40, x: 1410, y: 255, dur: 24 },
    { at: volume - 4, x: 1775, y: 1052, dur: 16 },
    { at: volume + 24, x: 1710, y: 968, dur: 10 },
    { at: volume + 34, x: 1780, y: 968, dur: 12 },
    { at: volume + 46, x: 1650, y: 968, dur: 12 },
    { at: menus - 2, x: 1300, y: 330, dur: 12 },
    { at: menus + 16, x: 1340, y: 410, dur: 8 },
    { at: menus + 26, x: 1580, y: 410, dur: 10 },
    { at: menus + 38, x: 1600, y: 480, dur: 8 },
  ];
  const clicks = [
    ...[0, 1, 2].map(k => ({ at: 26 + k * 40 })),
    { at: open + 4 }, ...opened.slice(1).map(a => ({ at: a - 1 })),
    { at: arrange + 6 }, { at: arrange + 38 },
    { at: volume + 14 }, { at: menus + 12, button: 'right' as const }, { at: menus + 46 },
  ];
  const flyout = inOut(f, volume + 16, menus + 4, 8);
  const level = keys(f, [[volume + 24, 30], [volume + 36, 72], [volume + 50, 38]]);
  const ctx = inOut(f, menus + 13, keyAt, 6);
  const sub = inOut(f, menus + 30, keyAt, 6);

  // The click counter of hook 1.
  const counter = Math.floor(1180 + Math.max(0, f) * 1.7);

  // ── the camera over the desktop ──────────────────────────────────
  const shots = [
    { at: 0, x: 980, y: 640, s: 1.22, rx: 10, ry: -6 },
    { at: 2, dur: h1.dur + 20, x: 960, y: 560, s: 1.04, rx: 4, ry: 2 },
    { at: open - 10, dur: 30, x: 960, y: 640, s: 1.12, rx: 0, ry: 0 },
    { at: arrange - 6, dur: 26, x: 860, y: 420, s: 1.06 },
    { at: volume - 8, dur: 24, x: 1600, y: 880, s: 1.75 },
    { at: menus - 8, dur: 24, x: 1340, y: 470, s: 1.45 },
    { at: keyAt - 14, dur: 30, x: 960, y: 540, s: 0.86, rx: 8 },
  ];
  const dim = ease(f, keyAt - 14, keyAt + 6) * (1 - 0.7 * inOut(f, pressAt, pressAt + 40, 4));
  const desktopGone = ease(f, logoAt - 26, logoAt - 4, IN);

  // ── one key, one mouse, one voice ────────────────────────────────
  const keyDrop = pop(f, keyAt, { damping: 11, stiffness: 150 });
  const keyDown = inOut(f, pressAt, pressAt + 10, 3);
  const keyAside = ease(f, mouseAt - 6, mouseAt + 16);
  const mouseIn = pop(f, mouseAt, { damping: 13 });
  const sideClick = inOut(f, mouseAt + 22, mouseAt + 30, 3);
  const miniMenu = pop(f, mouseAt + 26, { damping: 14, stiffness: 170 });
  const micIn = pop(f, voiceAt - 4, { damping: 13 });
  const trioOut = ease(f, logoAt - 24, logoAt - 6, IN);

  // ── the logo ─────────────────────────────────────────────────────
  const logo = pop(f, logoAt, { damping: 12, stiffness: 110 });
  const burst = ease(f, logoAt, logoAt + 40, OUT);
  const word = ease(f, logoAt + 8, logoAt + 30, OUT);
  const up = glide(f, promise.from - 4, 30);
  const tiles = [
    { cue: 'commands', label: 'Commands', icon: 'flash_24_filled', color: 'gold' },
    { cue: 'quickmenus', label: 'QuickMenus', icon: 'text_bullet_list_tree_24_filled', color: 'purple' },
    { cue: 'events', label: 'Events', icon: 'arrow_forward_down_lightning_24_filled', color: 'orange' },
    { cue: 'macros', label: 'Macros', icon: 'record_24_filled', color: 'red' },
    { cue: 'kboard', label: 'K-Board', icon: 'keyboard_layout_float_24_filled', color: 'indigo' },
    { cue: 'voice', label: 'Voice', icon: 'mic_24_filled', color: 'teal' },
    { cue: 'phone', label: 'Remote', icon: 'phone_24_filled', color: 'sky' },
    { cue: 'assistants', label: 'Assistants', icon: 'bot_24_filled', color: 'pink' },
  ];
  const orbit = (f - promise.from) / 260;

  return (
    <AbsoluteFill>
      {/* The desktop */}
      <AbsoluteFill style={{ opacity: 1 - desktopGone, transform: `scale(${1 + desktopGone * 0.3})` }}>
        <Camera shots={shots} clamp>
          <Wallpaper />
          {[{ k: 'notepad' as const, p: np, at: 0 }, { k: 'explorer' as const, p: ex, at: 1 }, { k: 'browser' as const, p: br, at: 2 }].map(({ k, p, at }) => (
            <div key={k} style={{
              position: 'absolute', left: p.x, top: p.y, transformOrigin: `${iconX([2, 0, 1][at]) - p.x}px ${BAR_Y - p.y}px`,
              transform: `scale(${winPop[at]})`, opacity: Math.min(1, winPop[at] * 2),
            }}>
              <AppWindow kind={k} width={p.w} height={p.h} style={{ position: 'relative' }} />
            </div>
          ))}
          <Taskbar apps={TASKBAR.map((a, i) => ({ ...a, active: f > opened[[1, 2, 0, 9, 9][i] ?? 9] }))} />
          <VolumeFlyout level={level} style={{ left: 1520, top: 930, opacity: flyout, transform: `translateY(${(1 - flyout) * 20}px)` }} />
          <ContextMenu items={['View', 'Sort by', 'Refresh', 'New', 'Display settings', 'Personalize']} hot={f > menus + 24 ? 3 : f > menus + 16 ? 1 : -1}
            style={{ left: 1310, top: 340, opacity: ctx, transform: `scale(${0.92 + 0.08 * ctx})`, transformOrigin: 'top left' }} />
          <ContextMenu items={['Folder', 'Shortcut', 'Text Document', 'Compressed (zip)']} hot={f > menus + 40 ? 1 : -1}
            style={{ left: 1562, top: 440, opacity: sub, transform: `scale(${0.92 + 0.08 * sub})`, transformOrigin: 'top left' }} />
          <Cursor path={cursorPath} clicks={clicks} show={1 - ease(f, keyAt - 10, keyAt)} />
          {/* Ghost cursors: the same moves, again and again */}
          {[18, 36].map((lag, i) => (
            <div key={lag} style={{ opacity: (0.35 - i * 0.12) * (1 - ease(f, open - 20, open)) }}>
              <Cursor path={cursorPath.map(p => ({ ...p, at: p.at + lag }))} />
            </div>
          ))}
          <div style={{
            position: 'absolute', left: 1420, top: 120, padding: '12px 20px', borderRadius: 14, background: 'rgba(20,20,26,0.85)', border: `1px solid ${C.line2}`,
            fontFamily: SANS, color: C.text, opacity: inOut(f, 10, open - 6, 14), display: 'flex', gap: 14, alignItems: 'baseline',
          }}>
            <span style={{ fontSize: 18, color: C.dim }}>Clicks today</span>
            <span style={{ fontSize: 34, fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{counter.toLocaleString('en-US')}</span>
          </div>
        </Camera>
        <AbsoluteFill style={{ background: `rgba(8,8,12,${0.72 * dim})`, backdropFilter: undefined }} />
      </AbsoluteFill>

      {/* One key… */}
      <AbsoluteFill style={{ opacity: 1 - trioOut, transform: `scale(${1 + trioOut * 0.25})` }}>
        <div style={{
          position: 'absolute', left: 960 + (430 - 960) * keyAside, top: 520 + (-700 * (1 - keyDrop)), transform: `translate(-50%, -50%) scale(${1.6 - 0.45 * keyAside})`,
          opacity: Math.min(1, keyDrop * 3),
        }}>
          <Keycap label={<Fluent name="flash_24_filled" size={64} color={keyDown > 0.3 || ease(f, pressAt - 6, pressAt) * (1 - 0.6 * keyAside) > 0.5 ? '#1a1306' : C.accentText} />}
            height={150} width={150} down={keyDown} lit={ease(f, pressAt - 6, pressAt) * (1 - 0.6 * keyAside)} />
        </div>
        {/* The shock wave of the press */}
        {f > pressAt && f < pressAt + 30 && (
          <div style={{
            position: 'absolute', left: 960, top: 540, width: 200, height: 200, marginLeft: -100, marginTop: -100, borderRadius: '50%',
            border: `6px solid ${C.gold1}`, opacity: 1 - ease(f, pressAt, pressAt + 30, OUT), transform: `scale(${1 + ease(f, pressAt, pressAt + 30, OUT) * 7})`,
          }} />
        )}
        {/* …a flick of the mouse… */}
        <div style={{ position: 'absolute', left: 860, top: 540, transform: `translate(-50%, -50%) scale(${mouseIn})`, opacity: mouseIn }}>
          <MouseShape side={sideClick} glow={ease(f, mouseAt + 10, mouseAt + 22)} />
        </div>
        <div style={{ position: 'absolute', left: 975, top: 390, transform: `scale(${miniMenu})`, transformOrigin: 'top left', opacity: miniMenu }}>
          <MiniMenu />
        </div>
        {/* …or your voice */}
        <div style={{ position: 'absolute', left: 1540, top: 540, transform: `translate(-50%, -50%) scale(${micIn})`, opacity: micIn, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 26 }}>
          <KdIcon icon="mic_24_filled" color="teal" size={120} glow={0.6} />
          <VoiceBars beat="hook3" bars={18} width={260} height={90} color={C.teal} gain={1.4} />
        </div>
      </AbsoluteFill>

      {/* K-Desktop */}
      <AbsoluteFill style={{ transform: `translateY(${-230 * up}px) scale(${1 - 0.3 * up})` }}>
        {f >= logoAt - 2 && (
          <>
            <div style={{
              position: 'absolute', left: 960, top: 450, width: 900, height: 900, marginLeft: -450, marginTop: -450, borderRadius: '50%',
              background: 'radial-gradient(circle, rgba(255,210,110,0.45), rgba(224,168,21,0.08) 45%, transparent 70%)',
              transform: `scale(${0.3 + burst * 1.2})`, opacity: 1 - burst * 0.6,
            }} />
            {Array.from({ length: 14 }, (_, i) => (
              <div key={i} style={{
                position: 'absolute', left: 960, top: 450, width: 4, height: 420, marginLeft: -2, transformOrigin: '2px 0',
                transform: `rotate(${i * (360 / 14) + f * 0.15}deg) scaleY(${burst})`,
                background: 'linear-gradient(180deg, rgba(255,217,112,0.5), transparent)', opacity: (1 - burst) * 0.9,
              }} />
            ))}
            <Img src={staticFile('brand/icon-512.png')} style={{
              position: 'absolute', left: 960 - 130, top: 450 - 130, width: 260, height: 260,
              transform: `scale(${logo}) rotate(${(1 - logo) * -25}deg)`, filter: `drop-shadow(0 20px 60px rgba(224,168,21,${0.55 * logo}))`,
            }} />
            <div style={{ position: 'absolute', left: 0, right: 0, top: 640, textAlign: 'center', fontFamily: SANS, fontWeight: 800, fontSize: 120, letterSpacing: -3, color: C.text }}>
              <span style={{ display: 'inline-block', clipPath: `inset(0 ${(1 - word) * 100}% 0 0)` }}>
                K-Desktop <span style={{ color: C.accentText }}>5</span>
              </span>
            </div>
          </>
        )}
      </AbsoluteFill>

      {/* What this video covers */}
      <AbsoluteFill style={{ perspective: 1600 }}>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 590, display: 'flex', justifyContent: 'center', gap: 34, transform: `rotateX(${8 - 4 * up}deg) rotateY(${Math.sin(orbit) * 6}deg)` }}>
          {f > promise.from - 10 && tiles.map((t, i) => {
            const at = promise.cue(t.cue);
            const k = pop(f, at - 4, { damping: 12, stiffness: 150 });
            const lit = inOut(f, at - 2, at + 26, 6);
            return (
              <div key={t.cue} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 18, opacity: Math.min(1, k * 2), transform: `translateY(${(1 - k) * 80 + Math.sin(f / 20 + i) * 6}px) scale(${0.6 + 0.4 * k})` }}>
                <KdIcon icon={t.icon} color={t.color} size={118} glow={0.25 + lit * 0.9} />
                <div style={{ fontFamily: SANS, fontWeight: 700, fontSize: 25, color: lit > 0.2 ? C.accentText : C.text }}>{t.label}</div>
              </div>
            );
          })}
        </div>
      </AbsoluteFill>

      {/* Sound */}
      {[0, 1, 2].map(k => <Sfx key={k} at={26 + k * 40} name="click" volume={0.25} />)}
      {opened.map(a => <Sfx key={a} at={a} name="pop" volume={0.3} />)}
      <Sfx at={arrange + 8} name="whoosh-short" volume={0.2} />
      <Sfx at={arrange + 40} name="whoosh-short" volume={0.2} />
      <Sfx at={volume + 14} name="click" volume={0.3} />
      <Sfx at={menus + 12} name="click" volume={0.3} />
      <Sfx at={menus + 46} name="click" volume={0.3} />
      <Sfx at={keyAt - 2} name="whoosh-down" volume={0.35} />
      <Sfx at={keyAt + 4} name="impact" volume={0.35} />
      <Sfx at={pressAt} name="key" volume={0.7} />
      <Sfx at={pressAt + 2} name="whoosh" volume={0.35} />
      <Sfx at={mouseAt} name="pop-low" volume={0.35} />
      <Sfx at={mouseAt + 24} name="click" volume={0.4} />
      <Sfx at={mouseAt + 26} name="pop-high" volume={0.3} />
      <Sfx at={voiceAt - 4} name="pop" volume={0.35} />
      <Sfx at={logoAt - 46} name="rise" volume={0.45} />
      <Sfx at={logoAt} name="impact" volume={0.55} />
      <Sfx at={logoAt + 2} name="chime" volume={0.3} />
      {tiles.map(t => <Sfx key={t.cue} at={promise.cue(t.cue) - 4} name="blip" volume={0.18} />)}
    </AbsoluteFill>
  );
};
