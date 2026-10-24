import React from 'react';
import { interpolate, useCurrentFrame } from 'remotion';
import { Timeline } from '../lib/timeline';
import { SANS } from '../theme';

// Gaps up to this long (frames) keep the last cue on screen, so the box doesn't blink between sentences.
const HOLD = 12;
const clamp = { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' } as const;

/** Burnt-in subtitles (render.mjs --subtitles): the same cues as out/subtitles.<lang>.srt. */
export const Subtitles: React.FC<{ timeline: Timeline }> = ({ timeline }) => {
  const frame = useCurrentFrame();
  const cues = timeline.subtitles ?? [];
  const i = cues.findIndex((cue, k) => {
    const next = cues[k + 1];
    return frame >= cue.from && frame < (next && next.from - cue.to <= HOLD ? next.from : cue.to + 4);
  });
  if (i < 0) return null;
  const cue = cues[i];
  // It fades in after a silence and out before one; between cues the text just changes.
  const fresh = i === 0 || cue.from - cues[i - 1].to > HOLD;
  const last = i === cues.length - 1 || cues[i + 1].from - cue.to > HOLD;
  const k = Math.min(
    fresh ? interpolate(frame, [cue.from, cue.from + 6], [0, 1], clamp) : 1,
    last ? interpolate(frame, [cue.to - 2, cue.to + 4], [1, 0], clamp) : 1,
  );
  return (
    <div style={{ position: 'absolute', left: 0, right: 0, bottom: 86, display: 'flex', justifyContent: 'center', opacity: k, transform: `translateY(${(1 - k) * 8}px)` }}>
      <div style={{
        maxWidth: 1400, textAlign: 'center', whiteSpace: 'pre-line', fontFamily: SANS, fontSize: 38, fontWeight: 600, lineHeight: 1.34,
        letterSpacing: 0.2, color: '#F7F7F9', textShadow: '0 1px 2px rgba(0,0,0,0.5)', padding: '10px 26px 12px', borderRadius: 14,
        background: 'rgba(8,8,11,0.74)', border: '1px solid rgba(255,255,255,0.07)', boxShadow: '0 8px 30px rgba(0,0,0,0.35)',
      }}>{cue.text}</div>
    </div>
  );
};
