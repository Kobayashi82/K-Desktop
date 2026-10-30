import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { useCapture } from '../components/Capture';
import { Camera } from '../components/Camera';
import { Part } from '../components/Chapter';
import { Cursor } from '../components/Cursor';
import { AppWindow, Taskbar, Wallpaper } from '../components/Desktop';
import { Kinetic } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Keycap, Mono } from '../components/Keys';
import { Osd } from '../components/Osd';
import { MouseShape } from '../components/Props';
import { MAIN_MENU, QmItem, QuickMenuPanel, qi, VOLUME_MENU } from '../components/QuickMenu';
import { Sfx } from '../components/Sfx';
import { Callout } from '../components/Ui';
import { ease, glide, inOut, pop, rand, SMOOTH } from '../lib/anim';
import { BeatTimes, useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';
import { t } from '../lib/i18n';

const SCALE = 1.6;
const ROW = 34 * SCALE; // row height + margins in QuickMenuPanel
const MENU = { x: 760, y: 250 };
// Vertical centre of row i of a panel at y (items: 32 px rows + 1 px margins; separators 9 px).
const rowY = (items: QmItem[], i: number, y: number) => {
  let at = y + 6 * SCALE;
  for (let k = 0; k < i; k++) at += items[k].kind === 'sep' ? 9 * SCALE : ROW;
  return at + ROW / 2;
};

const Intro: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const real = useCapture('quickmenu-closeup');
  const icons = MAIN_MENU.filter(i => i.kind === 'item') as Extract<QmItem, { kind: 'item' }>[];
  const gather = ease(f, b.cue('qm') - 10, b.cue('qm') + 20, SMOOTH);
  return (
    <AbsoluteFill>
      <Kinetic text={t('One key per command…')} at={b.from} x={960} y={200} size={64} color={C.dim} out={b.cue('qm') - 12} weight={700} />
      {Array.from({ length: 16 }, (_, i) => {
        const it = icons[i % icons.length];
        const sx = 200 + rand(i + 1) * 1520;
        const sy = 320 + rand(i + 30) * 560;
        const tx = 960 - 60 + (i % 4) * 0;
        const ty = 300 + (i % 8) * 64;
        const x = sx + (tx - sx) * gather + Math.sin(f / 30 + i) * 10 * (1 - gather);
        const y = sy + (ty - sy) * gather + Math.cos(f / 25 + i) * 10 * (1 - gather);
        const k = pop(f, b.from + i * 2);
        return <div key={i} style={{ position: 'absolute', left: x, top: y, transform: `scale(${k * (1 - 0.5 * gather)})`, opacity: 1 - gather * (i >= 8 ? 1 : 0.2) }}><KdIcon icon={it.icon} color={it.color} size={84} /></div>;
      })}
      <div style={{ position: 'absolute', left: 960, top: 560, transform: `translate(-50%, -50%) scale(${1.6 * pop(f, b.cue('qm') + 6, { damping: 13 })})` }}>
        <Img src={staticFile(real.file)} style={{ width: real.width, height: real.height, filter: 'drop-shadow(0 30px 45px rgba(0,0,0,0.6))' }} />
      </div>
      <Sfx at={b.cue('qm') - 6} name="whoosh" volume={0.3} />
      <Sfx at={b.cue('qm') + 8} name="pop" volume={0.3} />
    </AbsoluteFill>
  );
};

