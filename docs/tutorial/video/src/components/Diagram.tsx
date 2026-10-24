import React from 'react';
import { useCurrentFrame } from 'remotion';
import { ease, OUT } from '../lib/anim';
import { C, SANS } from '../theme';
import { KdIcon } from './Icon';

/** A rounded card with an icon, a title and an optional line under it. */
export const Node: React.FC<{
  x: number; y: number; icon: string; color: string; title: string; sub?: string; show?: number; lit?: number; width?: number; size?: number; align?: 'center' | 'left'; style?: React.CSSProperties;
}> = ({ x, y, icon, color, title, sub, show = 1, lit = 0, width, size = 1, align = 'center', style }) => (
  <div style={{
    position: 'absolute', left: x, top: y, transform: `translate(${align === 'center' ? '-50%' : '0'}, -50%) scale(${(0.6 + 0.4 * show) * size})`,
    opacity: Math.min(1, show * 1.6), display: 'flex', alignItems: 'center', gap: 16, width,
    padding: '14px 22px 14px 14px', borderRadius: 18, background: 'rgba(30,30,36,0.92)',
    border: `1.5px solid ${lit > 0 ? `rgba(242,193,74,${0.25 + 0.65 * lit})` : C.line2}`,
    boxShadow: `0 16px 40px rgba(0,0,0,0.45)${lit > 0 ? `, 0 0 ${40 * lit}px rgba(242,193,74,${0.35 * lit})` : ''}`, ...style,
  }}>
    <KdIcon icon={icon} color={color} size={54} glow={lit * 0.5} />
    <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      <div style={{ fontFamily: SANS, fontWeight: 700, fontSize: 25, color: C.text, whiteSpace: 'nowrap' }}>{title}</div>
      {sub && <div style={{ fontFamily: SANS, fontSize: 17, color: C.dim, whiteSpace: 'nowrap' }}>{sub}</div>}
    </div>
  </div>
);

/**
 * A wire between two points: drawn on as `draw` goes 0 → 1, then with light pulses travelling along it while `flow` > 0.
 * The curve leaves and arrives horizontally.
 */
export const Wire: React.FC<{ from: [number, number]; to: [number, number]; draw: number; flow?: number; color?: string; width?: number; seed?: number; bend?: number }> = ({ from, to, draw, flow = 0, color = C.accentText, width = 3, seed = 0, bend = 0.5 }) => {
  const frame = useCurrentFrame();
  if (draw <= 0) return null;
  const [x1, y1] = from;
  const [x2, y2] = to;
  const dx = (x2 - x1) * bend;
  const d = `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`;
  const len = Math.hypot(x2 - x1, y2 - y1) * 1.25;
  const pulses = flow > 0 ? [0, 0.5].map(o => ((frame / 45 + o + seed * 0.37) % 1)) : [];
  // Point on the cubic for a pulse.
  const at = (t: number) => {
    const u = 1 - t;
    const px = u * u * u * x1 + 3 * u * u * t * (x1 + dx) + 3 * u * t * t * (x2 - dx) + t * t * t * x2;
    const py = u * u * u * y1 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y2;
    return [px, py];
  };
  return (
    <svg style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible', pointerEvents: 'none' }} width={1} height={1}>
      <path d={d} stroke="rgba(255,255,255,0.10)" strokeWidth={width + 4} fill="none" strokeDasharray={`${len * draw} ${len}`} strokeLinecap="round" />
      <path d={d} stroke={color} strokeOpacity={0.55} strokeWidth={width} fill="none" strokeDasharray={`${len * draw} ${len}`} strokeLinecap="round" />
      {pulses.map((t, i) => {
        const [px, py] = at(t);
        return <circle key={i} cx={px} cy={py} r={width * 2.2} fill={color} opacity={flow * Math.sin(Math.PI * t)} style={{ filter: `drop-shadow(0 0 8px ${color})` }} />;
      })}
    </svg>
  );
};

/** A label in big type that writes itself on, word by word. */
export const Kinetic: React.FC<{ text: string; at: number; x: number; y: number; size?: number; color?: string; align?: 'left' | 'center'; weight?: number; stagger?: number; out?: number }> = ({ text, at, x, y, size = 64, color = C.text, align = 'center', weight = 800, stagger = 3, out }) => {
  const frame = useCurrentFrame();
  const words = text.split(' ');
  const gone = out !== undefined ? ease(frame, out, out + 12) : 0;
  return (
    <div style={{
      position: 'absolute', left: x, top: y, transform: `translate(${align === 'center' ? '-50%' : '0'}, -50%)`, display: 'flex', gap: size * 0.26,
      fontFamily: SANS, fontWeight: weight, fontSize: size, letterSpacing: -size * 0.02, color, whiteSpace: 'nowrap', opacity: 1 - gone,
    }}>
      {words.map((w, i) => {
        const k = ease(frame, at + i * stagger, at + i * stagger + 14, OUT);
        return (
          <span key={i} style={{ display: 'inline-block', overflow: 'hidden', paddingBottom: size * 0.12 }}>
            <span style={{ display: 'inline-block', transform: `translateY(${(1 - k) * 110}%)`, opacity: k }}>{w}</span>
          </span>
        );
      })}
    </div>
  );
};
