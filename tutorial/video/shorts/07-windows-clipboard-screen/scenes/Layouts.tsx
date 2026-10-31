import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { cursorAt, Cursor } from '../../../src/components/Cursor';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Keycap } from '../../../src/components/Keys';
import { Sfx } from '../../../src/components/Sfx';
import { Toggle } from '../../../src/components/Ui';
import { ease, glide, inOut, keys, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { Desk, DESK_W, lerpRect, Rect, Win, WORK_H, ZONES, ZoneOverlay } from './Desk';
import { MESS } from './Hook';

type R = Focus['rect'];
// The real Layouts page (tutorial/captures/en/arrangements.png).
const NAV: R = [12, 382, 216, 40];
const MONITORS: R = [576, 265, 218, 32];
const DRAWING: R = [866, 318, 716, 282];
const ZONE: R[] = [[868, 320, 420, 278], [1297, 320, 277, 164], [1297, 490, 277, 108]];
const TEMPLATES: R = [576, 618, 470, 42];
const SAVE: R = [1521, 96, 132, 34];

/** «Layouts», on the real page: the zones of a monitor and their programs, templates, and «Save windows». */
const Page: React.FC = () => {
  const B = useBeats();
  const what = B('what');
  const make = B('make');
  const put = B('put');
  const focus: Focus[] = [
    { from: what.cue('page') - 4, to: what.cue('cut') - 4, rect: NAV },
    { from: what.cue('cut') - 4, to: what.cue('cut') + 36, rect: MONITORS },
    { from: what.cue('cut') + 36, to: what.cue('code') - 4, rect: DRAWING, label: t('Zones'), at: 'bottom' },
    { from: what.cue('code') - 4, to: what.cue('chrome') - 4, rect: ZONE[0] },
    { from: what.cue('chrome') - 4, to: what.cue('terminal') - 4, rect: ZONE[1] },
    { from: what.cue('terminal') - 4, to: make.from, rect: ZONE[2] },
    { from: make.cue('templates') - 4, to: make.cue('split') - 4, rect: TEMPLATES },
    { from: make.cue('split') - 4, to: make.cue('save') - 6, rect: DRAWING, label: t('Click to split · drag the lines'), at: 'bottom' },
    { from: make.cue('save') - 6, to: put.from + 20, rect: SAVE, label: t('Save windows'), at: 'bottom' },
  ];
  return (
    <>
      <CaptureShot name="arrangements" focus={focus}
        shots={[
          { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
          { at: 4, dur: 30, x: 960, y: 540, s: 1.0, rx: 0 },
          { at: what.cue('cut') - 14, dur: 30, x: 1180, y: 440, s: 1.55 },
          { at: make.cue('templates') - 16, dur: 28, x: 1000, y: 540, s: 1.45 },
          { at: make.cue('save') - 22, dur: 22, x: 1400, y: 300, s: 1.5 },
        ]} />
      {focus.map(x => <Sfx key={x.from} at={x.from} name="blip" volume={0.12} />)}
    </>
  );
};

/** A K-Desktop style card on the stage: icon tile, name, description, and anything below. */
const Card: React.FC<{ icon: string; color: string; name: string; desc: string; show: number; children?: React.ReactNode; style?: React.CSSProperties }> = ({ icon, color, name, desc, show, children, style }) => (
  <div style={{
    position: 'absolute', width: 760, padding: '26px 30px', borderRadius: 24, boxSizing: 'border-box', fontFamily: SANS,
    background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
    opacity: Math.min(1, show * 1.4), transform: `translateX(${(1 - show) * 80}px) scale(${0.9 + 0.1 * show})`, ...style,
  }}>
    <div style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
      <KdIcon icon={icon} color={color} size={64} />
      <div>
        <div style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{name}</div>
        <div style={{ fontSize: 20, color: C.dim, marginTop: 4 }}>{desc}</div>
      </div>
    </div>
    {children}
  </div>
);

const Pill: React.FC<{ children: React.ReactNode; gold?: boolean; style?: React.CSSProperties }> = ({ children, gold, style }) => (
  <div style={{
    display: 'inline-flex', alignItems: 'center', gap: 12, padding: '12px 22px', borderRadius: 14, fontFamily: SANS, fontSize: 24, fontWeight: 600,
    color: gold ? '#1a1306' : C.text, background: gold ? 'linear-gradient(180deg, #FFD970, #F2B807)' : 'rgba(40,40,48,0.96)',
    border: gold ? undefined : `1.5px solid ${C.line2}`, boxShadow: '0 16px 40px rgba(0,0,0,0.45)', ...style,
  }}>{children}</div>
);

/** «Test» puts every window in its zone (opening the terminal), then the action and the event that do the same. */
const Put: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const put = B('put');
  const test = put.cue('test') + 14;
  const open = put.cue('open');
  const side = ease(f, put.cue('action') - 12, put.cue('action') + 14);
  const testPill = pop(f, put.from - 8, { damping: 14 });
  const press = inOut(f, test - 6, test + 4, 3);
  const term = pop(f, open + 4, { damping: 13 });
  const toggle = pop(f, open - 8, { damping: 14 });
  return (
    <AbsoluteFill>
      <Desk x={960 - 420 * side} y={keys(f, [[0, 530], [put.cue('action') + 14, 540]])} s={0.74 - 0.24 * side} apps={f > open ? ['code', 'chrome', 'terminal'] : ['code', 'chrome']}>
        {(['code', 'chrome'] as const).map((app, i) => <Win key={app} app={app} at={lerpRect(MESS[app], ZONES[i], ease(f, test + i * 5, test + i * 5 + 18, OUT))} />)}
        {f > open && <Win app="terminal" at={ZONES[2]} scale={0.7 + 0.3 * term} opacity={Math.min(1, term * 1.5)} active />}
        <ZoneOverlay show={inOut(f, test - 4, test + 30, 8)} />
      </Desk>
      <div style={{ position: 'absolute', left: 960 - 420 * side, top: 70, transform: `translateX(-50%) scale(${testPill * (1 - 0.06 * press)})`, opacity: 1 - side, display: 'flex', gap: 18 }}>
        <Pill gold><Fluent name="play_24_filled" size={24} color="#1a1306" /> {t('Test')}</Pill>
        <Pill style={{ opacity: Math.min(1, toggle * 1.4), transform: `translateX(${(1 - toggle) * 40}px)` }}>
          <Toggle on={ease(f, open - 2, open + 8)} /> {t('Open the programs that are not open')}
        </Pill>
      </div>
      <Card icon="grid_24_filled" color="teal" name={t('Window layout')} desc={t('Puts each program in its zone of a layout saved in Layouts.')} show={glide(f, put.cue('action') - 4, 20)} style={{ left: 1080, top: 230 }}>
        <div style={{ marginTop: 20, display: 'flex', alignItems: 'center', gap: 12, fontSize: 22, color: C.dim }}>
          {t('Layout')}
          <span style={{ padding: '8px 18px', borderRadius: 10, background: C.accentSoft, color: C.accentText, fontWeight: 700 }}>{t('Coding')}</span>
        </div>
      </Card>
      <Card icon="desktop_24_filled" color="blue" name={t('Screen plugged in or out')} desc={t('An event: it runs on its own.')} show={glide(f, put.cue('event') - 4, 20)} style={{ left: 1080, top: 560 }}>
        <div style={{ marginTop: 20, display: 'flex', alignItems: 'center', gap: 14, fontSize: 22, color: C.text }}>
          <Fluent name="arrow_right_24_filled" size={26} color={C.accentText} />
          <KdIcon icon="grid_24_filled" color="teal" size={34} />
          {t('Window layout')} <span style={{ color: C.dim }}>·</span> <span style={{ color: C.accentText, fontWeight: 700 }}>{t('Coding')}</span>
        </div>
      </Card>
      <Sfx at={test - 6} name="click" volume={0.3} />
      <Sfx at={test} name="whoosh-short" volume={0.3} />
      <Sfx at={open + 4} name="pop" volume={0.22} />
      <Sfx at={put.cue('action') - 4} name="pop" volume={0.22} />
      <Sfx at={put.cue('event') - 4} name="pop" volume={0.22} />
    </AbsoluteFill>
  );
};

const zoneAt = (x: number, y: number): number => ZONES.findIndex(z => x >= z.x * DESK_W && x <= (z.x + z.w) * DESK_W && y >= z.y * WORK_H && y <= (z.y + z.h) * WORK_H);

/** Shift while dragging shows the zones and a window drops into one; Win + Alt + arrows move it from zone to zone. */
const Snap: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const snap = B('snap');
  const shift = snap.cue('shift');
  const drop = snap.cue('drop');
  const hk = snap.cue('keys');
  const free: Rect = { x: 0.24, y: 0.2, w: 0.42, h: 0.5 };
  const grab = { x: free.x * DESK_W + 200, y: free.y * WORK_H + 16 };
  const path = [
    { at: snap.from - 10, x: 1000, y: 620 },
    { at: shift - 34, x: grab.x, y: grab.y, dur: 22 },
    { at: shift + 4, x: 1060, y: 330, dur: 30 },
    { at: drop - 40, x: 1300, y: 230, dur: 30 },
    { at: drop + 20, x: 1200, y: 640, dur: 26 },
  ];
  const c = cursorAt(f, path);
  const dragging = f >= shift - 8 && f < drop;
  // Held by the title bar from the click to the drop.
  const hand = f < drop ? c : cursorAt(drop, path);
  const dragged: Rect = f < shift - 8 ? free : { ...free, x: (hand.x - 200) / DESK_W, y: (hand.y - 16) / WORK_H };
  let chrome = lerpRect(dragged, ZONES[1], ease(f, drop, drop + 12, OUT));
  chrome = lerpRect(chrome, ZONES[2], ease(f, hk + 16, hk + 28, OUT));
  chrome = lerpRect(chrome, ZONES[1], ease(f, hk + 64, hk + 76, OUT));
  const zones = inOut(f, shift - 6, drop + 14, 8);
  const shiftKey = inOut(f, shift - 14, drop + 12, 8);
  const combo = pop(f, hk - 8, { damping: 14 });
  const arrow = f < hk + 56 ? '→' : '←';
  const tap = Math.max(inOut(f, hk + 12, hk + 22, 4), inOut(f, hk + 60, hk + 70, 4));
  return (
    <AbsoluteFill>
      <Desk y={490} s={0.78} apps={['code', 'chrome']}>
        <Win app="code" at={ZONES[0]} />
        <Win app="chrome" at={chrome} active lift={dragging ? 1 : 0} />
        <ZoneOverlay show={zones} hot={dragging ? zoneAt(c.x, c.y) : -1} />
        <Cursor path={path} clicks={[{ at: shift - 8 }]} scale={1.3} show={1 - ease(f, hk - 10, hk)} />
      </Desk>
      <div style={{ position: 'absolute', left: 960, top: 930, transform: 'translate(-50%, -50%)', display: 'flex', alignItems: 'center', gap: 16, opacity: shiftKey }}>
        <Keycap label="Shift" width={160} down={ease(f, shift - 12, shift - 4)} />
        <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 600, color: C.dim }}>{t('+ drag')}</span>
      </div>
      <div style={{ position: 'absolute', left: 960, top: 930, transform: `translate(-50%, -50%) scale(${combo})`, opacity: Math.min(1, combo * 1.4), display: 'flex', alignItems: 'center', gap: 14 }}>
        <Keycap label="Win" down={tap} />
        <span style={{ fontFamily: SANS, fontSize: 30, color: C.faint }}>+</span>
        <Keycap label="Alt" down={tap} />
        <span style={{ fontFamily: SANS, fontSize: 30, color: C.faint }}>+</span>
        <Keycap label={arrow} down={tap} size={32} />
      </div>
      <Sfx at={shift - 8} name="click" volume={0.25} />
      <Sfx at={shift - 4} name="blip" volume={0.15} />
      <Sfx at={drop} name="pop" volume={0.25} />
      <Sfx at={hk + 14} name="key" volume={0.3} />
      <Sfx at={hk + 62} name="key" volume={0.3} />
    </AbsoluteFill>
  );
};

/** Chapter «Window layouts». */
export const Layouts: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const put = B('put');
  const snap = B('snap');
  return (
    <AbsoluteFill>
      <Part from={0} to={put.from - 4} first><Page /></Part>
      <Part from={put.from - 4} to={snap.from - 4}><Put /></Part>
      <Part from={snap.from - 4} to={durationInFrames} last><Snap /></Part>
    </AbsoluteFill>
  );
};
