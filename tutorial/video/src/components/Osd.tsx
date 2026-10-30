import React from 'react';
import { C, SANS } from '../theme';
import { KdIcon } from './Icon';

/** K-Desktop's on-screen message (OsdWindow): dark rounded panel, icon, text and an optional bar. */
export const Osd: React.FC<{ icon: string; color: string; text: string; sub?: string; bar?: number; show?: number; scale?: number; style?: React.CSSProperties }> = ({ icon, color, text, sub, bar, show = 1, scale = 1.4, style }) => (
  <div style={{
    display: 'flex', alignItems: 'center', gap: 14 * scale, padding: `${14 * scale}px ${20 * scale}px`, borderRadius: 14 * scale, minWidth: 260 * scale,
    background: 'rgba(32,32,34,0.92)', border: '1px solid rgba(255,255,255,0.15)', boxShadow: `0 ${14 * scale}px ${40 * scale}px rgba(0,0,0,0.5)`,
    opacity: show, transform: `translateY(${(1 - show) * 30}px) scale(${0.9 + 0.1 * show})`, boxSizing: 'border-box', ...style,
  }}>
    <KdIcon icon={icon} color={color} size={36 * scale} />
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 6 * scale }}>
      <div style={{ fontFamily: SANS, fontSize: 15 * scale, fontWeight: 600, color: 'white', whiteSpace: 'nowrap' }}>{text}</div>
      {sub && <div style={{ fontFamily: SANS, fontSize: 13 * scale, color: 'rgba(255,255,255,0.7)', whiteSpace: 'nowrap' }}>{sub}</div>}
      {bar !== undefined && (
        <div style={{ height: 4 * scale, borderRadius: 2 * scale, background: 'rgba(255,255,255,0.12)' }}>
          <div style={{ width: `${bar}%`, height: '100%', borderRadius: 2 * scale, background: C.accent }} />
        </div>
      )}
    </div>
  </div>
);
