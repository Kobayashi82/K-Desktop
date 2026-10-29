import React from 'react';
import { Img, staticFile, useCurrentFrame } from 'remotion';
import { clamp01, ease, IN, OUT, pop } from '../lib/anim';
import { C, MONO, SANS } from '../theme';
import { Fluent, KdIcon } from './Icon';

/** How far something is shown: popped in at `at`, gone from `out` (if given). */
export const shown = (f: number, at: number, out?: number, len = 12) =>
  Math.min(pop(f, at, { damping: 16, stiffness: 150 }), out === undefined ? 1 : 1 - ease(f, out, out + len, IN));

/** A big heading at the top of a part, with a small line over it. */
export const Heading: React.FC<{ kicker?: string; text: string; at: number; y?: number; size?: number; color?: string }> = ({ kicker, text, at, y = 150, size = 62, color = C.text }) => {
  const f = useCurrentFrame();
  const k = ease(f, at, at + 18, OUT);
  return (
    <div style={{ position: 'absolute', left: 0, right: 0, top: y, textAlign: 'center', opacity: k, transform: `translateY(${(1 - k) * 24}px)` }}>
      {kicker && <div style={{ fontFamily: SANS, fontSize: 22, fontWeight: 700, letterSpacing: 4, color: C.accentText, textTransform: 'uppercase', marginBottom: 10 }}>{kicker}</div>}
      <div style={{ fontFamily: SANS, fontSize: size, fontWeight: 800, letterSpacing: -size * 0.02, color }}>{text}</div>
    </div>
  );
};

/** A rounded panel with an optional icon and title, absolutely placed (x, y: top left). */
export const Panel: React.FC<{
  x: number; y: number; w: number; h?: number; show?: number; lit?: number; icon?: string; color?: string; title?: React.ReactNode; sub?: React.ReactNode;
  children?: React.ReactNode; pad?: number; style?: React.CSSProperties; dim?: number;
}> = ({ x, y, w, h, show = 1, lit = 0, icon, color = 'blue', title, sub, children, pad = 26, style, dim = 0 }) => (
  <div style={{
    position: 'absolute', left: x, top: y, width: w, height: h, boxSizing: 'border-box', padding: pad, borderRadius: 26,
    background: 'rgba(28,28,34,0.94)', border: `2px solid ${lit > 0.02 ? `rgba(242,193,74,${0.25 + 0.65 * lit})` : C.line2}`,
    boxShadow: `0 26px 60px rgba(0,0,0,0.45)${lit > 0.02 ? `, 0 0 ${60 * lit}px rgba(242,193,74,${0.28 * lit})` : ''}`,
    opacity: clamp01(show * 1.5) * (1 - 0.6 * dim), transform: `translateY(${(1 - show) * 40}px) scale(${0.92 + 0.08 * show})`, ...style,
  }}>
    {(icon || title) && (
      <div style={{ display: 'flex', alignItems: 'center', gap: 18, marginBottom: children ? 20 : 0 }}>
        {icon && <KdIcon icon={icon} color={color} size={58} glow={lit * 0.4} />}
        <div>
          {title && <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text, lineHeight: 1.15 }}>{title}</div>}
          {sub && <div style={{ fontFamily: SANS, fontSize: 20, color: C.dim, marginTop: 3 }}>{sub}</div>}
        </div>
      </div>
    )}
    {children}
  </div>
);

/** A small rounded label. */
export const Pill: React.FC<{ children: React.ReactNode; color?: string; bg?: string; size?: number; mono?: boolean; style?: React.CSSProperties }> = ({ children, color = C.text, bg = 'rgba(255,255,255,0.08)', size = 20, mono, style }) => (
  <span style={{
    display: 'inline-flex', alignItems: 'center', gap: size * 0.4, fontFamily: mono ? MONO : SANS, fontSize: size, fontWeight: mono ? 500 : 700,
    color, background: bg, padding: `${size * 0.3}px ${size * 0.65}px`, borderRadius: size * 0.6, whiteSpace: 'nowrap', ...style,
  }}>{children}</span>
);

/** A line of a list that slides in at `at`; `lit` brightens it. */
export const Item: React.FC<{ at: number; icon?: string; color?: string; children: React.ReactNode; size?: number; lit?: number; dim?: boolean; style?: React.CSSProperties }> = ({ at, icon = 'checkmark_circle_24_filled', color = C.accentText, children, size = 30, lit = 0, dim, style }) => {
  const f = useCurrentFrame();
  const k = ease(f, at - 4, at + 12, OUT);
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: size * 0.5, opacity: k * (dim ? 0.45 : 1), transform: `translateX(${(1 - k) * -30}px)`, marginBottom: size * 0.55, ...style }}>
      <Fluent name={icon} size={size * 1.1} color={color} style={{ filter: lit > 0 ? `drop-shadow(0 0 ${10 * lit}px ${color})` : undefined }} />
      <div style={{ fontFamily: SANS, fontSize: size, fontWeight: 600, color: C.text, lineHeight: 1.25 }}>{children}</div>
    </div>
  );
};

