import React from 'react';
import { C, SANS } from '../theme';
import { Fluent } from './Icon';

/** A K-Desktop card (the rounded panels of the main window). */
export const Card: React.FC<{ children?: React.ReactNode; style?: React.CSSProperties; pad?: number }> = ({ children, style, pad = 22 }) => (
  <div style={{ background: C.card, borderRadius: 14, border: `1px solid ${C.line}`, padding: pad, boxSizing: 'border-box', ...style }}>{children}</div>
);

export const Title: React.FC<{ children: React.ReactNode; size?: number; style?: React.CSSProperties }> = ({ children, size = 20, style }) => (
  <div style={{ fontFamily: SANS, fontSize: size, fontWeight: 600, color: C.text, ...style }}>{children}</div>
);

export const Caption: React.FC<{ children: React.ReactNode; size?: number; style?: React.CSSProperties }> = ({ children, size = 15, style }) => (
  <div style={{ fontFamily: SANS, fontSize: size, color: C.dim, lineHeight: 1.4, ...style }}>{children}</div>
);

/** The gold button («Nuevo comando», «Ejecutar»…). */
export const GoldButton: React.FC<{ children: React.ReactNode; icon?: string; press?: number; scale?: number; style?: React.CSSProperties }> = ({ children, icon, press = 0, scale = 1, style }) => (
  <div style={{
    display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10 * scale, height: 44 * scale, padding: `0 ${18 * scale}px`, borderRadius: 9 * scale,
    background: '#FFD970', color: '#2a1d02', fontFamily: SANS, fontWeight: 600, fontSize: 16 * scale,
    transform: `scale(${1 - press * 0.05})`, boxShadow: press > 0 ? `0 0 ${30 * press}px rgba(255,217,112,0.6)` : '0 2px 6px rgba(0,0,0,0.3)', ...style,
  }}>
    {icon && <Fluent name={icon} size={18 * scale} color="#2a1d02" />}
    {children}
  </div>
);

export const GhostButton: React.FC<{ icon: string; size?: number; style?: React.CSSProperties }> = ({ icon, size = 44, style }) => (
  <div style={{ width: size, height: size, borderRadius: 9, background: C.card2, border: `1px solid ${C.line}`, display: 'flex', alignItems: 'center', justifyContent: 'center', ...style }}>
    <Fluent name={icon} size={size * 0.45} color={C.text} />
  </div>
);

/** On/off switch (gold when on). */
export const Toggle: React.FC<{ on: number; scale?: number }> = ({ on, scale = 1 }) => (
  <div style={{ width: 44 * scale, height: 22 * scale, borderRadius: 11 * scale, position: 'relative', background: on > 0.5 ? '#F2C14A' : 'transparent', border: `${1.5 * scale}px solid ${on > 0.5 ? '#F2C14A' : C.dim}`, boxSizing: 'border-box' }}>
    <div style={{ position: 'absolute', top: 3 * scale, left: (3 + on * 21) * scale, width: 13 * scale, height: 13 * scale, borderRadius: '50%', background: on > 0.5 ? '#1a1306' : C.dim }} />
  </div>
);

export const Check: React.FC<{ on?: boolean; scale?: number }> = ({ on = true, scale = 1 }) => (
  <div style={{ width: 24 * scale, height: 24 * scale, borderRadius: 6 * scale, background: on ? '#F2C14A' : 'transparent', border: on ? 'none' : `1.5px solid ${C.dim}`, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
    {on && <Fluent name="checkmark_24_filled" size={16 * scale} color="#1a1306" />}
  </div>
);

/** A small rounded tag («+4», «×2», a hotkey under a command…). */
export const Tag: React.FC<{ children: React.ReactNode; gold?: boolean; size?: number; style?: React.CSSProperties }> = ({ children, gold, size = 13, style }) => (
  <span style={{
    fontFamily: SANS, fontSize: size, fontWeight: 600, padding: `${size * 0.2}px ${size * 0.55}px`, borderRadius: size * 0.5,
    background: gold ? C.accentSoft : C.chip, color: gold ? C.accentText : C.dim, ...style,
  }}>{children}</span>
);

/** A label that floats next to something, to name it on screen. */
export const Callout: React.FC<{ children: React.ReactNode; show: number; x: number; y: number; align?: 'left' | 'right' | 'center'; size?: number; gold?: boolean }> = ({ children, show, x, y, align = 'left', size = 26, gold = true }) => (
  <div style={{
    position: 'absolute', left: x, top: y, transform: `translate(${align === 'right' ? '-100%' : align === 'center' ? '-50%' : '0'}, -50%) translateY(${(1 - show) * 14}px) scale(${0.9 + show * 0.1})`,
    opacity: show, whiteSpace: 'nowrap', fontFamily: SANS, fontSize: size, fontWeight: 600, color: gold ? '#1a1306' : C.text,
    background: gold ? 'linear-gradient(180deg, #FFD970, #F2B807)' : 'rgba(30,30,36,0.92)', border: gold ? 'none' : `1px solid ${C.line2}`,
    padding: `${size * 0.35}px ${size * 0.7}px`, borderRadius: size * 0.5, boxShadow: '0 10px 30px rgba(0,0,0,0.45)',
  }}>{children}</div>
);
