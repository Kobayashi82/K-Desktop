import React from 'react';
import { useCurrentFrame } from 'remotion';
import { rand } from '../lib/anim';

/**
 * A waveform drawn as mirrored bars. `amp(x)` (x in 0–1 along the width) shapes it; it scrolls with time when
 * `scroll` is on, like audio passing by.
 */
export const Wave: React.FC<{ width: number; height: number; color: string; amp: (x: number) => number; bars?: number; seed?: number; scroll?: boolean; opacity?: number }> = ({ width, height, color, amp, bars = 90, seed = 1, scroll = true, opacity = 1 }) => {
  const f = useCurrentFrame();
  const w = width / bars;
  return (
    <svg width={width} height={height} style={{ display: 'block', opacity }}>
      {Array.from({ length: bars }, (_, i) => {
        const x = i / bars;
        const n = i + (scroll ? Math.floor(f / 2) : 0);
        const v = Math.max(0.03, amp(x) * (0.35 + 0.65 * rand(n * 1.37 + seed)));
        const h = v * height;
        return <rect key={i} x={i * w + w * 0.2} y={(height - h) / 2} width={w * 0.6} height={h} rx={w * 0.3} fill={color} />;
      })}
    </svg>
  );
};

/** Voice-like envelope: words as bumps between `from` and `to` (0–1), silence elsewhere. */
export const speechAmp = (from: number, to: number, words = 5) => (x: number) => {
  if (x < from || x > to) return 0.04;
  const t = (x - from) / (to - from);
  return 0.25 + 0.75 * Math.abs(Math.sin(t * Math.PI * words)) * Math.sin(t * Math.PI) ** 0.3;
};
