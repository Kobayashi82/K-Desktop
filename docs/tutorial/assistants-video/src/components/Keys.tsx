import React from 'react';
import { C, MONO, SANS } from '../theme';
import { Fluent } from './Icon';

/** A keyboard key; `down` (0–1) pushes it in and lights it gold. */
export const Keycap: React.FC<{ label: React.ReactNode; down?: number; width?: number; height?: number; size?: number; lit?: number; dim?: boolean; style?: React.CSSProperties }> = ({ label, down = 0, width, height = 78, size = 26, lit = 0, dim, style }) => {
  const glow = Math.max(down, lit);
  return (
    <div style={{
      position: 'relative', height, minWidth: width ?? height, padding: '0 22px', boxSizing: 'border-box',
      borderRadius: height * 0.2, fontFamily: SANS, fontWeight: 600, fontSize: size, color: glow > 0.5 ? '#1a1306' : C.text,
      display: 'flex', alignItems: 'center', justifyContent: 'center', letterSpacing: 0.2,
      background: glow > 0
        ? `linear-gradient(180deg, rgba(255,217,112,${glow}) 0%, rgba(224,168,21,${glow}) 100%), linear-gradient(180deg, #3a3a42, #2a2a30)`
        : 'linear-gradient(180deg, #3a3a42, #2a2a30)',
      boxShadow: `0 ${8 * (1 - down) + 1}px 0 ${glow > 0.3 ? '#8a6408' : '#17171b'}, 0 ${14 * (1 - down) + 4}px ${24 * (1 - down) + 6}px rgba(0,0,0,0.45), inset 0 1px 0 rgba(255,255,255,${glow > 0.3 ? 0.5 : 0.12})` + (glow > 0 ? `, 0 0 ${40 * glow}px rgba(242,193,74,${0.45 * glow})` : ''),
      transform: `translateY(${7 * down}px)`, opacity: dim ? 0.45 : 1, ...style,
    }}>
      {label}
    </div>
  );
};

/** A hotkey chip like the editor's («Ctrl» «Alt» «W» ×). */
export const HotkeyChip: React.FC<{ keys: string[]; scale?: number; fresh?: number }> = ({ keys, scale = 1, fresh = 0 }) => (
  <div style={{
    display: 'flex', alignItems: 'center', gap: 6 * scale, padding: `${7 * scale}px ${10 * scale}px`, borderRadius: 10 * scale,
    background: C.card2, border: `1px solid ${fresh > 0 ? `rgba(242,193,74,${0.7 * fresh})` : C.line}`,
    boxShadow: fresh > 0 ? `0 0 ${24 * fresh}px rgba(242,193,74,${0.35 * fresh})` : 'none',
  }}>
    {keys.map((k, i) => (
      <span key={i} style={{ fontFamily: SANS, fontSize: 15 * scale, fontWeight: 600, color: C.text, background: '#3a3a40', padding: `${5 * scale}px ${10 * scale}px`, borderRadius: 7 * scale }}>{k}</span>
    ))}
    <span style={{ marginLeft: 4 * scale, opacity: 0.55 }}><Fluent name="dismiss_24_regular" size={16 * scale} color={C.text} /></span>
  </div>
);

/** Monospace text, for code, addresses and terminal lines. */
export const Mono: React.FC<{ children: React.ReactNode; size?: number; color?: string; style?: React.CSSProperties }> = ({ children, size = 22, color = C.text, style }) => (
  <span style={{ fontFamily: MONO, fontSize: size, color, ...style }}>{children}</span>
);
