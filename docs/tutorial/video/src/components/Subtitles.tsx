import React from 'react';
import { useCurrentFrame } from 'remotion';
import { Timeline } from '../lib/timeline';
import { SANS } from '../theme';

/** Burnt-in subtitles (optional: the video normally ships them as out/subtitles.<lang>.srt). */
export const Subtitles: React.FC<{ timeline: Timeline }> = ({ timeline }) => {
  const frame = useCurrentFrame();
  let text = '';
  for (const ch of timeline.chapters)
    for (const b of ch.beats)
      for (const s of b.segments)
        if (frame >= ch.from + s.from && frame < ch.from + s.to + 6) text = s.text;
  if (!text) return null;
  return (
    <div style={{ position: 'absolute', left: 0, right: 0, bottom: 70, display: 'flex', justifyContent: 'center' }}>
      <div style={{
        maxWidth: 1300, textAlign: 'center', fontFamily: SANS, fontSize: 36, fontWeight: 600, color: 'white', lineHeight: 1.3,
        background: 'rgba(0,0,0,0.6)', padding: '10px 22px', borderRadius: 12,
      }}>{text}</div>
    </div>
  );
};
