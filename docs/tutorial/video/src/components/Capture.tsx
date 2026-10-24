import React, { createContext, useContext } from 'react';
import { Img, OffthreadVideo, staticFile, useCurrentFrame } from 'remotion';
import { ease, OUT } from '../lib/anim';
import { C, SANS } from '../theme';
import { Camera, Shot } from './Camera';
import { Click, Cursor, CursorPoint } from './Cursor';

export type CaptureInfo = { file: string; width?: number; height?: number };
type Media = { lang: string; captures: Record<string, CaptureInfo> };

const MediaContext = createContext<Media>({ lang: 'en', captures: {} });

export const MediaProvider: React.FC<Media & { children: React.ReactNode }> = ({ lang, captures, children }) => (
  <MediaContext.Provider value={{ lang, captures }}>{children}</MediaContext.Provider>
);

/** The language of the video being rendered. */
export const useLang = (): string => useContext(MediaContext).lang;

/** A real screenshot (docs/tutorial/captures/<lang>): the one of the video's language, or the English one when there is none. */
export const useCapture = (name: string): CaptureInfo & { width: number; height: number } => {
  const { lang, captures } = useContext(MediaContext);
  const info = captures[`${lang}/${name}`] ?? captures[`en/${name}`] ?? { file: `captures/en/${name}.png` };
  return { width: 1920, height: 1080, ...info };
};

/** A region of a screenshot to point at, in its own pixels, from `from` to `to` (frames). */
export type Focus = { from: number; to: number; rect: [number, number, number, number]; label?: string; at?: 'top' | 'bottom' | 'left' | 'right' };

/** Dims everything but the focused region, outlined in gold, with an optional label beside it. */
export const FocusLayer: React.FC<{ focus: Focus[]; width: number; height: number }> = ({ focus, width, height }) => {
  const f = useCurrentFrame();
  const active = focus.map(x => ({ ...x, k: Math.min(ease(f, x.from, x.from + 12, OUT), 1 - ease(f, x.to - 10, x.to)) })).filter(x => x.k > 0);
  if (!active.length) return null;
  const strongest = Math.max(...active.map(a => a.k));
  const id = `hole-${Math.round(active[0].from)}`;
  return (
    <>
      <svg width={width} height={height} style={{ position: 'absolute', left: 0, top: 0 }}>
        <defs>
          <mask id={id}>
            <rect width={width} height={height} fill="white" />
            {active.map((a, i) => <rect key={i} x={a.rect[0] - 8} y={a.rect[1] - 8} width={a.rect[2] + 16} height={a.rect[3] + 16} rx={12} fill="black" opacity={a.k} />)}
          </mask>
        </defs>
        <rect width={width} height={height} fill="rgba(5,5,8,0.62)" mask={`url(#${id})`} opacity={strongest} />
        {active.map((a, i) => (
          <rect key={i} x={a.rect[0] - 8} y={a.rect[1] - 8} width={a.rect[2] + 16} height={a.rect[3] + 16} rx={12} fill="none" stroke={C.accentText} strokeWidth={3} opacity={a.k}
            style={{ filter: 'drop-shadow(0 0 10px rgba(242,193,74,0.7))' }} />
        ))}
      </svg>
      {active.filter(a => a.label).map((a, i) => {
        const [x, y, w, h] = a.rect;
        const side = a.at ?? 'bottom';
        const pos = side === 'top' ? { left: x, top: y - 22, transform: 'translateY(-100%)' } : side === 'left' ? { left: x - 24, top: y + h / 2, transform: 'translate(-100%, -50%)' } : side === 'right' ? { left: x + w + 24, top: y + h / 2, transform: 'translateY(-50%)' } : { left: x, top: y + h + 22 };
        return (
          <div key={i} style={{
            position: 'absolute', ...pos, opacity: a.k, whiteSpace: 'nowrap', fontFamily: SANS, fontSize: 26, fontWeight: 800, color: '#1a1306',
            background: 'linear-gradient(180deg, #FFD970, #F2B807)', padding: '8px 18px', borderRadius: 14, boxShadow: '0 10px 30px rgba(0,0,0,0.5)',
          }}>{a.label}</div>
        );
      })}
    </>
  );
};

/**
 * A real screenshot filling the stage, seen through a camera (shots in the screenshot's pixels), with focus regions,
 * an added mouse pointer (the screenshots have none) and anything else drawn on top as children (same pixels).
 */
export const CaptureShot: React.FC<{
  name: string; shots?: Shot[]; focus?: Focus[]; cursor?: CursorPoint[]; clicks?: Click[]; children?: React.ReactNode; float?: number; frame?: boolean;
}> = ({ name, shots, focus = [], cursor, clicks, children, float = 0.6, frame = false }) => {
  const cap = useCapture(name);
  const { width, height } = cap;
  return (
    <Camera shots={shots ?? [{ at: 0, x: width / 2, y: height / 2, s: Math.min(1920 / width, 1080 / height) }]} width={width} height={height} float={float} clamp={width >= 1920}>
      <div style={{ position: 'absolute', left: 0, top: 0, width, height, borderRadius: frame ? 14 : 0, overflow: 'hidden', boxShadow: frame ? '0 40px 100px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.1)' : undefined }}>
        <Img src={staticFile(cap.file)} style={{ width, height, display: 'block' }} />
        <FocusLayer focus={focus} width={width} height={height} />
        {children}
      </div>
      {cursor && <Cursor path={cursor} clicks={clicks} />}
    </Camera>
  );
};

/** A real screen recording (docs/tutorial/captures/<lang>/clips), from frame `from` of the clip. */
export const CaptureClip: React.FC<{ name: string; startFrom?: number; style?: React.CSSProperties; volume?: number }> = ({ name, startFrom = 0, style, volume = 0 }) => {
  const cap = useCapture(name);
  return <OffthreadVideo src={staticFile(cap.file)} startFrom={startFrom} volume={volume} style={{ width: '100%', height: '100%', ...style }} />;
};
