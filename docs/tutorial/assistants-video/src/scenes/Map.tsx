import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent, KdIcon } from '../components/Icon';
import { Heading, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';
import { Journey, STATIONS, Walker } from './Journey';

/** Chapter: the map of the journey, and «cheap and safe first». */
export const MapScene: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const st = B('stations');
  const pr = B('principle');
  const appear = STATIONS.map(s => st.cue(s.id));
  const current = appear.reduce((c, t, i) => (f >= t - 4 ? i : c), -1);
  // The layers that settle an order, widest (cheapest) first; the AI is the narrow end.
  const layers = [
    { title: 'Fixed rules', sub: 'patterns, no model', icon: 'flash_24_filled', color: 'green', w: 1180 },
    { title: 'Your commands', sub: 'words, then meaning', icon: 'apps_list_24_filled', color: 'blue', w: 980 },
    { title: 'One letter · programs', sub: 'a tenth of a second', icon: 'text_number_format_24_filled', color: 'indigo', w: 780 },
    { title: 'The AI', sub: 'only what is left', icon: 'brain_circuit_24_filled', color: 'purple', w: 560 },
  ];
  return (
    <AbsoluteFill>
      <Part from={0} to={pr.from - 4} first>
        <Heading kicker="The map" text="From your voice to its voice" at={st.from} y={170} size={56} />
        <Journey appear={appear} current={current} y={560} />
        {current > 0 && <Walker from={Math.max(0, current - 1)} to={current} start={appear[current] - 10} end={appear[current] + 6} y={560} />}
        {appear.map((t, i) => <Sfx key={i} at={t - 4} name={i === 5 ? 'chime' : 'pop'} volume={0.16} />)}
        {/* Who may answer: three lanes out of the routing station. */}
        {['a rule', 'your command', 'the AI'].map((w, i) => {
          const k = shown(f, st.cue('route') + 14 + i * 8);
          return (
            <div key={w} style={{ position: 'absolute', left: 960 + 228 * 0.5 + (i - 1) * 190, top: 760, transform: `translate(-50%, 0) scale(${0.7 + 0.3 * k})`, opacity: Math.min(1, k * 1.4) * (1 - ease(f, st.cue('tools') - 6, st.cue('tools') + 6) * 0.4), fontFamily: SANS, fontSize: 26, fontWeight: 700, color: i === 2 ? C.violet : C.text, padding: '10px 20px', borderRadius: 16, background: 'rgba(255,255,255,0.07)' }}>{w}</div>
          );
        })}
      </Part>
      <Part from={pr.from - 4} to={durationInFrames} last>
        <Heading kicker="One idea, all the way" text="Cheap and safe first" at={pr.from} y={150} />
        {layers.map((l, i) => {
          const at = i === 3 ? pr.cue('left') : pr.cue('cheap') + 10 + i * 10;
          const k = shown(f, at);
          return (
            <div key={l.title} style={{
              position: 'absolute', left: 960 - l.w / 2, top: 340 + i * 150, width: l.w, height: 124, boxSizing: 'border-box', borderRadius: 26, display: 'flex', alignItems: 'center', gap: 22, padding: '0 30px',
              background: i === 3 ? 'rgba(123,63,228,0.22)' : 'rgba(28,28,34,0.94)', border: `2px solid ${i === 3 ? 'rgba(188,140,255,0.7)' : C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 30}px)`,
            }}>
              <KdIcon icon={l.icon} color={l.color} size={70} />
              <div style={{ fontFamily: SANS, fontSize: 36, fontWeight: 800, color: C.text, whiteSpace: 'nowrap' }}>{l.title}</div>
              <div style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 24, color: C.dim, whiteSpace: 'nowrap' }}>{l.sub}</div>
            </div>
          );
        })}
        {/* An order drops through; most are caught early. */}
        {[0, 1, 2].map(i => {
          const start = pr.cue('cheap') + 50 + i * 26;
          const stopAt = [0, 1, 3][i];
          const t = ease(f, start, start + 18 + stopAt * 12, OUT);
          return <div key={i} style={{ position: 'absolute', left: 960 - 200 + i * 200 - 16, top: 300 + t * (stopAt * 150 + 80), width: 32, height: 32, borderRadius: 16, background: C.gold1, boxShadow: `0 0 26px ${C.gold2}`, opacity: ease(f, start - 6, start) }} />;
        })}
        <div style={{ position: 'absolute', left: 1500, top: 860, display: 'flex', alignItems: 'center', gap: 12, opacity: ease(f, pr.cue('left'), pr.cue('left') + 14), fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.violet }}>
          <Fluent name="arrow_up_24_filled" size={30} color={C.violet} style={{ transform: 'rotate(-45deg)' }} />
          what is left over
        </div>
        <Sfx at={pr.from} name="whoosh" volume={0.25} />
        <Sfx at={pr.cue('left')} name="blip" volume={0.22} />
      </Part>
    </AbsoluteFill>
  );
};
