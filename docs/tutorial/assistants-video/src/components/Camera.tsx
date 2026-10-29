import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { drift, ease, SMOOTH } from '../lib/anim';

/**
 * A move of the camera: from frame `at`, for `dur` frames, it travels to look at (x, y) of the stage with zoom `s`,
 * tilted `rx`/`ry` degrees in 3D and rolled `r` degrees. Missing values keep the previous shot's.
 */
export type Shot = { at: number; dur?: number; x?: number; y?: number; s?: number; r?: number; rx?: number; ry?: number };

type Pose = { x: number; y: number; s: number; r: number; rx: number; ry: number };

export const pose = (frame: number, shots: Shot[], bounds?: { width: number; height: number }): Pose => {
  let p: Pose = { x: 960, y: 540, s: 1, r: 0, rx: 0, ry: 0 };
  let first = true;
  for (const shot of shots) {
    const next: Pose = {
      x: shot.x ?? p.x, y: shot.y ?? p.y, s: shot.s ?? p.s, r: shot.r ?? p.r, rx: shot.rx ?? p.rx, ry: shot.ry ?? p.ry,
    };
    if (first) {
      p = next;
      first = false;
      continue;
    }
    const k = ease(frame, shot.at, shot.at + (shot.dur ?? 36), SMOOTH);
    if (k <= 0) break;
    // Zoom travels in log space so it feels even.
    const s = Math.exp(Math.log(p.s) + (Math.log(next.s) - Math.log(p.s)) * k);
    p = { x: p.x + (next.x - p.x) * k, y: p.y + (next.y - p.y) * k, s, r: p.r + (next.r - p.r) * k, rx: p.rx + (next.rx - p.rx) * k, ry: p.ry + (next.ry - p.ry) * k };
  }
  if (bounds && p.s >= 1.03) {
    // Keep the view inside the stage: never show what lies beyond its edges.
    const hw = 972 / p.s; // a little more: the float moves a few pixels
    const hh = 552 / p.s;
    p.x = Math.min(Math.max(p.x, hw), bounds.width - hw);
    p.y = Math.min(Math.max(p.y, hh), bounds.height - hh);
  }
  return p;
};

/** The stage (1920 × 1080 by default) seen through the camera, with a slight handheld float. */
export const Camera: React.FC<{ shots: Shot[]; float?: number; children: React.ReactNode; width?: number; height?: number; clamp?: boolean }> = ({ shots, float = 1, children, width = 1920, height = 1080, clamp = false }) => {
  const frame = useCurrentFrame();
  const p = pose(frame, shots, clamp ? { width, height } : undefined);
  const d = drift(frame, 3, float);
  return (
    <AbsoluteFill style={{ perspective: 2200, overflow: 'hidden' }}>
      <div style={{
        position: 'absolute', left: 0, top: 0, width, height, transformOrigin: '0 0',
        transform: `translate(${960 + d.x}px, ${540 + d.y}px) rotateX(${p.rx}deg) rotateY(${p.ry}deg) rotate(${p.r + d.r}deg) scale(${p.s}) translate(${-p.x}px, ${-p.y}px)`,
      }}>
        {children}
      </div>
    </AbsoluteFill>
  );
};
