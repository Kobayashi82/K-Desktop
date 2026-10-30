import { useAudioData, visualizeAudio } from '@remotion/media-utils';
import React from 'react';
import { staticFile, useCurrentFrame } from 'remotion';
import { FPS } from '../lib/anim';
import { useChapter } from '../lib/timeline';
import { C } from '../theme';

/**
 * Bars that move with the narrator's real voice: the audio of `beat` (of the current chapter), analysed at each
 * frame. Without it (still loading), a gentle idle wave.
 */
export const VoiceBars: React.FC<{ beat: string; bars?: number; width?: number; height?: number; color?: string; gain?: number; style?: React.CSSProperties }> = ({ beat, bars = 24, width = 420, height = 140, color = C.accentText, gain = 1, style }) => {
  const frame = useCurrentFrame();
  const chapter = useChapter();
  const b = chapter.beats.find(x => x.id === beat);
  const audio = useAudioData(staticFile(b ? b.audio : chapter.beats[0].audio));
  let values: number[];
  const local = frame - (b?.from ?? 0);
  if (audio && b && local >= 0 && local < b.durationInFrames) {
    const spectrum = visualizeAudio({ fps: FPS, frame: local, audioData: audio, numberOfSamples: 64, smoothing: true });
    values = Array.from({ length: bars }, (_, i) => {
      const k = Math.floor((i / bars) * 40) + 1;
      return Math.min(1, Math.sqrt(spectrum[k] ?? 0) * 2.2 * gain);
    });
  } else {
    values = Array.from({ length: bars }, (_, i) => 0.06 + 0.04 * Math.sin(frame / 6 + i * 0.7));
  }
  // Mirror so the loudest sit in the middle.
  const ordered = values.map((_, i) => values[Math.abs(Math.floor(bars / 2) - i) * 2 % bars]);
  const w = width / bars;
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: w * 0.35, height, ...style }}>
      {ordered.map((v, i) => (
        <div key={i} style={{ width: w * 0.65, height: Math.max(w * 0.65, v * height), borderRadius: w, background: `linear-gradient(180deg, ${C.gold1}, ${color})`, boxShadow: `0 0 ${12 * v}px ${color}` }} />
      ))}
    </div>
  );
};
