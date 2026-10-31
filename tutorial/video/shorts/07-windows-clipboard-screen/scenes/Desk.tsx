import React from 'react';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { WinFrame } from '../../../src/components/KdWindow';
import { Taskbar, Wallpaper } from '../../../src/components/Desktop';
import { t } from '../../../src/lib/i18n';
import { C, MONO, SANS } from '../../../src/theme';

/** A place on the screen, in fractions of the work area (above the taskbar). */
export type Rect = { x: number; y: number; w: number; h: number };

/** The «Coding» layout of the real capture (arrangements.png): Code 60 × 100 %, Chrome 40 × 60 %, Terminal 40 × 40 %. */
export const ZONES: Rect[] = [
  { x: 0, y: 0, w: 0.6, h: 1 },
  { x: 0.6, y: 0, w: 0.4, h: 0.6 },
  { x: 0.6, y: 0.6, w: 0.4, h: 0.4 },
];

/** Between two places, k from 0 to 1. */
export const lerpRect = (a: Rect, b: Rect, k: number): Rect => ({ x: a.x + (b.x - a.x) * k, y: a.y + (b.y - a.y) * k, w: a.w + (b.w - a.w) * k, h: a.h + (b.h - a.h) * k });

/** Size of the stage screen (16:9) and of its work area. */
export const DESK_W = 1600;
export const DESK_H = 900;
const BAR = 56;
export const WORK_H = DESK_H - BAR;

/** Pixels of a place on the stage screen (a 3 px gap between windows, as Windows leaves). */
export const px = (r: Rect, gap = 3) => ({ left: r.x * DESK_W + gap, top: r.y * WORK_H + gap, width: r.w * DESK_W - gap * 2, height: r.h * WORK_H - gap * 2 });

export type AppId = 'code' | 'chrome' | 'terminal';

const APPS: Record<AppId, { title: string; icon: string; color: string }> = {
  code: { title: t('main.ts - atlas - Visual Studio Code'), icon: 'code_24_filled', color: 'blue' },
  chrome: { title: t('Pull requests - Google Chrome'), icon: 'globe_24_filled', color: 'green' },
  terminal: { title: t('Terminal'), icon: 'window_console_20_filled', color: 'dark' },
};

const CODE = [
  ['#C586C0', 'import', '#9CDCFE', ' { Router } ', '#C586C0', 'from', '#CE9178', " './router';"],
  ['', '', '', '', '', '', '', ''],
  ['#569CD6', 'export', '#569CD6', ' function ', '#DCDCAA', 'start', '#D4D4D4', '(port: number) {'],
  ['#D4D4D4', '  ', '#569CD6', 'const ', '#4FC1FF', 'app', '#D4D4D4', ' = new Router();'],
  ['#D4D4D4', '  app.', '#DCDCAA', 'listen', '#D4D4D4', '(port);', '#6A9955', ' // ready'],
  ['#D4D4D4', '}', '', '', '', '', '', ''],
];

/** What each program shows: quiet, recognisable content. */
const Content: React.FC<{ app: AppId; w: number; h: number }> = ({ app, w, h }) => {
  if (app === 'code') {
    return (
      <div style={{ display: 'flex', height: '100%', background: '#1e1e1e' }}>
        <div style={{ width: 46, background: '#333', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 18, paddingTop: 14 }}>
          {['document_24_filled', 'search_24_filled', 'apps_24_filled'].map(i => <Fluent key={i} name={i} size={20} color="rgba(255,255,255,0.45)" />)}
        </div>
        <div style={{ flex: 1, padding: '14px 18px', fontFamily: MONO, fontSize: 16, lineHeight: '28px', overflow: 'hidden' }}>
          {Array.from({ length: Math.max(1, Math.floor((h - 70) / 28)) }, (_, i) => {
            const line = CODE[i % CODE.length];
            return (
              <div key={i} style={{ whiteSpace: 'nowrap' }}>
                <span style={{ color: '#6e7681', display: 'inline-block', width: 34 }}>{i + 1}</span>
                {[0, 2, 4, 6].map(j => <span key={j} style={{ color: line[j] }}>{line[j + 1]}</span>)}
              </div>
            );
          })}
        </div>
      </div>
    );
  }
  if (app === 'terminal') {
    return (
      <div style={{ height: '100%', background: '#0c0c0c', padding: '12px 16px', fontFamily: MONO, fontSize: 15, lineHeight: '24px', color: '#cccccc', boxSizing: 'border-box', overflow: 'hidden' }}>
        <div><span style={{ color: '#6CCB5F' }}>PS C:\atlas&gt;</span> npm run dev</div>
        <div style={{ color: '#888' }}>  ready in 412 ms</div>
        <div style={{ color: '#888' }}>  ➜ Local: http://localhost:5173/</div>
        <div><span style={{ color: '#6CCB5F' }}>PS C:\atlas&gt;</span> ▍</div>
      </div>
    );
  }
  return (
    <div style={{ height: '100%', background: '#202124', overflow: 'hidden' }}>
      <div style={{ height: 34, margin: '6px 12px', borderRadius: 17, background: 'rgba(255,255,255,0.08)', display: 'flex', alignItems: 'center', padding: '0 14px', fontFamily: SANS, fontSize: 13, color: C.dim }}>github.com/alexmorgan/atlas/pulls</div>
      <div style={{ padding: '10px 22px', display: 'flex', flexDirection: 'column', gap: 12 }}>
        {Array.from({ length: Math.max(1, Math.floor((h - 110) / 46)) }, (_, i) => (
          <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <Fluent name="branch_24_regular" size={18} color={i % 3 === 0 ? '#a371f7' : '#3fb950'} />
            <div style={{ height: 10, borderRadius: 5, background: 'rgba(255,255,255,0.22)', width: (w - 120) * (0.45 + 0.4 * Math.abs(Math.sin(i * 1.9))) }} />
          </div>
        ))}
      </div>
    </div>
  );
};

