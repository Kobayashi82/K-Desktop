import React from 'react';
import { C, SANS } from '../theme';
import { Fluent, KdIcon } from './Icon';
import { WinFrame } from './KdWindow';
import { t } from '../lib/i18n';

/** A dark Windows 11 style wallpaper: soft folded light. */
export const Wallpaper: React.FC<{ width?: number; height?: number; hue?: number }> = ({ width = 1920, height = 1080, hue = 0 }) => (
  <div style={{
    position: 'absolute', left: 0, top: 0, width, height, overflow: 'hidden',
    background: `radial-gradient(ellipse 60% 70% at 62% 58%, hsla(${215 + hue},70%,38%,0.85), transparent 70%),
      radial-gradient(ellipse 45% 55% at 30% 70%, hsla(${265 + hue},60%,32%,0.75), transparent 70%),
      radial-gradient(ellipse 50% 40% at 70% 20%, hsla(${195 + hue},70%,30%,0.6), transparent 70%), #0c1020`,
  }}>
    <svg width={width} height={height} viewBox="0 0 1920 1080" preserveAspectRatio="none" style={{ position: 'absolute', inset: 0, opacity: 0.55 }}>
      <defs>
        <linearGradient id="petal" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#7fb2ff" stopOpacity="0.55" />
          <stop offset="1" stopColor="#3b4cc0" stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d="M1180 640 C 1000 420, 760 380, 600 520 C 820 470, 1000 520, 1180 640 Z" fill="url(#petal)" />
      <path d="M1180 640 C 1240 400, 1180 220, 1000 140 C 1100 300, 1130 460, 1180 640 Z" fill="url(#petal)" />
      <path d="M1180 640 C 1400 520, 1560 560, 1660 700 C 1500 620, 1360 610, 1180 640 Z" fill="url(#petal)" />
      <path d="M1180 640 C 1300 820, 1260 960, 1120 1060 C 1180 900, 1190 780, 1180 640 Z" fill="url(#petal)" />
    </svg>
  </div>
);

export type TaskbarApp = { icon: string; color: string; active?: boolean };

