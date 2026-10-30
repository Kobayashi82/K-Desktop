import React from 'react';
import { C, SANS } from '../theme';
import { KdIcon } from './Icon';
import { t } from '../lib/i18n';

export type ActionRow = { icon: string; color: string; name: string; detail: string };

/** A command as a card: its icon and name, and its actions as numbered rows that light up as they run. */
export const CommandCard: React.FC<{
  name: string; icon?: string; color?: string; rows: ActionRow[]; shown: number[]; active?: number; done?: number; width?: number; nameShow?: number; glow?: number; hotkey?: string;
}> = ({ name, icon = 'flash_24_filled', color = 'gold', rows, shown, active = -1, done = -1, width = 470, nameShow = 1, glow = 0, hotkey }) => (
  <div style={{
    width, padding: 22, borderRadius: 24, background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))',
    border: `1.5px solid rgba(242,193,74,${0.25 + 0.5 * glow})`, boxSizing: 'border-box',
    boxShadow: `0 30px 70px rgba(0,0,0,0.55), 0 0 ${60 * glow}px rgba(242,193,74,${0.3 * glow})`,
  }}>
    <div style={{ display: 'flex', alignItems: 'center', gap: 18 }}>
      <KdIcon icon={icon} color={color} size={72} glow={glow * 0.6} />
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4, opacity: nameShow }}>
        <div style={{ fontFamily: SANS, fontSize: 15, fontWeight: 700, letterSpacing: 2, color: C.accentText, textTransform: 'uppercase' }}>{t('Command')}</div>
        <div style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text, letterSpacing: -0.5 }}>{name}</div>
      </div>
      {hotkey && <div style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 16, fontWeight: 600, color: C.accentText, background: C.accentSoft, padding: '6px 12px', borderRadius: 9 }}>{hotkey}</div>}
    </div>
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginTop: rows.length ? 20 : 0 }}>
      {rows.map((r, i) => {
        const k = shown[i] ?? 0;
        if (k <= 0) return null;
        const isActive = i === active;
        const isDone = i <= done;
        return (
          <div key={i} style={{
            display: 'flex', alignItems: 'center', gap: 14, padding: '10px 14px', borderRadius: 14,
            background: isActive ? 'rgba(242,193,74,0.16)' : 'rgba(255,255,255,0.05)',
            border: `1px solid ${isActive ? 'rgba(242,193,74,0.7)' : 'rgba(255,255,255,0.06)'}`,
            opacity: Math.min(1, k * 1.5), transform: `translateX(${(1 - k) * -40}px) scale(${isActive ? 1.03 : 1})`,
            height: 52 * Math.min(1, k * 1.2), overflow: 'hidden',
          }}>
            <div style={{
              width: 28, height: 28, borderRadius: 14, flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'center',
              fontFamily: SANS, fontSize: 15, fontWeight: 700, background: isDone || isActive ? C.accentText : 'rgba(255,255,255,0.1)', color: isDone || isActive ? '#1a1306' : C.dim,
            }}>{i + 1}</div>
            <KdIcon icon={r.icon} color={r.color} size={36} />
            <div style={{ fontFamily: SANS, fontSize: 20, fontWeight: 600, color: C.text, whiteSpace: 'nowrap' }}>{r.name}</div>
            <div style={{ fontFamily: SANS, fontSize: 18, color: C.dim, marginLeft: 'auto', whiteSpace: 'nowrap' }}>{r.detail}</div>
          </div>
        );
      })}
    </div>
  </div>
);

export const WORK_MODE: ActionRow[] = [
  { icon: 'open_24_filled', color: 'blue', name: t('Run application'), detail: t('notepad.exe') },
  { icon: 'timer_24_filled', color: 'purple', name: t('Wait'), detail: '1 s' },
  { icon: 'window_new_24_filled', color: 'sky', name: t('Window'), detail: t('Left half') },
  { icon: 'speaker_2_24_filled', color: 'teal', name: t('Volume'), detail: '20 %' },
  { icon: 'folder_open_24_filled', color: 'yellow', name: t('Open folder'), detail: t('Documents') },
];