/** One program's window at a place of the stage screen. */
export const Win: React.FC<{ app: AppId; at: Rect; scale?: number; opacity?: number; active?: boolean; lift?: number }> = ({ app, at, scale = 1, opacity = 1, active, lift = 0 }) => {
  const p = px(at);
  const a = APPS[app];
  return (
    <WinFrame width={p.width} height={p.height} title={a.title} bar={36} bg="#1f1f23"
      icon={<KdIcon icon={a.icon} color={a.color} size={18} />}
      style={{
        left: p.left, top: p.top, opacity, transform: `scale(${scale})`, fontSize: 12,
        boxShadow: `0 ${20 + lift * 30}px ${50 + lift * 40}px rgba(0,0,0,${0.5 + lift * 0.2}), 0 0 0 1px ${active ? 'rgba(242,193,74,0.55)' : 'rgba(255,255,255,0.1)'}`,
      }}>
      <Content app={app} w={p.width} h={p.height} />
    </WinFrame>
  );
};

/** The zones of the layout drawn over the screen (Shift + drag, or the moment they are put); `hot` lights one. */
export const ZoneOverlay: React.FC<{ show: number; hot?: number; zones?: Rect[] }> = ({ show, hot = -1, zones = ZONES }) => (
  <>
    {zones.map((z, i) => {
      const p = px(z, 8);
      return (
        <div key={i} style={{
          position: 'absolute', ...p, borderRadius: 12, opacity: show, boxSizing: 'border-box',
          border: `3px solid rgba(242,193,74,${i === hot ? 0.95 : 0.55})`, background: `rgba(242,193,74,${i === hot ? 0.24 : 0.08})`,
          display: 'flex', alignItems: 'center', justifyContent: 'center', fontFamily: SANS, fontSize: 64, fontWeight: 800, color: `rgba(242,193,74,${i === hot ? 0.95 : 0.6})`,
          boxShadow: i === hot ? '0 0 40px rgba(242,193,74,0.35)' : undefined,
        }}>{i + 1}</div>
      );
    })}
  </>
);

/** The stage screen: a monitor with the wallpaper, the taskbar and whatever is on it, at (x, y) centre and scale s. */
export const Desk: React.FC<{ x?: number; y?: number; s?: number; opacity?: number; children?: React.ReactNode; apps?: AppId[] }> = ({ x = 960, y = 520, s = 1, opacity = 1, children, apps = ['code', 'chrome', 'terminal'] }) => (
  <div style={{ position: 'absolute', left: x, top: y, width: DESK_W, height: DESK_H, transform: `translate(-50%, -50%) scale(${s})`, opacity }}>
    <div style={{ position: 'absolute', inset: -14, borderRadius: 22, background: '#16161a', boxShadow: '0 50px 120px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.08)' }} />
    <div style={{ position: 'absolute', inset: 0, borderRadius: 10, overflow: 'hidden' }}>
      <Wallpaper width={DESK_W} height={DESK_H} />
      {children}
      <Taskbar width={DESK_W} y={DESK_H - BAR} clock="9:02" apps={apps.map(a => ({ icon: APPS[a].icon, color: APPS[a].color, active: true }))} />
    </div>
  </div>
);