export const Taskbar: React.FC<{ apps: TaskbarApp[]; width?: number; y?: number; clock?: string; extra?: React.ReactNode }> = ({ apps, width = 1920, y = 1080 - 56, clock = '9:41', extra }) => (
  <div style={{
    position: 'absolute', left: 0, top: y, width, height: 56, background: 'rgba(28,30,38,0.86)', borderTop: '1px solid rgba(255,255,255,0.08)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10,
  }}>
    <div style={{ width: 40, height: 40, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <svg width="24" height="24" viewBox="0 0 24 24"><g fill="#4cc2ff"><rect x="1" y="1" width="10.5" height="10.5" rx="1.5" /><rect x="12.5" y="1" width="10.5" height="10.5" rx="1.5" /><rect x="1" y="12.5" width="10.5" height="10.5" rx="1.5" /><rect x="12.5" y="12.5" width="10.5" height="10.5" rx="1.5" /></g></svg>
    </div>
    {apps.map((a, i) => (
      <div key={i} style={{ position: 'relative', width: 44, height: 44, borderRadius: 8, background: a.active ? 'rgba(255,255,255,0.08)' : 'transparent', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <KdIcon icon={a.icon} color={a.color} size={30} />
        {a.active && <div style={{ position: 'absolute', bottom: 1, width: 16, height: 3, borderRadius: 2, background: '#4cc2ff' }} />}
      </div>
    ))}
    <div style={{ position: 'absolute', right: 18, display: 'flex', alignItems: 'center', gap: 16, color: C.text, fontFamily: SANS, fontSize: 14 }}>
      {extra}
      <Fluent name="wifi_1_24_regular" size={18} color={C.text} />
      <Fluent name="speaker_2_24_regular" size={18} color={C.text} />
      <div style={{ textAlign: 'right', lineHeight: 1.25, fontSize: 13 }}>{clock}<br />24/10/2026</div>
    </div>
  </div>
);

/** Skeleton lines (text that is there but needn't be read). */
export const Lines: React.FC<{ count: number; width?: number; color?: string; gap?: number; height?: number; seed?: number }> = ({ count, width = 600, color = 'rgba(255,255,255,0.14)', gap = 16, height = 10, seed = 1 }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap }}>
    {Array.from({ length: count }, (_, i) => (
      <div key={i} style={{ height, borderRadius: height / 2, background: color, width: width * (0.55 + 0.45 * Math.abs(Math.sin(i * 2.3 + seed))) }} />
    ))}
  </div>
);

export type AppKind = 'notepad' | 'explorer' | 'browser' | 'player' | 'steam' | 'chrome';

const APPS: Record<AppKind, { title: string; icon: string; color: string }> = {
  notepad: { title: t('Untitled - Notepad'), icon: 'document_text_24_filled', color: 'sky' },
  explorer: { title: t('Documents'), icon: 'folder_24_filled', color: 'yellow' },
  browser: { title: t('New tab - Browser'), icon: 'globe_24_filled', color: 'blue' },
  chrome: { title: t('Google Chrome'), icon: 'globe_24_filled', color: 'green' },
  player: { title: t('Music'), icon: 'music_note_2_24_filled', color: 'pink' },
  steam: { title: t('Steam'), icon: 'xbox_controller_24_filled', color: 'indigo' },
};

/** A generic application window with recognisable but quiet content. */
export const AppWindow: React.FC<{ kind: AppKind; width: number; height: number; style?: React.CSSProperties; focus?: boolean }> = ({ kind, width, height, style, focus = true }) => {
  const app = APPS[kind];
  const content = (() => {
    switch (kind) {
      case 'notepad':
        return <div style={{ padding: '18px 22px' }}><Lines count={Math.floor((height - 80) / 30)} width={width - 120} color="rgba(255,255,255,0.16)" gap={18} height={9} /></div>;
      case 'explorer':
        return (
          <div style={{ display: 'flex', height: '100%' }}>
            <div style={{ width: 170, padding: 16, borderRight: `1px solid ${C.line}` }}><Lines count={7} width={120} gap={20} height={9} /></div>
            <div style={{ flex: 1, padding: 20, display: 'flex', flexWrap: 'wrap', gap: 22, alignContent: 'flex-start' }}>
              {Array.from({ length: Math.max(4, Math.floor((width - 220) / 110) * 2) }, (_, i) => (
                <div key={i} style={{ width: 84, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
                  <Fluent name={i % 3 === 2 ? 'document_24_filled' : 'folder_24_filled'} size={54} color={i % 3 === 2 ? '#9fb3c8' : '#F2C14A'} />
                  <div style={{ width: 60, height: 7, borderRadius: 4, background: 'rgba(255,255,255,0.18)' }} />
                </div>
              ))}
            </div>
          </div>
        );
      case 'player':
        return (
          <div style={{ padding: 26, display: 'flex', gap: 24 }}>
            <div style={{ width: 150, height: 150, borderRadius: 12, background: 'linear-gradient(135deg,#ff8cc6,#7b3fe4)' }} />
            <div style={{ flex: 1, paddingTop: 18 }}><Lines count={3} width={width - 260} gap={18} height={12} /></div>
          </div>
        );
      default:
        return (
          <div>
            <div style={{ height: 40, margin: '6px 14px', borderRadius: 20, background: 'rgba(255,255,255,0.07)' }} />
            <div style={{ padding: '18px 26px', display: 'flex', flexDirection: 'column', gap: 18 }}>
              <div style={{ height: Math.min(180, height * 0.3), borderRadius: 12, background: 'linear-gradient(120deg, rgba(90,169,255,0.35), rgba(123,63,228,0.3))' }} />
              <Lines count={Math.max(2, Math.floor((height - 340) / 30))} width={width - 140} gap={16} height={9} />
            </div>
          </div>
        );
    }
  })();
  return (
    <WinFrame width={width} height={height} title={app.title} icon={<KdIcon icon={app.icon} color={app.color} size={20} />}
      bg={focus ? '#202228' : '#1b1c21'} style={style}>
      {content}
    </WinFrame>
  );
};

/** The Windows volume flyout. */
export const VolumeFlyout: React.FC<{ level: number; style?: React.CSSProperties }> = ({ level, style }) => (
  <div style={{ position: 'absolute', width: 360, padding: '20px 22px', borderRadius: 12, background: 'rgba(36,38,46,0.96)', boxShadow: '0 20px 50px rgba(0,0,0,0.5)', border: `1px solid ${C.line}`, display: 'flex', alignItems: 'center', gap: 16, ...style }}>
    <Fluent name="speaker_2_24_regular" size={24} color={C.text} />
    <div style={{ flex: 1, height: 4, borderRadius: 2, background: 'rgba(255,255,255,0.2)', position: 'relative' }}>
      <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: `${level}%`, borderRadius: 2, background: '#4cc2ff' }} />
      <div style={{ position: 'absolute', left: `${level}%`, top: -8, width: 20, height: 20, marginLeft: -10, borderRadius: '50%', background: '#4cc2ff', border: '4px solid #2a2c34' }} />
    </div>
    <div style={{ fontFamily: SANS, color: C.text, fontSize: 16, width: 32, textAlign: 'right' }}>{Math.round(level)}</div>
  </div>
);

/** A plain context menu (Windows), to show menus the slow way. */
export const ContextMenu: React.FC<{ items: string[]; hot?: number; style?: React.CSSProperties }> = ({ items, hot = -1, style }) => (
  <div style={{ position: 'absolute', width: 250, padding: 6, borderRadius: 10, background: 'rgba(40,42,50,0.97)', border: `1px solid ${C.line}`, boxShadow: '0 16px 40px rgba(0,0,0,0.5)', ...style }}>
    {items.map((it, i) => (
      <div key={i} style={{ height: 34, borderRadius: 6, padding: '0 14px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontFamily: SANS, fontSize: 14, color: C.text, background: i === hot ? 'rgba(255,255,255,0.09)' : 'transparent' }}>
        {it}
        {it.endsWith('›') ? null : null}
      </div>
    ))}
  </div>
);
