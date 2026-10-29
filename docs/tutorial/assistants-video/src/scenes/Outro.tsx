import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Kinetic } from '../components/Diagram';
import { KdIcon } from '../components/Icon';
import { Lander } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT, pop } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';

const STEPS = [
  { cue: 'wake', icon: 'person_voice_24_filled', color: 'sky', t: 'Wake word', s: 'two opinions + a check' },
  { cue: 'silence', icon: 'pulse_24_filled', color: 'orange', t: 'The order', s: 'until 800 ms of silence' },
  { cue: 'rules', icon: 'flash_24_filled', color: 'green', t: 'Fixed rules', s: 'at once, no AI' },
  { cue: 'commands', icon: 'apps_list_24_filled', color: 'blue', t: 'Your commands', s: 'words, then meaning' },
  { cue: 'letter', icon: 'text_number_format_24_filled', color: 'indigo', t: 'One letter', s: 'a tenth of a second' },
  { cue: 'programs', icon: 'window_24_filled', color: 'red', t: 'Programs', s: 'minimize, quit, open' },
  { cue: 'ai', icon: 'brain_circuit_24_filled', color: 'purple', t: 'The AI', s: 'few tools, checked' },
  { cue: 'voice', icon: 'speaker_2_24_filled', color: 'pink', t: 'The voice', s: 'sentence by sentence' },
];

/** Chapter: the recap, and goodbye. */
export const Outro: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const recap = B('recap');
  const end = B('end');
  return (
    <AbsoluteFill>
      <Part from={0} to={end.from - 4} first>
        <Kinetic text="To sum up" at={recap.from} x={960} y={170} size={64} />
        {STEPS.map((s, i) => {
          const k = pop(f, recap.cue(s.cue) - 4, { damping: 14 });
          const x = 140 + (i % 4) * 420;
          const y = 300 + Math.floor(i / 4) * 300;
          return (
            <div key={s.cue} style={{ position: 'absolute', left: x, top: y, width: 380, height: 250, boxSizing: 'border-box', padding: 26, borderRadius: 26, background: i === 6 ? 'rgba(123,63,228,0.2)' : 'rgba(28,28,34,0.94)', border: `1.5px solid ${i === 6 ? 'rgba(188,140,255,0.6)' : C.line2}`, opacity: Math.min(1, k * 1.5), transform: `scale(${0.7 + 0.3 * k})` }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                <span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.faint }}>{i + 1}</span>
                <KdIcon icon={s.icon} color={s.color} size={62} />
              </div>
              <div style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text, marginTop: 20 }}>{s.t}</div>
              <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, marginTop: 6 }}>{s.s}</div>
            </div>
          );
        })}
        {STEPS.map((s, i) => <Sfx key={i} at={recap.cue(s.cue) - 4} name="pop" volume={0.12} />)}
      </Part>
      <Part from={end.from - 4} to={durationInFrames} last>
        <Lander x={960} y={420} size={260} state="speak" />
        <div style={{ position: 'absolute', left: 0, right: 0, top: 640, textAlign: 'center', fontFamily: SANS, fontSize: 64, fontWeight: 800, color: C.text, opacity: ease(f, end.from, end.from + 16, OUT) }}>All of it on your PC.</div>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 760, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 18, opacity: ease(f, end.from + 30, end.from + 50) }}>
          <Img src={staticFile('brand/icon-192.png')} style={{ width: 64, height: 64 }} />
          <span style={{ fontFamily: SANS, fontSize: 38, fontWeight: 700, color: C.dim }}>K-Desktop</span>
        </div>
        <Sfx at={end.from} name="chime" volume={0.22} />
      </Part>
    </AbsoluteFill>
  );
};