/** The menu at the pointer: what it has, and what each click does. */
const OnDesktop: React.FC<{ open: BeatTimes; clicks: BeatTimes }> = ({ open, clicks }) => {
  const f = useCurrentFrame();
  const bloom = open.cue('blooms');
  const subAt = open.cue('submenus');
  const left = clicks.cue('left');
  const right = clicks.cue('right');
  const middle = clicks.cue('middle');
  const kb = clicks.cue('keyboard');

  // The menu: opens, closes on the left click, opens again, stays through the right clicks.
  const reopen = left + 34;
  const firstOpen = pop(f, bloom - 4, { damping: 14, stiffness: 170 }) * (1 - ease(f, left + 6, left + 14));
  const secondOpen = pop(f, reopen, { damping: 14, stiffness: 170 });
  const menuShow = f < reopen ? firstOpen : secondOpen;
  const appear = f < reopen ? ease(f, bloom - 4, bloom + 18) : ease(f, reopen, reopen + 14);
  const sub = f < reopen ? inOut(f, subAt - 4, left - 4, 6) : ease(f, right - 30, right - 20);
  const browser = pop(f, left + 10);

  const volIdx = MAIN_MENU.findIndex(i => i.kind === 'item' && i.label === 'Volume');
  const browserIdx = 1;
  const musicIdx = 2;
  const subX = MENU.x + 250 * SCALE - 8;
  const subY = rowY(MAIN_MENU, volIdx, MENU.y) - ROW / 2 - 6 * SCALE;
  const kbHot = Math.min(4, Math.max(1, 1 + Math.floor((f - kb) / 8)));
  const hot = f < left + 6 ? (f > left - 16 ? browserIdx : -1)
    : f < reopen + 10 ? -1
      : f < middle - 20 ? volIdx
        : f < kb - 4 ? musicIdx : kbHot;
  const subHot = f > right - 22 && f < middle - 20 ? 0 : -1;
  const flash = Math.max(inOut(f, left - 2, left + 8, 3), inOut(f, middle - 2, middle + 8, 3));
  const subFlash = [0, 12, 24].reduce((m, d) => Math.max(m, inOut(f, right + d - 2, right + d + 6, 3)), 0);
  const volume = 40 + [0, 12, 24].filter(d => f > right + d).length * 5;
  const editing = pop(f, middle + 8, { damping: 15 });

  const path = [
    { at: 0, x: 900, y: 700 },
    { at: open.cue('mouse5') - 16, x: MENU.x - 4, y: MENU.y - 4, dur: 14 },
    { at: left - 18, x: MENU.x + 160, y: rowY(MAIN_MENU, browserIdx, MENU.y), dur: 12 },
    { at: left + 16, x: MENU.x - 4, y: MENU.y - 4, dur: 14 },
    { at: right - 34, x: MENU.x + 160, y: rowY(MAIN_MENU, volIdx, MENU.y), dur: 10 },
    { at: right - 22, x: subX + 160, y: rowY(VOLUME_MENU, 0, subY), dur: 10 },
    { at: middle - 20, x: MENU.x + 160, y: rowY(MAIN_MENU, musicIdx, MENU.y), dur: 14 },
  ];
  const cursorClicks = [
    { at: left, button: 'left' as const }, ...[0, 12, 24].map(d => ({ at: right + d, button: 'right' as const })), { at: middle, button: 'middle' as const },
  ];
  const which = f > middle - 4 ? 'middle' : f > right - 4 ? 'right' : f > left - 4 ? 'left' : 'side';
  const shots = [
    { at: 0, x: 960, y: 560, s: 1.0, ry: 4 },
    { at: bloom - 10, dur: 30, x: 1060, y: 520, s: 1.12, ry: 0 },
    { at: subAt - 10, dur: 30, x: 1180, y: 560, s: 1.1 },
    { at: middle - 10, dur: 30, x: 1100, y: 540, s: 1.0 },
  ];
  return (
    <AbsoluteFill>
      <Camera shots={shots} clamp>
        <Wallpaper hue={20} />
        <AppWindow kind="player" width={760} height={420} style={{ left: 120, top: 520 }} focus={false} />
        <div style={{ position: 'absolute', left: 1180, top: 120, transform: `scale(${browser})`, opacity: Math.min(1, browser * 2) }}>
          <AppWindow kind="browser" width={640} height={460} style={{ position: 'relative' }} />
        </div>
        <Taskbar apps={[{ icon: 'folder_24_filled', color: 'yellow' }, { icon: 'globe_24_filled', color: 'blue', active: browser > 0.5 }, { icon: 'music_note_2_24_filled', color: 'pink', active: true }]} />
        <div style={{ position: 'absolute', left: MENU.x, top: MENU.y, transform: `scale(${0.6 + 0.4 * menuShow})`, transformOrigin: '0 0', opacity: Math.min(1, menuShow * 2) }}>
          <QuickMenuPanel items={MAIN_MENU} scale={SCALE} appear={appear} hot={hot} flash={flash} />
        </div>
        <div style={{ position: 'absolute', left: subX, top: subY, transform: `scale(${0.7 + 0.3 * sub})`, transformOrigin: '0 0', opacity: sub * menuShow }}>
          <QuickMenuPanel items={VOLUME_MENU} scale={SCALE} appear={sub} hot={subHot} flash={subFlash} width={210} />
        </div>
        <Callout show={inOut(f, open.cue('icons') - 2, open.cue('separators') + 6, 6)} x={MENU.x - 30} y={rowY(MAIN_MENU, 2, MENU.y)} align="right">{t('Icons')}</Callout>
        <Callout show={inOut(f, open.cue('titles') - 2, open.cue('submenus') + 8, 6)} x={MENU.x - 30} y={rowY(MAIN_MENU, 0, MENU.y)} align="right">{t('Titles')}</Callout>
        <Callout show={inOut(f, open.cue('separators') - 2, subAt + 20, 6)} x={MENU.x - 30} y={rowY(MAIN_MENU, 5, MENU.y) - ROW / 2 + 6} align="right">{t('Separators')}</Callout>
        <Callout show={inOut(f, subAt + 4, left - 10, 6)} x={subX + 210 * SCALE + 30} y={subY + 40}>{t('Submenus')}</Callout>
        <div style={{ position: 'absolute', left: 990, top: 820 }}>
          <Osd icon="speaker_2_24_filled" color="teal" text={`Volume: ${volume} %`} bar={volume} show={inOut(f, right, middle - 10, 6)} />
        </div>
        <Cursor path={path} clicks={cursorClicks} />
      </Camera>

      {/* Which button */}
      <div style={{ position: 'absolute', left: 80, top: 230, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 14, opacity: inOut(f, open.from, kb - 6, 10) }}>
        <MouseShape scale={0.6} side={which === 'side' ? inOut(f, open.cue('mouse5'), open.cue('mouse5') + 10, 3) : 0} glow={which === 'side' ? inOut(f, open.cue('mouse5') - 4, bloom + 10, 4) : 0}
          left={which === 'left' ? 1 : 0} right={which === 'right' ? 1 : 0} wheel={which === 'middle' ? 1 : 0} />
        <div style={{ fontFamily: SANS, fontSize: 26, fontWeight: 700, color: C.accentText, textAlign: 'center', width: 260 }}>
          {{ side: t('Mouse 5'), left: t('Left click'), right: t('Right click'), middle: t('Middle click') }[which]}
        </div>
        <div style={{ fontFamily: SANS, fontSize: 20, color: C.dim, textAlign: 'center', width: 260 }}>
          {t({ side: 'opens it', left: 'runs and closes', right: 'runs, stays open', middle: 'edits the item' }[which])}
        </div>
      </div>

      {/* Middle click: the item's command, ready to edit */}
      <div style={{ position: 'absolute', right: 90, top: 250, width: 540, padding: 24, borderRadius: 22, background: 'rgba(32,32,38,0.97)', border: `1.5px solid rgba(242,193,74,0.6)`, opacity: Math.min(1, editing * 1.5) * (1 - ease(f, kb - 8, kb)), transform: `translateX(${(1 - editing) * 80}px)` }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
          <KdIcon icon="music_note_2_24_filled" color="pink" size={60} />
          <div>
            <div style={{ fontFamily: SANS, fontSize: 15, fontWeight: 700, letterSpacing: 2, color: C.accentText }}>{t('EDITING')}</div>
            <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>{t('Music')}</div>
          </div>
        </div>
        <div style={{ marginTop: 16, display: 'flex', flexDirection: 'column', gap: 8 }}>
          <Mono size={18} color={C.dim}>{t('Run application · AIMP.exe')}</Mono>
          <Mono size={18} color={C.dim}>{t('Audio output · Speakers')}</Mono>
        </div>
      </div>

      {/* The keyboard works too */}
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 120, display: 'flex', justifyContent: 'center', gap: 16, opacity: inOut(f, kb - 6, clicks.to + 20, 8) }}>
        {['↑', '↓', '→', 'Enter', 'Esc'].map((k, i) => (
          <Keycap key={k} label={k} height={90} width={k.length > 1 ? 150 : 90} size={30} down={inOut(f, kb + 2 + i * 8, kb + 8 + i * 8, 2)} />
        ))}
      </div>

      <Sfx at={open.cue('mouse5')} name="click" volume={0.3} />
      <Sfx at={bloom - 4} name="pop" volume={0.35} />
      <Sfx at={subAt - 4} name="pop-high" volume={0.2} />
      <Sfx at={left} name="click" volume={0.35} />
      <Sfx at={left + 10} name="pop" volume={0.25} />
      {[0, 12, 24].map(d => <Sfx key={d} at={right + d} name="click" volume={0.3} />)}
      {[0, 12, 24].map(d => <Sfx key={`b${d}`} at={right + d + 1} name="blip" volume={0.15} />)}
      <Sfx at={middle} name="click" volume={0.3} />
      <Sfx at={middle + 8} name="whoosh-short" volume={0.25} />
      {[0, 8, 16, 24, 32].map(d => <Sfx key={`k${d}`} at={kb + 2 + d} name="key" volume={0.3} />)}
    </AbsoluteFill>
  );
};

