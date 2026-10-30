import React from 'react';
import { t } from '../lib/i18n';
import { C, SANS } from '../theme';
import { KdIcon } from './Icon';

export type QmItem =
  | { kind: 'item'; label: string; icon: string; color: string; sub?: boolean }
  | { kind: 'title'; label: string }
  | { kind: 'sep' };

export const qi = (label: string, icon: string, color: string, sub = false): QmItem => ({ kind: 'item', label, icon, color, sub });
export const qt = (label: string): QmItem => ({ kind: 'title', label });
export const qs: QmItem = { kind: 'sep' };

/**
 * A QuickMenu panel as K-Desktop draws it (QuickMenuWindow.xaml): acrylic tint, rows with icon, label and chevron,
 * gold titles on a faint band, separators, the hover highlight with its accent pill, and the «ran» flash.
 * `appear` (0–1) unfolds the rows one after another.
 */
export const QuickMenuPanel: React.FC<{ items: QmItem[]; hot?: number; flash?: number; appear?: number; scale?: number; width?: number; style?: React.CSSProperties }> = ({ items, hot = -1, flash = 0, appear = 1, scale = 1.6, width = 250, style }) => {
  const row = 32 * scale;
  return (
    <div style={{
      width: width * scale, padding: 6 * scale, borderRadius: 10 * scale, boxSizing: 'border-box',
      background: 'rgba(28,28,30,0.78)', border: `${scale}px solid rgba(255,255,255,0.12)`,
      boxShadow: `0 ${18 * scale}px ${44 * scale}px rgba(0,0,0,0.55)`, backdropFilter: 'blur(18px) saturate(1.4)',
      fontFamily: SANS, fontSize: 13 * scale, color: C.text, ...style,
    }}>
      {items.map((it, i) => {
        const k = Math.min(1, Math.max(0, appear * (items.length + 3) - i));
        const common: React.CSSProperties = { opacity: k, transform: `translateY(${(1 - k) * -6 * scale}px)` };
        if (it.kind === 'sep') return <div key={i} style={{ height: 1, margin: `${4 * scale}px ${12 * scale}px`, background: 'rgba(255,255,255,0.1)', ...common }} />;
        if (it.kind === 'title')
          return (
            <div key={i} style={{ height: row, margin: `${scale}px 0`, borderRadius: 5 * scale, background: 'rgba(255,255,255,0.06)', display: 'flex', alignItems: 'center', padding: `0 ${10 * scale}px`, fontWeight: 600, color: C.accentText, ...common }}>
              {t(it.label)}
            </div>
          );
        const isHot = i === hot;
        return (
          <div key={i} style={{ position: 'relative', height: row, margin: `${scale}px 0`, borderRadius: 5 * scale, display: 'flex', alignItems: 'center', padding: `0 ${8 * scale}px 0 ${10 * scale}px`, gap: 10 * scale, ...common }}>
            {isHot && <div style={{ position: 'absolute', inset: 0, borderRadius: 5 * scale, background: 'rgba(255,255,255,0.09)' }} />}
            {isHot && flash > 0 && <div style={{ position: 'absolute', inset: 0, borderRadius: 5 * scale, background: `rgba(224,168,21,${0.35 * flash})` }} />}
            {isHot && <div style={{ position: 'absolute', left: scale, top: (row - 16 * scale) / 2, width: 3 * scale, height: 16 * scale, borderRadius: 2 * scale, background: C.accent }} />}
            <KdIcon icon={it.icon} color={it.color} size={20 * scale} />
            <span style={{ flex: 1, position: 'relative', whiteSpace: 'nowrap' }}>{t(it.label)}</span>
            {it.sub && (
              <svg width={6 * scale} height={10 * scale} viewBox="0 0 6 10" style={{ position: 'relative' }}>
                <path d="M1 1 L5 5 L1 9" stroke="rgba(255,255,255,0.55)" strokeWidth="1.4" fill="none" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            )}
          </div>
        );
      })}
    </div>
  );
};

/** The QuickMenu used across the video («Main»). */
export const MAIN_MENU: QmItem[] = [
  qt('Apps'),
  qi('Browser', 'globe_24_filled', 'blue'),
  qi('Music', 'music_note_2_24_filled', 'pink'),
  qi('Notepad', 'document_text_24_filled', 'sky'),
  qi('Games', 'xbox_controller_24_filled', 'indigo', true),
  qs,
  qt('System'),
  qi('Volume', 'speaker_2_24_filled', 'teal', true),
  qi('Screenshot', 'screenshot_24_filled', 'green'),
  qi('Downloads', 'arrow_download_24_filled', 'teal'),
  qi('Empty Recycle Bin', 'broom_24_filled', 'orange'),
  qs,
  qi('Power', 'power_24_filled', 'red', true),
];

export const VOLUME_MENU: QmItem[] = [
  qi('Volume up', 'speaker_2_24_filled', 'teal'),
  qi('Volume down', 'speaker_1_24_filled', 'teal'),
  qi('Mute', 'speaker_mute_24_filled', 'gray'),
  qs,
  qi('Headphones', 'headphones_sound_wave_24_filled', 'purple'),
  qi('Speakers', 'speaker_2_24_filled', 'blue'),
];
