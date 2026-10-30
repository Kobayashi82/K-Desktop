import React from 'react';
import { useCurrentFrame } from 'remotion';
import { ease, OUT, SMOOTH } from '../lib/anim';
import { C } from '../theme';

export type CursorPoint = { at: number; x: number; y: number; dur?: number };
export type Click = { at: number; button?: 'left' | 'right' | 'middle' };

export const cursorAt = (frame: number, path: CursorPoint[]): { x: number; y: number } => {
  let x = path[0].x;
  let y = path[0].y;
  for (let i = 1; i < path.length; i++) {
    const p = path[i];
    const k = ease(frame, p.at, p.at + (p.dur ?? 22), SMOOTH);
    if (k <= 0) break;
    // A slight arc, like a hand moving the mouse.
    const mx = (x + p.x) / 2 + (p.y - y) * 0.12;
    const my = (y + p.y) / 2 - (p.x - x) * 0.12;
    const u = 1 - k;
    x = u * u * x + 2 * u * k * mx + k * k * p.x;
    y = u * u * y + 2 * u * k * my + k * k * p.y;
  }
  return { x, y };
};

const BUTTON_COLOUR = { left: C.accentText, right: '#55D2FF', middle: '#BC8CFF' };

/** The Windows pointer following `path`, with a ring at every click (gold left, blue right, violet middle). */
export const Cursor: React.FC<{ path: CursorPoint[]; clicks?: Click[]; scale?: number; show?: number }> = ({ path, clicks = [], scale = 1, show = 1 }) => {
  const frame = useCurrentFrame();
  const { x, y } = cursorAt(frame, path);
  const press = clicks.reduce((m, c) => Math.max(m, frame >= c.at && frame < c.at + 6 ? 1 - Math.abs(frame - c.at - 3) / 3 : 0), 0);
  return (
    <div style={{ position: 'absolute', left: 0, top: 0, pointerEvents: 'none', opacity: show }}>
      {clicks.map((c, i) => {
        const k = ease(frame, c.at, c.at + 20, OUT);
        if (frame < c.at || k >= 1) return null;
        const colour = BUTTON_COLOUR[c.button ?? 'left'];
        const at = cursorAt(c.at, path);
        return (
          <div key={i} style={{
            position: 'absolute', left: at.x - 34 * scale, top: at.y - 34 * scale, width: 68 * scale, height: 68 * scale, borderRadius: '50%',
            border: `${3 * scale}px solid ${colour}`, transform: `scale(${0.3 + k * 1.1})`, opacity: 1 - k, boxShadow: `0 0 ${20 * scale}px ${colour}`,
          }} />
        );
      })}
      <svg width={34 * scale} height={46 * scale} viewBox="0 0 17 23" style={{
        position: 'absolute', left: x - 2 * scale, top: y - 1 * scale, transform: `scale(${1 - press * 0.12})`, transformOrigin: '2px 1px',
        filter: 'drop-shadow(0 3px 5px rgba(0,0,0,0.5))',
      }}>
        <path d="M1 1 L1 18.5 L5.4 14.4 L8.3 21.2 L11.3 19.9 L8.5 13.3 L14.6 13.3 Z" fill="white" stroke="black" strokeWidth="1.1" strokeLinejoin="round" />
      </svg>
    </div>
  );
};
