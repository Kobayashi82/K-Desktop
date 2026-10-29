import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Bubble, Code, Gate, Heading, Label, Lander, Meter, Packet, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT, pop, rand } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

// A made-up map of meanings: each command (and the order) as a point; similar meanings sit together.
const POINTS = [
  { t: 'Empty Recycle Bin', x: 1120, y: 470, c: C.accentText },
  { t: 'Open Recycle Bin', x: 1290, y: 600, c: C.text },
  { t: 'Shut down the computer', x: 640, y: 330, c: C.text },
  { t: 'Volume up', x: 560, y: 760, c: C.text },
  { t: 'YouTube', x: 860, y: 860, c: C.text },
  { t: 'Game mode', x: 1460, y: 320, c: C.text },
];
const ORDER = { t: 'clear out the trash', x: 1030, y: 560 };
const ORIGIN: [number, number] = [380, 940];

/** Chapter: embeddings, the space of meanings, what an order is compared with, the bars, and what the assistant decides. */
export const Meaning: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const intro = B('intro');
  const what = B('what');
  const space = B('space');
  const cosine = B('cosine');
  const anchors = B('anchors');
  const th = B('thresholds');
  const nm = B('notmine');
  const shared = B('shared');
  const result = B('result');
  const numbers = Array.from({ length: 12 }, (_, i) => (rand(i * 7.3 + 2) * 0.4 - 0.2).toFixed(3));
  const cosK = ease(f, cosine.cue('cos') - 6, cosine.cue('cos') + 20);
  return (
    <AbsoluteFill>
      <Part from={0} to={space.from - 4} first>
        <Heading kicker="Second layer" text="Meaning: embeddings" at={intro.from} y={140} />
        <div style={{ position: 'absolute', left: 0, right: 0, top: 320, textAlign: 'center', opacity: shown(f, what.from) }}>
          <Pill size={44}>“clear out the trash”</Pill>
        </div>
        <Wire from={[960, 400]} to={[961, 470]} draw={ease(f, what.cue('sense') - 6, what.cue('sense') + 6)} flow={1} bend={0} />
        <div style={{ position: 'absolute', left: 960, top: 470, transform: 'translateX(-50%)', opacity: shown(f, what.cue('sense')) }}>
          <Panel x={-340} y={0} w={680} icon="cube_24_filled" color="purple" title="EmbeddingGemma" sub="a small model from Google · runs on your PC" />
        </div>
        <Wire from={[960, 600]} to={[961, 680]} draw={ease(f, what.cue('dims') - 10, what.cue('dims'))} flow={1} bend={0} />
        <div style={{ position: 'absolute', left: 160, top: 690, width: 1600, display: 'flex', flexWrap: 'nowrap', gap: 10, justifyContent: 'center', opacity: shown(f, what.cue('dims') - 4) }}>
          <span style={{ fontFamily: MONO, fontSize: 32, color: C.dim }}>[</span>
          {numbers.map((n, i) => <span key={i} style={{ fontFamily: MONO, fontSize: 28, color: Number(n) < 0 ? C.sky : C.accentText, opacity: ease(f, what.cue('dims') + i * 2, what.cue('dims') + i * 2 + 6) }}>{n}</span>)}
          <span style={{ fontFamily: MONO, fontSize: 28, color: C.dim }}>… ]</span>
        </div>
        <div style={{ position: 'absolute', left: 160, top: 770, width: 1600, display: 'flex', gap: 1, height: 40, opacity: ease(f, what.cue('dims') + 20, what.cue('dims') + 40) }}>
          {Array.from({ length: 192 }, (_, i) => { const v = rand(i * 3.1 + 9); return <div key={i} style={{ flex: 1, background: v > 0.5 ? `rgba(242,193,74,${v})` : `rgba(85,210,255,${1 - v})` }} />; })}
        </div>
        <Label x={960} y={860} size={30} color={C.accentText} show={ease(f, what.cue('dims') + 30, what.cue('dims') + 44)}>768 numbers</Label>
        <Sfx at={what.cue('dims')} name="rise" volume={0.18} />
      </Part>

      <Part from={space.from - 4} to={anchors.from - 4}>
        <Heading kicker="A point in space" text={f < cosine.from - 6 ? 'Close meaning, close points' : 'Closeness = cosine'} at={space.from} y={130} size={54} />
        {/* axes */}
        <div style={{ position: 'absolute', left: ORIGIN[0], top: 230, width: 3, height: ORIGIN[1] - 230, background: C.line2 }} />
        <div style={{ position: 'absolute', left: ORIGIN[0], top: ORIGIN[1], width: 1200, height: 3, background: C.line2 }} />
        {POINTS.map((p, i) => {
          const k = shown(f, space.cue('point') + i * 5);
          const lit = i === 0 && f > space.cue('empty') - 4;
          return (
            <div key={p.t} style={{ position: 'absolute', left: p.x, top: p.y, transform: `translate(-50%,-50%) scale(${k})`, display: 'flex', alignItems: 'center', gap: 12 }}>
              <div style={{ width: 22, height: 22, borderRadius: 11, background: lit ? C.accentText : C.sky, boxShadow: lit ? `0 0 24px ${C.accentText}` : undefined }} />
              <span style={{ fontFamily: SANS, fontSize: 24, fontWeight: 700, color: lit ? C.accentText : C.text, whiteSpace: 'nowrap' }}>{p.t}</span>
            </div>
          );
        })}
        {/* the order lands next to its command */}
        {(() => {
          const k = ease(f, space.cue('clean') - 6, space.cue('clean') + 20, OUT);
          const x = 1560 + (ORDER.x - 1560) * k;
          const y = 900 + (ORDER.y - 900) * k;
          return (
            <div style={{ position: 'absolute', left: x, top: y, transform: 'translate(-50%,-50%)', opacity: ease(f, space.cue('clean') - 10, space.cue('clean')), display: 'flex', alignItems: 'center', gap: 12 }}>
              <div style={{ width: 26, height: 26, borderRadius: 13, background: C.gold1, boxShadow: `0 0 30px ${C.gold2}` }} />
              <span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.gold1, whiteSpace: 'nowrap' }}>“{ORDER.t}”</span>
            </div>
          );
        })()}
        {/* vectors from the origin, and the angle between them */}
        <svg style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }} width={1} height={1}>
          {[[ORDER.x, ORDER.y, C.gold1], [POINTS[0].x, POINTS[0].y, C.accentText], [POINTS[3].x, POINTS[3].y, C.sky]].map(([x, y, c], i) => (
            <line key={i} x1={ORIGIN[0]} y1={ORIGIN[1]} x2={ORIGIN[0] + ((x as number) - ORIGIN[0]) * cosK} y2={ORIGIN[1] + ((y as number) - ORIGIN[1]) * cosK} stroke={c as string} strokeWidth={4} opacity={i === 2 ? ease(f, cosine.to - 40, cosine.to - 20) : 1} />
          ))}
        </svg>
        <div style={{ position: 'absolute', left: 1450, top: 700, opacity: ease(f, cosine.cue('cos') + 10, cosine.cue('cos') + 24) }}>
          <div style={{ fontFamily: MONO, fontSize: 26, color: C.dim }}>cos(order, Empty Recycle Bin)</div>
          <div style={{ fontFamily: SANS, fontSize: 60, fontWeight: 800, color: C.accentText }}>0.78</div>
          <div style={{ fontFamily: MONO, fontSize: 22, color: C.dim, marginTop: 10, opacity: ease(f, cosine.to - 40, cosine.to - 20) }}>vs. Volume up: <span style={{ color: C.sky }}>0.21</span></div>
          <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, marginTop: 10 }}>1 = identical · 0 = unrelated</div>
        </div>
        <Sfx at={space.cue('clean')} name="whoosh-short" volume={0.2} />
        <Sfx at={space.cue('empty')} name="chime" volume={0.18} />
      </Part>

      <Part from={anchors.from - 4} to={th.from - 4}>
        <Heading kicker="Compared with what?" text="Each command's anchors" at={anchors.from} y={140} />
        <Panel x={160} y={330} w={560} show={shown(f, anchors.from)} lit={0.6} icon="flash_24_filled" color="gold" title="Empty Recycle Bin" sub="one of your commands" />
        {[
          { cue: 'cname', t: 'Its name', ex: '“Empty Recycle Bin”' },
          { cue: 'phrases', t: 'Its voice phrases', ex: '“clean the bin”' },
          { cue: 'actions', t: 'What its first actions do', ex: '“Empty the recycle bin”' },
        ].map((a, i) => (
          <React.Fragment key={a.cue}>
            <Wire from={[720, 400]} to={[860, 300 + i * 150 + 50]} draw={ease(f, anchors.cue(a.cue) - 6, anchors.cue(a.cue) + 6)} flow={1} />
            <div style={{ position: 'absolute', left: 860, top: 300 + i * 150, width: 880, height: 110, boxSizing: 'border-box', padding: '0 28px', borderRadius: 22, display: 'flex', alignItems: 'center', gap: 22, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: shown(f, anchors.cue(a.cue)) }}>
              <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text, width: 380 }}>{a.t}</span>
              <span style={{ fontFamily: MONO, fontSize: 24, color: C.accentText }}>{a.ex}</span>
              <span style={{ marginLeft: 'auto', display: 'flex', gap: 2 }}>{Array.from({ length: 14 }, (_, j) => <span key={j} style={{ width: 6, height: 30, background: rand(i * 30 + j) > 0.5 ? C.accentText : C.sky, opacity: 0.4 + 0.6 * rand(j * 5 + i) }} />)}</span>
            </div>
          </React.Fragment>
        ))}
        <div style={{ position: 'absolute', left: 860, top: 780, display: 'flex', alignItems: 'center', gap: 16, opacity: shown(f, anchors.cue('once')) }}>
          <KdIcon icon="save_24_filled" color="indigo" size={56} />
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.text }}>Worked out once, kept on disk</span>
          <Pill mono size={22}>anchors.bin</Pill>
        </div>
      </Part>

      <Part from={th.from - 4} to={shared.from - 4}>
        <Heading kicker="How close is close enough?" text={f < nm.from - 6 ? 'The bars' : 'Not K-Desktop\'s job'} at={th.from} y={140} />
        <div style={{ opacity: 1 - ease(f, nm.from - 8, nm.from + 4) }}>
          {[
            { cue: 'sixtytwo', v: 0.62, t: 'A command', s: 'and clearly ahead (0.02)', c: C.teal },
            { cue: 'media', v: 0.8, t: 'A built-in music order', s: '«pause», «next song»…', c: C.sky },
            { cue: 'power', v: 0.9, t: 'Shut down · restart · sign out', s: 'must be said clearly', c: C.danger },
          ].map((b, i) => {
            const k = shown(f, th.cue(b.cue));
            return (
              <div key={b.cue} style={{ position: 'absolute', left: 260, top: 320 + i * 190, width: 1400, opacity: Math.min(1, k * 1.5), transform: `translateX(${(1 - k) * -30}px)` }}>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 20, marginBottom: 14 }}>
                  <span style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text }}>{b.t}</span>
                  <span style={{ fontFamily: SANS, fontSize: 24, color: C.dim }}>{b.s}</span>
                  <span style={{ marginLeft: 'auto', fontFamily: MONO, fontSize: 40, fontWeight: 600, color: b.c }}>≥ {b.v.toFixed(2)}</span>
                </div>
                <div style={{ position: 'relative', height: 20, borderRadius: 10, background: 'rgba(255,255,255,0.07)' }}>
                  <div style={{ width: `${b.v * 100 * k}%`, height: '100%', borderRadius: 10, background: b.c, opacity: 0.8 }} />
                </div>
              </div>
            );
          })}
        </div>
        {f >= nm.from - 8 && (
          <>
            <div style={{ position: 'absolute', left: 0, right: 0, top: 290, textAlign: 'center', opacity: shown(f, nm.cue('tv') - 4) }}><Pill size={42}>“Turn off the TV”</Pill></div>
            <Panel x={260} y={420} w={680} show={shown(f, nm.cue('tv'))} icon="flash_24_filled" color="gold" title="Your commands">
              <Meter label="Shut down the computer" value={0.81 * ease(f, nm.cue('tv') + 10, nm.cue('tv') + 30)} need={0.9} width={620} />
            </Panel>
            <Panel x={980} y={420} w={680} show={shown(f, nm.cue('notmine') - 4)} icon="dismiss_circle_24_filled" color="gray" title="Things that are not K-Desktop's">
              <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginBottom: 18 }}>{['the TV', 'the lights', 'alarms', 'the weather', '…'].map(t => <Pill key={t} size={20}>{t}</Pill>)}</div>
              <Meter label="«turn off the TV»" value={0.93 * ease(f, nm.cue('tv') + 20, nm.cue('tv') + 40)} width={620} color={C.violet} />
            </Panel>
            <Gate x={560} y={800} w={800} label="Closer to «not mine»: nothing runs" sub="and a command that shuts down would need 0.90 anyway" state={f > nm.to - 30 ? 'fail' : 'wait'} show={shown(f, nm.to - 36)} />
          </>
        )}
        <Sfx at={th.cue('power')} name="pop-low" volume={0.2} />
        <Sfx at={nm.to - 30} name="pop-low" volume={0.22} />
      </Part>

      <Part from={shared.from - 4} to={durationInFrames} last>
        <Heading kicker="Back at the assistant" text={f < result.from - 6 ? 'One model, shared' : 'matched: what now?'} at={shared.from} y={130} size={54} />
        <div style={{ opacity: 1 - ease(f, result.from - 8, result.from + 4) }}>
          <Panel x={300} y={380} w={520} show={shown(f, shared.cue('one'))} lit={1} icon="cube_24_filled" color="purple" title="EmbeddingGemma" sub="loaded once, in K-Desktop" />
          <Lander x={1450} y={460} size={220} />
          {[0, 1, 2].map(i => <Packet key={i} from={[1330, 470]} to={[830, 460]} start={shared.cue('embed') + i * 20} end={shared.cue('embed') + 22 + i * 20} label={i % 2 ? 'embedded' : 'embed'} arc={i % 2 ? 60 : -60} size={20} color={i % 2 ? C.sky : C.accentText} />)}
          <Label x={960} y={720} size={26} color={C.dim} show={ease(f, shared.cue('embed'), shared.cue('embed') + 14)}>tool examples, memories and documents too: one copy of the model in memory</Label>
        </div>
        {f >= result.from - 8 && (
          <>
            <Code x={120} y={270} w={700} at={result.cue('matched')} every={3} size={21} title="K-Desktop → assistant" show={shown(f, result.cue('matched') - 4)} hi={f > result.cue('sure') ? [4] : []}
              lines={['{ "t": "matched",', '  "parts": [{', '    "text": "empty the recycle bin",', '    "candidates": [', '      { "name": "Empty Recycle Bin", "score": 0.93 },', '      { "name": "Open Recycle Bin", "score": 0.71 }],', '    "sure": true }],', '  "near": [ …10 closest by meaning ] }']} />
            {[
              { at: result.cue('eighty'), icon: 'play_circle_24_filled', c: C.success, t: 'Sure, and ≥ 0.80', s: 'runs right away · no AI' },
              { at: result.cue('two'), icon: 'question_circle_24_filled', c: C.warning, t: 'Two almost tied', s: '≥ 0.80 and ≥ 0.75, less than 0.08 apart: it asks' },
              { at: result.to - 10, icon: 'arrow_down_24_filled', c: C.violet, t: 'Neither', s: 'next: one letter' },
            ].map((d, i) => {
              const k = shown(f, d.at);
              return (
                <div key={i} style={{ position: 'absolute', left: 900, top: 290 + i * 150, width: 880, display: 'flex', alignItems: 'center', gap: 20, padding: '20px 26px', boxSizing: 'border-box', borderRadius: 22, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateX(${(1 - k) * 40}px)` }}>
                  <Fluent name={d.icon} size={46} color={d.c} />
                  <div><div style={{ fontFamily: SANS, fontSize: 29, fontWeight: 800, color: C.text }}>{d.t}</div><div style={{ fontFamily: SANS, fontSize: 21, color: C.dim }}>{d.s}</div></div>
                </div>
              );
            })}
            <Lander x={1080} y={870} size={150} state={f > result.cue('which') ? 'speak' : 'idle'} show={shown(f, result.cue('which') - 10)} />
            <Bubble text="Empty Recycle Bin or Open Recycle Bin?" show={pop(f, result.cue('which'))} x={1170} y={830} size={26} />
            <Bubble user text="The first one" show={pop(f, result.cue('first'))} x={1790} y={930} size={26} />
          </>
        )}
        <Sfx at={result.cue('run')} name="chime" volume={0.18} />
        <Sfx at={result.cue('which')} name="blip" volume={0.2} />
        <Sfx at={result.cue('first')} name="pop" volume={0.2} />
      </Part>
    </AbsoluteFill>
  );
};
