import { Easing, interpolate, spring } from 'remotion';

export const FPS = 30;

/** Smooth in and out: the default motion of the video. */
export const SMOOTH = Easing.bezier(0.65, 0, 0.35, 1);
export const OUT = Easing.bezier(0.16, 1, 0.3, 1);
export const IN = Easing.bezier(0.7, 0, 0.84, 0);

/** 0 → 1 between two frames (clamped), eased. */
export const ease = (frame: number, from: number, to: number, easing = SMOOTH): number =>
  interpolate(frame, [from, to], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp', easing });

/** A value moving between two others between two frames. */
export const tween = (frame: number, from: number, to: number, a: number, b: number, easing = SMOOTH): number =>
  a + (b - a) * ease(frame, from, to, easing);

/** A spring from a frame on (0 before it). */
export const pop = (frame: number, start: number, config: { damping?: number; stiffness?: number; mass?: number } = {}): number =>
  spring({ frame: frame - start, fps: FPS, config: { damping: 14, stiffness: 120, mass: 0.8, ...config } });

/** Soft spring without overshoot. */
export const glide = (frame: number, start: number, duration = 24): number =>
  spring({ frame: frame - start, fps: FPS, durationInFrames: duration, config: { damping: 200 } });

/** In at `start`, out at `end` (each `len` frames): 0 → 1 → 0. */
export const inOut = (frame: number, start: number, end: number, len = 12): number =>
  Math.min(ease(frame, start, start + len, OUT), 1 - ease(frame, end - len, end, IN));

/** Piecewise path through [frame, value] keys, eased between each pair. */
export const keys = (frame: number, points: Array<[number, number]>, easing = SMOOTH): number => {
  if (frame <= points[0][0]) return points[0][1];
  for (let i = 1; i < points.length; i++) {
    const [f1, v1] = points[i];
    const [f0, v0] = points[i - 1];
    if (frame <= f1) return v0 + (v1 - v0) * ease(frame, f0, f1, easing);
  }
  return points[points.length - 1][1];
};

/** Deterministic pseudo-random number in [0, 1) for a seed. */
export const rand = (seed: number): number => {
  const x = Math.sin(seed * 12.9898 + 78.233) * 43758.5453;
  return x - Math.floor(x);
};

/** Gentle continuous float, so nothing is ever perfectly still. */
export const drift = (frame: number, seed = 0, amount = 1): { x: number; y: number; r: number } => ({
  x: Math.sin(frame / 53 + seed) * 6 * amount,
  y: Math.cos(frame / 61 + seed * 1.7) * 5 * amount,
  r: Math.sin(frame / 97 + seed * 0.7) * 0.25 * amount,
});

export const clamp01 = (v: number) => Math.min(1, Math.max(0, v));
