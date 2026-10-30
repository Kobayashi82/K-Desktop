import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { ease, IN, OUT, pop, SMOOTH } from '../lib/anim';
import { useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';
import { Sfx } from './Sfx';

const EDGE = 18; // frames of the cross-fade between chapters (tools/tts.py OVERLAP)

/** A chapter's picture: it comes in from slightly behind and leaves forwards, blurring, as the next one arrives. */
export const ChapterStage: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const frame = useCurrentFrame();
  const { durationInFrames } = useChapter();
  const enter = ease(frame, 0, EDGE + 6, OUT);
  const exit = ease(frame, durationInFrames - EDGE, durationInFrames, IN);
  const scale = (0.94 + 0.06 * enter) * (1 + 0.08 * exit);
  const blur = (1 - enter) * 14 + exit * 16;
  return (
    <AbsoluteFill style={{ opacity: Math.min(enter * 1.4, 1 - exit), transform: `scale(${scale})`, filter: blur > 0.3 ? `blur(${blur}px)` : undefined }}>
      {children}
    </AbsoluteFill>
  );
};

/** The chapter's number and name: big when it starts, then a small label in the top left corner while it lasts. */
export const ChapterTitle: React.FC<{ index: number }> = ({ index }) => {
  const frame = useCurrentFrame();
  const { title, durationInFrames } = useChapter();
  const appear = pop(frame, 4, { damping: 16, stiffness: 140 });
  const shrink = ease(frame, 34, 56, SMOOTH);
  const leave = ease(frame, durationInFrames - 22, durationInFrames - 8, IN);
  const number = String(index).padStart(2, '0');

  // Big: centred; small: top left.
  const x = 960 + (96 - 960) * shrink;
  const y = 540 + (70 - 540) * shrink;
  const scale = 1 - 0.72 * shrink;
  const wipe = ease(frame, 6, 26, OUT);
  return (
    <AbsoluteFill style={{ pointerEvents: 'none' }}>
      <AbsoluteFill style={{ background: `rgba(11,11,14,${0.55 * (1 - shrink)})` }} />
      <div style={{
        position: 'absolute', left: x, top: y, transform: `translate(${-50 * (1 - shrink)}%, -50%) scale(${scale})`, transformOrigin: 'left center',
        display: 'flex', alignItems: 'center', gap: 34, opacity: appear * (1 - leave),
        // Small, it sits on a dark pill, so it reads cleanly over a full-screen screenshot.
        padding: '14px 56px 14px 40px', margin: '0 0 0 -40px', borderRadius: 90,
        background: `rgba(14,14,18,${0.86 * shrink})`, boxShadow: shrink > 0.5 ? `0 0 0 3px rgba(255,255,255,${0.08 * shrink})` : undefined,
      }}>
        <div style={{
          fontFamily: SANS, fontWeight: 800, fontSize: 150, lineHeight: 1, letterSpacing: -4,
          color: 'transparent', WebkitTextStroke: `3px ${C.accentText}`, transform: `translateY(${(1 - appear) * 40}px)`,
        }}>{number}</div>
        <div style={{ width: 6, height: 130 * appear, borderRadius: 3, background: `linear-gradient(180deg, ${C.gold1}, ${C.accent})` }} />
        <div style={{ overflow: 'hidden', paddingBottom: 10 }}>
          <div style={{
            fontFamily: SANS, fontWeight: 800, fontSize: 120, lineHeight: 1.05, letterSpacing: -3, color: C.text, whiteSpace: 'nowrap',
            clipPath: `inset(0 ${100 - wipe * 100}% 0 0)`, transform: `translateX(${(1 - wipe) * -30}px)`,
          }}>{title}</div>
        </div>
      </div>
      <Sfx at={4} name="whoosh" volume={0.35} />
    </AbsoluteFill>
  );
};