const PALETTE_ITEMS = [
  qi('Steam', 'xbox_controller_24_filled', 'indigo'), qi('Calculator', 'calculator_24_filled', 'gray'), qi('Screenshot', 'screenshot_24_filled', 'green'),
  qi('Lock PC', 'lock_closed_24_filled', 'orange'), qi('Dark theme', 'dark_theme_24_filled', 'dark'), qi('Wake NAS', 'desktop_pulse_24_filled', 'green'),
] as Extract<QmItem, { kind: 'item' }>[];

/** The QuickMenu editor: drag commands in, above, below or inside; sizes; quick commands. */
const Editor: React.FC<{ editor: BeatTimes; quick: BeatTimes }> = ({ editor, quick }) => {
  const f = useCurrentFrame();
  const drag = editor.cue('drag');
  const above = editor.cue('above');
  const below = editor.cue('below');
  const inside = editor.cue('inside');
  const size = editor.cue('size');
  // The dragged item travels from the palette to the menu.
  const dragK = ease(f, drag, above - 4, SMOOTH);
  const target = f < below - 4 ? 'above' : f < inside - 4 ? 'below' : 'inside';
  const dropAt = inside + 20;
  const dropped = f > dropAt;
  const menuX = 260;
  const menuY = 230;
  const items: QmItem[] = [
    ...MAIN_MENU.slice(0, 5),
    ...(dropped ? [qi('Steam', 'xbox_controller_24_filled', 'indigo')] : []),
    ...MAIN_MENU.slice(5, 9),
    ...(f > quick.cue('program') + 20 ? [qi('Blender', 'cube_24_filled', 'orange')] : []),
    ...(f > quick.cue('folder') + 20 ? [qi('Photos', 'folder_24_filled', 'yellow')] : []),
    ...(f > quick.cue('web') + 20 ? [qi('GitHub', 'globe_24_filled', 'dark')] : []),
  ];
  const targetRow = 3; // «Games»
  const ty = rowY(MAIN_MENU, targetRow, menuY);
  const sizeK = keys2(f, size);
  const startX = 1270;
  const startY = 330;
  const gx = startX + (menuX + 120 - startX) * dragK + (target === 'inside' ? 60 : 0) * ease(f, inside - 4, inside + 6);
  const gy = startY + (ty - startY) * dragK + (target === 'below' ? 30 : target === 'above' ? -30 : 0) * dragK;
  const quickOn = f > quick.from - 10;
  const typed = (text: string, at: number) => text.slice(0, Math.max(0, Math.floor((f - at) * 1.6)));
  const field = f > quick.cue('web') ? typed('https://github.com', quick.cue('web')) : f > quick.cue('folder') ? typed('D:\\Photos', quick.cue('folder')) : typed('C:\\Program Files\\Blender\\blender.exe', quick.cue('program'));
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 120, top: 150, width: 1680, height: 790, borderRadius: 26, background: 'rgba(28,28,33,0.96)', border: `1px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.5)', transform: `translateY(${(1 - glide(f, editor.from - 10, 26)) * 60}px)` }}>
        <div style={{ position: 'absolute', left: 40, top: 26, display: 'flex', alignItems: 'center', gap: 16 }}>
          <Fluent name="text_bullet_list_tree_24_filled" size={32} color={C.accentText} />
          <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>{t('QuickMenu editor')}</span>
          <span style={{ fontFamily: SANS, fontSize: 18, color: C.dim }}>{t('Main')}</span>
          {['arrow_undo_24_filled', 'arrow_redo_24_filled'].map((ic, i) => (
            <div key={ic} style={{ marginLeft: i ? 0 : 30, width: 44, height: 44, borderRadius: 10, display: 'flex', alignItems: 'center', justifyContent: 'center', background: inOut(f, editor.cue('undo') + i * 8, editor.cue('undo') + i * 8 + 14, 4) > 0.3 ? C.accentSoft : 'rgba(255,255,255,0.06)' }}>
              <Fluent name={ic} size={22} color={C.text} />
            </div>
          ))}
          <div style={{ marginLeft: 30, display: 'flex', alignItems: 'center', gap: 10, fontFamily: SANS, fontSize: 18, color: C.dim }}>
            {t('Size')}
            {[t('Compact'), t('Normal'), t('Medium'), t('Large'), t('Very large'), t('Huge')].map((s, i) => (
              <span key={s} style={{ padding: '5px 10px', borderRadius: 8, fontSize: 15, background: Math.round(sizeK * 5) === i ? C.accentText : 'rgba(255,255,255,0.06)', color: Math.round(sizeK * 5) === i ? '#1a1306' : C.dim, fontWeight: 600 }}>{s}</span>
            ))}
          </div>
        </div>
        <div style={{ position: 'absolute', right: 40, top: 100, width: 420, bottom: 30, borderRadius: 18, background: 'rgba(255,255,255,0.04)', padding: 20, boxSizing: 'border-box' }}>
          <div style={{ fontFamily: SANS, fontSize: 20, fontWeight: 700, color: C.text, marginBottom: 14 }}>{t('Add to the menu')}</div>
          {PALETTE_ITEMS.map((p, i) => (
            <div key={p.label} style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '10px 12px', borderRadius: 12, marginBottom: 8, background: i === 0 && dragK > 0 && !dropped ? 'rgba(242,193,74,0.12)' : 'rgba(255,255,255,0.05)', opacity: i === 0 && dragK > 0 && !dropped ? 0.4 : 1 }}>
              <KdIcon icon={p.icon} color={p.color} size={40} />
              <span style={{ fontFamily: SANS, fontSize: 20, color: C.text }}>{t(p.label)}</span>
            </div>
          ))}
        </div>
      </div>
      <div style={{ position: 'absolute', left: menuX, top: menuY, transform: `scale(${1 + sizeK * 0.3})`, transformOrigin: '0 0' }}>
        <QuickMenuPanel items={items} scale={SCALE} hot={dropped && f < dropAt + 30 ? 5 : -1} flash={inOut(f, dropAt, dropAt + 20, 4)} />
      </div>
      {/* Drop indicator */}
      {dragK > 0.9 && !dropped && (
        <div style={{
          position: 'absolute', left: menuX + 10, top: target === 'above' ? ty - ROW / 2 - 2 : target === 'below' ? ty + ROW / 2 - 2 : ty - ROW / 2,
          width: 380, height: target === 'inside' ? ROW : 4, borderRadius: 4, border: target === 'inside' ? `2px solid ${C.accentText}` : 'none', background: target === 'inside' ? 'rgba(242,193,74,0.12)' : C.accentText,
        }}>
          <div style={{ position: 'absolute', left: 420, top: -14, fontFamily: SANS, fontSize: 24, fontWeight: 800, color: C.accentText, whiteSpace: 'nowrap' }}>{{ above: 'Above', below: 'Below', inside: 'Inside (submenu)' }[target]}</div>
        </div>
      )}
      {dragK > 0 && !dropped && (
        <div style={{ position: 'absolute', left: gx, top: gy, transform: 'translate(-20px, -20px) rotate(-3deg)', display: 'flex', alignItems: 'center', gap: 12, padding: '10px 16px', borderRadius: 12, background: 'rgba(40,40,48,0.98)', border: `1.5px solid ${C.accentText}`, boxShadow: '0 20px 40px rgba(0,0,0,0.5)' }}>
          <KdIcon icon="xbox_controller_24_filled" color="indigo" size={40} /><span style={{ fontFamily: SANS, fontSize: 20, color: C.text }}>{t('Steam')}</span>
        </div>
      )}
      {/* Rename */}
      <Callout show={inOut(f, editor.cue('rename') - 4, editor.cue('nest') + 10, 6)} x={menuX + 420} y={rowY(MAIN_MENU, 1, menuY)}>{t('F2 renames · change icon')}</Callout>
      {/* Quick command */}
      <div style={{
        position: 'absolute', left: 1000, top: 330, width: 760, padding: 28, borderRadius: 24, background: 'rgba(36,36,42,0.99)', border: `1.5px solid rgba(242,193,74,0.6)`, boxShadow: '0 30px 80px rgba(0,0,0,0.6)',
        opacity: quickOn ? pop(f, quick.from - 6) : 0, transform: `scale(${0.9 + 0.1 * (quickOn ? pop(f, quick.from - 6) : 0)})`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 20 }}>
          <Fluent name="flash_24_filled" size={30} color={C.accentText} />
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>{t('Quick command')}</span>
        </div>
        <div style={{ fontFamily: SANS, fontSize: 17, color: C.dim, marginBottom: 8 }}>{t('Program, folder or URL')}</div>
        <div style={{ height: 56, borderRadius: 12, background: '#1a1a1f', border: `1.5px solid ${C.accentText}`, display: 'flex', alignItems: 'center', padding: '0 18px' }}>
          <Mono size={22}>{field}</Mono><span style={{ width: 2, height: 26, background: C.accentText, marginLeft: 2 }} />
        </div>
        <div style={{ display: 'flex', gap: 12, marginTop: 18 }}>
          {[['program', 'cube_24_filled', 'orange', t('Program')], ['folder', 'folder_24_filled', 'yellow', t('Folder')], ['web', 'globe_24_filled', 'dark', t('Web')]].map(([c, ic, col, t]) => (
            <span key={c} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 14px', borderRadius: 12, background: f > quick.cue(c) - 2 ? C.accentSoft : 'rgba(255,255,255,0.05)', fontFamily: SANS, fontSize: 18, color: C.text }}>
              <KdIcon icon={ic} color={col} size={28} />{t}
            </span>
          ))}
        </div>
      </div>
      <Sfx at={drag} name="whoosh-short" volume={0.2} />
      <Sfx at={dropAt} name="pop" volume={0.3} />
      <Sfx at={size} name="whoosh-short" volume={0.15} />
      <Sfx at={quick.from - 6} name="pop-low" volume={0.25} />
      {['program', 'folder', 'web'].map(c => <Sfx key={c} at={quick.cue(c) + 20} name="pop-high" volume={0.22} />)}
    </AbsoluteFill>
  );
};

/** The «size» moment: the preview grows from compact to huge and back. */
const keys2 = (f: number, at: number) => Math.min(1, Math.max(0, (f - at) / 40)) * (1 - ease(f, at + 60, at + 80)) + ease(f, at + 60, at + 80) * 0.2;

export const QuickMenuScene: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const intro = B('intro');
  const open = B('open');
  const clicks = B('clicks');
  const editor = B('editor');
  const quick = B('quick');
  return (
    <AbsoluteFill>
      <Part from={0} to={open.cue('mouse5') - 16} first><Intro b={intro} /></Part>
      <Part from={open.cue('mouse5') - 16} to={editor.from - 6}><OnDesktop open={open} clicks={clicks} /></Part>
      <Part from={editor.from - 6} to={durationInFrames} last><Editor editor={editor} quick={quick} /></Part>
    </AbsoluteFill>
  );
};

