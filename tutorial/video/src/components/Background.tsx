import React from 'react';
import { AbsoluteFill, interpolateColors, staticFile, useCurrentFrame } from 'remotion';
import { Timeline } from '../lib/timeline';
import { C, MOODS } from '../theme';

/**
 * The stage behind everything: slow coloured light drifting over a dark floor, a faint dot grid, a vignette and film
 * grain. The light takes each chapter's colours, blending across the chapter changes.
 */
export const Background: React.FC<{ timeline: Timeline; moods?: Record<string, readonly string[]> }> = ({ timeline, moods = {} }) => {
  const frame = useCurrentFrame();
  const stops: number[] = [];
  const colours: string[][] = [[], [], []];
  for (const chapter of timeline.chapters) {
    const mood = moods[chapter.id] ?? MOODS[chapter.id] ?? MOODS.intro;
    for (const at of [chapter.from + 10, chapter.from + chapter.durationInFrames - 10]) {
      stops.push(at);
      mood.forEach((c, i) => colours[i].push(c));
    }
  }
  const mood = colours.map(list => interpolateColors(frame, stops, list));

  const orb = (i: number, size: number, ax: number, ay: number, speed: number, opacity: number) => {
    const t = frame / speed + i * 2.1;
    const x = 960 + Math.cos(t) * ax + Math.sin(t * 0.37) * ax * 0.4;
    const y = 540 + Math.sin(t * 0.8) * ay;
    return (
      <div key={i} style={{
        position: 'absolute', left: x - size / 2, top: y - size / 2, width: size, height: size, borderRadius: '50%',
        background: `radial-gradient(circle, ${mood[i]} 0%, transparent 62%)`, opacity,
      }} />
    );
  };

  const gridShift = (frame * 0.25) % 48;
  return (
    <AbsoluteFill style={{ background: C.bg, overflow: 'hidden' }}>
      {orb(0, 1500, 520, 260, 260, 0.28)}
      {orb(1, 1300, 600, 300, 330, 0.22)}
      {orb(2, 1700, 420, 340, 410, 0.26)}
      <AbsoluteFill style={{
        backgroundImage: 'radial-gradient(rgba(255,255,255,0.10) 1.2px, transparent 1.3px)',
        backgroundSize: '48px 48px', backgroundPosition: `${gridShift}px ${gridShift * 0.6}px`,
        maskImage: 'radial-gradient(ellipse 70% 65% at 50% 50%, black 20%, transparent 100%)',
        WebkitMaskImage: 'radial-gradient(ellipse 70% 65% at 50% 50%, black 20%, transparent 100%)',
        opacity: 0.55,
      }} />
      <AbsoluteFill style={{ background: 'radial-gradient(ellipse 85% 80% at 50% 45%, transparent 55%, rgba(0,0,0,0.65) 100%)' }} />
    </AbsoluteFill>
  );
};

/** Film grain over the whole picture: a noise tile that jumps every frame. */
export const Grain: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <AbsoluteFill style={{
      backgroundImage: `url(${staticFile('brand/noise.png')})`, backgroundSize: '256px 256px',
      backgroundPosition: `${(frame * 73) % 256}px ${(frame * 151) % 256}px`, opacity: 0.05, mixBlendMode: 'overlay', pointerEvents: 'none',
    }} />
  );
};
