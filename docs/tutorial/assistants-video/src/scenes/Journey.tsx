import React from 'react';
import { useCurrentFrame } from 'remotion';
import { Wire } from '../components/Diagram';
import { KdIcon } from '../components/Icon';
import { ease, OUT, pop } from '../lib/anim';
import { C, SANS } from '../theme';

/** The stations of an order's journey, left to right. */
export const STATIONS = [
  { id: 'mic', icon: 'mic_24_filled', color: 'teal', title: 'Microphone' },
  { id: 'wake', icon: 'person_voice_24_filled', color: 'sky', title: 'Wake word' },
  { id: 'listen', icon: 'pulse_24_filled', color: 'orange', title: 'Listening' },
  { id: 'text', icon: 'text_t_24_filled', color: 'indigo', title: 'Text' },
  { id: 'deliver', icon: 'send_24_filled', color: 'purple', title: 'Hand-over' },
  { id: 'route', icon: 'branch_fork_24_filled', color: 'gold', title: 'Routing' },
  { id: 'tools', icon: 'wrench_24_filled', color: 'green', title: 'Tools' },
  { id: 'voice', icon: 'speaker_2_24_filled', color: 'pink', title: 'Voice' },
] as const;

/**
 * The journey as a track of stations: each appears at `appear[i]`, the `current` one is lit, and a dot travels along to
 * it. `y` is the centre line; `scale` shrinks the whole track.
 */
export const Journey: React.FC<{ appear: number[]; current?: number; y?: number; scale?: number; dim?: number }> = ({ appear, current = -1, y = 520, scale = 1, dim = 0 }) => {
  const f = useCurrentFrame();
  const gap = 228 * scale;
  const x0 = 960 - (gap * (STATIONS.length - 1)) / 2;
  return (
    <div style={{ position: 'absolute', inset: 0, opacity: 1 - dim * 0.75 }}>
      {STATIONS.slice(1).map((_, i) => (
        <Wire key={i} from={[x0 + i * gap + 46 * scale, y]} to={[x0 + (i + 1) * gap - 46 * scale, y]} draw={ease(f, appear[i + 1] - 14, appear[i + 1] + 4)} flow={current > i ? 1 : 0} color={C.accentText} width={4 * scale} seed={i} />
      ))}
      {STATIONS.map((s, i) => {
        const k = pop(f, appear[i] - 6, { damping: 13 });
        const lit = i === current ? 1 : 0;
        return (
          <div key={s.id} style={{ position: 'absolute', left: x0 + i * gap, top: y, transform: `translate(-50%, -${46 * scale}px) scale(${0.5 + 0.5 * k})`, opacity: Math.min(1, k * 1.5), display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16 * scale }}>
            <div style={{ transform: `scale(${1 + 0.15 * lit})` }}><KdIcon icon={s.icon} color={s.color} size={92 * scale} glow={lit} /></div>
            <div style={{ fontFamily: SANS, fontSize: 26 * scale, fontWeight: 800, color: lit ? C.accentText : C.text, whiteSpace: 'nowrap' }}>{s.title}</div>
          </div>
        );
      })}
    </div>
  );
};

/** A dot that walks the track from station `from` to station `to` between two frames. */
export const Walker: React.FC<{ from: number; to: number; start: number; end: number; y?: number; scale?: number }> = ({ from, to, start, end, y = 520, scale = 1 }) => {
  const f = useCurrentFrame();
  const gap = 228 * scale;
  const x0 = 960 - (gap * (STATIONS.length - 1)) / 2;
  const t = ease(f, start, end);
  const x = x0 + (from + (to - from) * t) * gap;
  const k = ease(f, start - 8, start, OUT);
  return <div style={{ position: 'absolute', left: x - 14, top: y - 150 * scale, width: 28, height: 28, borderRadius: 14, background: C.gold1, boxShadow: `0 0 30px ${C.gold2}`, opacity: k }} />;
};