/** Thin bar at the bottom: one segment per chapter, filling with gold as the video advances. */
export const ProgressBar: React.FC<{ chapters: { id: string; label: string; from: number; durationInFrames: number }[]; frame: number }> = ({ chapters, frame }) => {
  if (chapters.length < 3) return null; // only intro and outro (a script still being written): nothing in between
  const total = chapters[chapters.length - 1].from + chapters[chapters.length - 1].durationInFrames;
  const visible = ease(frame, chapters[1].from, chapters[1].from + 30) * (1 - ease(frame, chapters[chapters.length - 1].from, chapters[chapters.length - 1].from + 20));
  return (
    <div style={{ position: 'absolute', left: 96, right: 96, bottom: 34, height: 4, display: 'flex', gap: 6, opacity: visible * 0.9 }}>
      {chapters.slice(1, -1).map(ch => {
        const k = Math.min(1, Math.max(0, (frame - ch.from) / ch.durationInFrames));
        const current = frame >= ch.from && frame < ch.from + ch.durationInFrames;
        return (
          <div key={ch.id} style={{ flex: ch.durationInFrames / total, position: 'relative', height: 4, borderRadius: 2, background: 'rgba(255,255,255,0.12)' }}>
            <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: `${k * 100}%`, borderRadius: 2, background: `linear-gradient(90deg, ${C.accent}, ${C.gold1})`, boxShadow: current ? '0 0 10px rgba(242,193,74,0.7)' : 'none' }} />
            {current && (
              <div style={{
                position: 'absolute', left: 0, bottom: 12, fontFamily: SANS, fontSize: 13, fontWeight: 700, letterSpacing: 1.5,
                color: C.accentText, whiteSpace: 'nowrap', textTransform: 'uppercase',
                opacity: ease(frame, ch.from + 10, ch.from + 30) * (1 - ease(frame, ch.from + ch.durationInFrames - 20, ch.from + ch.durationInFrames)),
              }}>{ch.label}</div>
            )}
          </div>
        );
      })}
    </div>
  );
};

/** A sweep of golden light across the screen at a chapter change. */
export const LightSweep: React.FC<{ at: number; frame: number }> = ({ at, frame }) => {
  const k = ease(frame, at - 14, at + 16, SMOOTH);
  if (k <= 0 || k >= 1) return null;
  const x = -800 + k * 3600;
  return (
    <AbsoluteFill style={{ pointerEvents: 'none', mixBlendMode: 'screen' }}>
      <div style={{
        position: 'absolute', top: -400, left: x - 300, width: 600, height: 1900, transform: 'rotate(18deg)',
        background: 'linear-gradient(90deg, transparent, rgba(255,210,110,0.10) 35%, rgba(255,230,170,0.32) 50%, rgba(255,210,110,0.10) 65%, transparent)',
        filter: 'blur(20px)',
      }} />
    </AbsoluteFill>
  );
};

/**
 * One part of a chapter, shown from frame `from` to `to` (chapter frames). It arrives from slightly behind and leaves
 * forwards with a blur, overlapping the next part by `edge` frames, so the chapter flows from part to part.
 */
export const Part: React.FC<{ from: number; to: number; edge?: number; children: React.ReactNode; first?: boolean; last?: boolean }> = ({ from, to, edge = 16, children, first, last }) => {
  const frame = useCurrentFrame();
  if (frame < from - edge || frame > to + edge) return null;
  const enter = first ? 1 : ease(frame, from - edge, from + edge * 0.5, OUT);
  const exit = last ? 0 : ease(frame, to - edge * 0.5, to + edge, IN);
  const blur = (1 - enter) * 12 + exit * 12;
  return (
    <AbsoluteFill style={{
      opacity: Math.min(enter, 1 - exit), transform: `scale(${(0.95 + 0.05 * enter) * (1 + 0.06 * exit)})`,
      filter: blur > 0.3 ? `blur(${blur}px)` : undefined,
    }}>
      {children}
    </AbsoluteFill>
  );
};
