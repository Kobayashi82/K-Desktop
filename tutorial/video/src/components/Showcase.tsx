import React from 'react';
import { Freeze, Loop, OffthreadVideo, Sequence, staticFile, useCurrentFrame } from 'remotion';
import { SHOWCASE } from '../generated/showcase';
import { C, SANS } from '../theme';

/** The clip within its loop: it plays, then holds its last frame until the loop starts again. */
const Clip: React.FC<{ src: string; len: number }> = ({ src, len }) => {
  const f = useCurrentFrame();
  const video = <OffthreadVideo src={src} muted style={{ width: '100%', height: '100%', display: 'block' }} />;
  return f < len ? video : <Freeze frame={len - 1}>{video}</Freeze>;
};

/**
 * One of the assistants' showcase clips («Lander/window.minimize»: the real recording «Try» plays, made by
 * tools/showcase.py), looping from frame `from` (with `pause` frames on its last one, for short clips), at 16:9 and
 * `width` wide, with an optional caption under it.
 */
export const Showcase: React.FC<{ clip: string; width?: number; label?: string; from?: number; pause?: number; style?: React.CSSProperties }> = ({ clip, width = 720, label, from = 0, pause = 0, style }) => {
  const len = SHOWCASE[clip] ?? 90;
  return (
    <div style={{ width, ...style }}>
      <div style={{ position: 'relative', width, height: (width * 9) / 16, borderRadius: 18, overflow: 'hidden', boxShadow: '0 40px 90px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.12)', background: '#20202a' }}>
        <Sequence from={from} layout="none">
          <Loop durationInFrames={len + pause} layout="none">
            <Clip src={staticFile(`generated/showcase/${clip}.mp4`)} len={len} />
          </Loop>
        </Sequence>
      </div>
      {label && <div style={{ marginTop: 16, textAlign: 'center', fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.text }}>{label}</div>}
    </div>
  );
};