/** A score bar with the bar it must reach. */
export const Meter: React.FC<{ label: React.ReactNode; value: number; need?: number; width?: number; size?: number; color?: string; show?: number }> = ({ label, value, need, width = 520, size = 24, color = C.teal, show = 1 }) => {
  const pass = need !== undefined && value >= need;
  return (
    <div style={{ width, marginBottom: size * 1.1, opacity: show }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', fontFamily: SANS, fontSize: size, color: C.text, marginBottom: size * 0.4 }}>
        <span>{label}</span><span style={{ fontFamily: MONO, color: pass ? C.success : C.dim }}>{value.toFixed(2)}</span>
      </div>
      <div style={{ position: 'relative', height: size * 0.62, borderRadius: size, background: 'rgba(255,255,255,0.08)' }}>
        <div style={{ width: `${clamp01(value) * 100}%`, height: '100%', borderRadius: size, background: pass ? C.success : color, boxShadow: pass ? `0 0 16px ${C.success}` : 'none' }} />
        {need !== undefined && <div style={{ position: 'absolute', left: `${need * 100}%`, top: -7, bottom: -7, width: 3, borderRadius: 2, background: C.accentText }} />}
      </div>
    </div>
  );
};

/** A speech bubble: the user's (gold, pointing right) or the assistant's (white). */
export const Bubble: React.FC<{ text: React.ReactNode; show: number; x: number; y: number; user?: boolean; size?: number; width?: number }> = ({ text, show, x, y, user, size = 32, width }) => (
  <div style={{
    position: 'absolute', left: x, top: y, width, transform: `translate(${user ? '-100%' : '0'}, -50%) scale(${0.8 + 0.2 * show})`, transformOrigin: user ? 'right center' : 'left center', opacity: clamp01(show),
    padding: `${size * 0.5}px ${size * 0.8}px`, borderRadius: size * 0.9, fontFamily: SANS, fontSize: size, fontWeight: 700, lineHeight: 1.3,
    background: user ? C.accentText : 'white', color: user ? '#1a1306' : '#141418', boxShadow: '0 20px 50px rgba(0,0,0,0.5)', whiteSpace: width ? 'normal' : 'nowrap',
  }}>{text}</div>
);

export type FigureState = 'idle' | 'listen' | 'think' | 'speak' | 'sleep';

/** The Lander, floating, showing what it is doing: a pulsing ring (listening), dots (thinking), waves (speaking). */
export const Lander: React.FC<{ x: number; y: number; size?: number; state?: FigureState; show?: number; tilt?: number }> = ({ x, y, size = 220, state = 'idle', show = 1, tilt = 0 }) => {
  const f = useCurrentFrame();
  const bob = Math.sin(f / 14) * size * 0.035;
  return (
    <div style={{ position: 'absolute', left: x - size / 2, top: y - size / 2 + bob, width: size, height: size, opacity: clamp01(show * 1.4), transform: `scale(${0.7 + 0.3 * show}) rotate(${tilt + Math.sin(f / 23) * 2}deg)` }}>
      {state === 'listen' && [0, 1].map(i => {
        const k = ((f / 40 + i * 0.5) % 1);
        return <div key={i} style={{ position: 'absolute', left: '50%', top: '45%', width: size * (0.7 + k * 0.9), height: size * (0.7 + k * 0.9), transform: 'translate(-50%,-50%)', borderRadius: '50%', border: `${size * 0.02}px solid ${C.teal}`, opacity: 1 - k }} />;
      })}
      <Img src={staticFile('brand/lander/classic.png')} style={{ width: size, height: size, position: 'relative', filter: state === 'sleep' ? 'grayscale(0.6) brightness(0.7)' : undefined }} />
      {state === 'think' && (
        <div style={{ position: 'absolute', left: '72%', top: '-6%', display: 'flex', gap: size * 0.04 }}>
          {[0, 1, 2].map(i => <div key={i} style={{ width: size * 0.07, height: size * 0.07, borderRadius: '50%', background: C.accentText, opacity: 0.35 + 0.65 * Math.max(0, Math.sin(f / 5 - i * 0.9)) }} />)}
        </div>
      )}
      {state === 'speak' && [0, 1, 2].map(i => {
        const k = ((f / 18 + i / 3) % 1);
        return <div key={i} style={{ position: 'absolute', left: `${88 + k * 18}%`, top: '30%', width: size * 0.18, height: size * 0.36, borderRadius: '50%', borderRight: `${size * 0.025}px solid ${C.accentText}`, opacity: 1 - k }} />;
      })}
    </div>
  );
};

/** A message travelling from one point to another along an arc, between frames `start` and `end`. */
export const Packet: React.FC<{ from: [number, number]; to: [number, number]; start: number; end: number; label: React.ReactNode; color?: string; arc?: number; size?: number }> = ({ from, to, start, end, label, color = C.accentText, arc = -80, size = 22 }) => {
  const f = useCurrentFrame();
  if (f < start - 6 || f > end + 14) return null;
  const t = ease(f, start, end);
  const x = from[0] + (to[0] - from[0]) * t;
  const y = from[1] + (to[1] - from[1]) * t + arc * Math.sin(Math.PI * t);
  const fade = Math.min(ease(f, start - 6, start + 4), 1 - ease(f, end + 2, end + 14));
  return (
    <div style={{
      position: 'absolute', left: x, top: y, transform: `translate(-50%,-50%) scale(${0.8 + 0.2 * fade})`, opacity: fade, whiteSpace: 'nowrap',
      fontFamily: MONO, fontSize: size, fontWeight: 600, color: '#15120a', background: color, padding: `${size * 0.35}px ${size * 0.7}px`, borderRadius: size * 0.6,
      boxShadow: `0 0 ${size * 1.4}px ${color}, 0 10px 30px rgba(0,0,0,0.5)`,
    }}>{label}</div>
  );
};

/** Colours a line of JSON-ish code: keys, strings, numbers and the rest. */
const paint = (line: string) => {
  const parts: React.ReactNode[] = [];
  const re = /("(?:[^"\\]|\\.)*")(\s*:)?|(-?\b\d+(?:\.\d+)?\b)|(\btrue\b|\bfalse\b|\bnull\b)|(\/\/.*$)/g;
  let last = 0;
  let m: RegExpExecArray | null;
  let i = 0;
  while ((m = re.exec(line))) {
    if (m.index > last) parts.push(<span key={i++} style={{ color: C.dim }}>{line.slice(last, m.index)}</span>);
    if (m[1]) parts.push(<span key={i++} style={{ color: m[2] ? C.sky : '#F2C14A' }}>{m[1]}</span>, m[2] ? <span key={i++} style={{ color: C.dim }}>{m[2]}</span> : null);
    else if (m[3]) parts.push(<span key={i++} style={{ color: C.teal }}>{m[3]}</span>);
    else if (m[4]) parts.push(<span key={i++} style={{ color: C.violet }}>{m[4]}</span>);
    else if (m[5]) parts.push(<span key={i++} style={{ color: C.faint }}>{m[5]}</span>);
    last = re.lastIndex;
  }
  if (last < line.length) parts.push(<span key={i++} style={{ color: C.text }}>{line.slice(last)}</span>);
  return parts;
};

/** A block of code whose lines appear one after another from `at`, every `every` frames. */
export const Code: React.FC<{ lines: string[]; at: number; every?: number; size?: number; title?: string; x: number; y: number; w: number; show?: number; hi?: number[] }> = ({ lines, at, every = 4, size = 24, title, x, y, w, show = 1, hi = [] }) => {
  const f = useCurrentFrame();
  return (
    <div style={{
      position: 'absolute', left: x, top: y, width: w, boxSizing: 'border-box', borderRadius: 20, background: 'rgba(16,16,21,0.96)', border: `1.5px solid ${C.line2}`,
      boxShadow: '0 26px 60px rgba(0,0,0,0.5)', overflow: 'hidden', opacity: clamp01(show * 1.5), transform: `translateY(${(1 - show) * 30}px)`,
    }}>
      {title && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '12px 20px', borderBottom: `1px solid ${C.line}`, background: 'rgba(255,255,255,0.03)' }}>
          {['#FF6B6B', '#FCB33F', '#6CCB5F'].map(c => <div key={c} style={{ width: 12, height: 12, borderRadius: 6, background: c, opacity: 0.8 }} />)}
          <span style={{ marginLeft: 10, fontFamily: MONO, fontSize: 18, color: C.dim }}>{title}</span>
        </div>
      )}
      <div style={{ padding: '18px 24px', fontFamily: MONO, fontSize: size, lineHeight: 1.5, whiteSpace: 'pre' }}>
        {lines.map((line, i) => {
          const k = ease(f, at + i * every, at + i * every + 8, OUT);
          return (
            <div key={i} style={{ opacity: k, transform: `translateX(${(1 - k) * 16}px)`, background: hi.includes(i) ? 'rgba(242,193,74,0.12)' : undefined, borderRadius: 6, margin: '0 -8px', padding: '0 8px' }}>
              {paint(line) }{line.length === 0 ? ' ' : null}
            </div>
          );
        })}
      </div>
    </div>
  );
};

/** A check that passes (✓), fails (✗) or is still waiting. */
export const Gate: React.FC<{ x: number; y: number; label: React.ReactNode; sub?: React.ReactNode; state: 'wait' | 'pass' | 'fail' | 'doubt'; show?: number; w?: number }> = ({ x, y, label, sub, state, show = 1, w = 460 }) => {
  const colour = state === 'pass' ? C.success : state === 'fail' ? C.danger : state === 'doubt' ? C.warning : C.faint;
  const icon = state === 'pass' ? 'checkmark_circle_24_filled' : state === 'fail' ? 'dismiss_circle_24_filled' : state === 'doubt' ? 'question_circle_24_filled' : 'circle_24_regular';
  return (
    <div style={{
      position: 'absolute', left: x, top: y, width: w, display: 'flex', alignItems: 'center', gap: 18, padding: '18px 22px', boxSizing: 'border-box', borderRadius: 20,
      background: 'rgba(28,28,34,0.94)', border: `2px solid ${state === 'wait' ? C.line2 : colour}`, opacity: clamp01(show * 1.5), transform: `translateX(${(1 - show) * -40}px)`,
      boxShadow: state === 'wait' ? '0 18px 40px rgba(0,0,0,0.4)' : `0 0 30px ${colour}44, 0 18px 40px rgba(0,0,0,0.4)`,
    }}>
      <Fluent name={icon} size={46} color={colour} />
      <div>
        <div style={{ fontFamily: SANS, fontSize: 27, fontWeight: 800, color: C.text }}>{label}</div>
        {sub && <div style={{ fontFamily: SANS, fontSize: 19, color: C.dim, marginTop: 2 }}>{sub}</div>}
      </div>
    </div>
  );
};

/** A plain label placed somewhere, centred on (x, y). */
export const Label: React.FC<{ x: number; y: number; children: React.ReactNode; size?: number; color?: string; show?: number; weight?: number; align?: 'center' | 'left' | 'right'; width?: number }> = ({ x, y, children, size = 26, color = C.text, show = 1, weight = 700, align = 'center', width }) => (
  <div style={{
    position: 'absolute', left: x, top: y, width, transform: `translate(${align === 'center' ? '-50%' : align === 'right' ? '-100%' : '0'}, -50%)`, opacity: clamp01(show),
    fontFamily: SANS, fontSize: size, fontWeight: weight, color, textAlign: align, whiteSpace: width ? 'normal' : 'nowrap', lineHeight: 1.3,
  }}>{children}</div>
);

/** A big number or value that pops in, with its unit under it. */
export const Stat: React.FC<{ x: number; y: number; value: React.ReactNode; unit?: React.ReactNode; show: number; color?: string; size?: number }> = ({ x, y, value, unit, show, color = C.accentText, size = 96 }) => (
  <div style={{ position: 'absolute', left: x, top: y, transform: `translate(-50%,-50%) scale(${0.7 + 0.3 * show})`, opacity: clamp01(show * 1.5), textAlign: 'center' }}>
    <div style={{ fontFamily: SANS, fontSize: size, fontWeight: 800, color, letterSpacing: -size * 0.03, lineHeight: 1 }}>{value}</div>
    {unit && <div style={{ fontFamily: SANS, fontSize: size * 0.28, fontWeight: 600, color: C.dim, marginTop: size * 0.12 }}>{unit}</div>}
  </div>
);

/** A highlight ring that pulses around a rectangle while `on`. */
export const Ring: React.FC<{ x: number; y: number; w: number; h: number; on: number; color?: string; r?: number }> = ({ x, y, w, h, on, color = C.accentText, r = 20 }) => {
  const f = useCurrentFrame();
  if (on <= 0.01) return null;
  const p = 6 + Math.sin(f / 6) * 3;
  return <div style={{ position: 'absolute', left: x - p, top: y - p, width: w + 2 * p, height: h + 2 * p, borderRadius: r + p, border: `3px solid ${color}`, opacity: on, boxShadow: `0 0 30px ${color}` }} />;
};
