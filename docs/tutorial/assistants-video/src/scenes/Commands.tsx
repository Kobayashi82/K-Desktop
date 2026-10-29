import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Heading, Label, Lander, Meter, Packet, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

// Where the words of the command's name and of what was heard sit, across the stage.
const NAME_X = [660, 960, 1260];
const SAID_X = [380, 640, 860, 1100, 1380];

/** Chapter: asking K-Desktop which commands an order names; the layer of words; splitting an order. */
export const Commands: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const ask = B('ask');
  const words = B('words');
  const accept = B('accept');
  const split = B('split');
  // Said vs. the command's name, word by word.
  const said = [
    { w: 'please', role: 'filler' }, { w: 'emty', role: 'slip' }, { w: 'the', role: 'filler' }, { w: 're cycle', role: 'split' }, { w: 'been', role: 'sound' },
  ];
  const name = ['Empty', 'Recycle', 'Bin'];
  const link = (i: number) => ease(f, words.cue(['slips', 'split', 'sound'][i]) - 4, words.cue(['slips', 'split', 'sound'][i]) + 10);
  return (
    <AbsoluteFill>
      <Part from={0} to={words.from - 4} first>
        <Heading kicker="Layer 2" text="Which commands look like this?" at={ask.from} y={140} />
        <Lander x={300} y={540} size={230} state="think" />
        <Packet from={[420, 540]} to={[1260, 540]} start={ask.cue('match') - 6} end={ask.cue('match') + 22} label='match · "empty the recycle bin"' size={20} />
        <Panel x={1240} y={400} w={560} show={shown(f, ask.cue('match') + 16)} icon="shield_checkmark_24_filled" color="gold" title="K-Desktop" sub="the same matcher as its own voice control">
          <div style={{ opacity: shown(f, ask.cue('norun')) }}><Pill size={24} bg="rgba(255,107,107,0.15)" color={C.danger}><Fluent name="play_circle_24_filled" size={24} color={C.danger} />runs nothing: it only looks</Pill></div>
        </Panel>
        <div style={{ position: 'absolute', left: 300, top: 800, display: 'flex', alignItems: 'center', gap: 20, opacity: shown(f, ask.cue('question') - 30) }}>
          <Fluent name="question_circle_24_filled" size={44} color={C.violet} />
          <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 700, color: C.text }}>«What does my game mode command do?»</span>
          <Fluent name="arrow_right_24_filled" size={36} color={C.faint} />
          <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.violet, opacity: ease(f, ask.cue('question'), ask.cue('question') + 10) }}>a question: skips this, to the AI</span>
        </div>
        <Sfx at={ask.cue('match') - 6} name="whoosh-short" volume={0.2} />
      </Part>

      <Part from={words.from - 4} to={split.from - 4}>
        <Heading kicker="First by words" text="How much of the name did you say?" at={words.from} y={140} />
        {/* the command's name */}
        {name.map((w, i) => (
          <div key={w} style={{ position: 'absolute', left: NAME_X[i], top: 360, transform: 'translate(-50%,-50%)', opacity: shown(f, words.cue('name') - 4) }}>
            <Pill size={44} bg={C.accentSoft} color={C.accentText}>{w}</Pill>
          </div>
        ))}
        <Label x={960} y={290} size={22} color={C.dim} show={shown(f, words.cue('name') - 4)}>the command's name</Label>
        {/* links said → name, behind the words */}
        {[[1, 0], [3, 1], [4, 2]].map(([s, n], i) => <Wire key={i} from={[SAID_X[s], 575]} to={[NAME_X[n] + 1, 400]} draw={link(i)} flow={1} bend={0.05} color={C.teal} />)}
        {/* what was said */}
        {said.map((s, i) => {
          const isFiller = s.role === 'filler';
          const fil = isFiller ? ease(f, words.cue('filler'), words.cue('filler') + 12) : 0;
          return (
            <div key={i} style={{ position: 'absolute', left: SAID_X[i], top: 610, transform: 'translate(-50%,-50%)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, opacity: ease(f, words.from + i * 4, words.from + i * 4 + 10) * (1 - 0.65 * fil) }}>
              <Pill size={40} mono>{s.w}</Pill>
              <span style={{ fontFamily: SANS, fontSize: 19, fontWeight: 700, whiteSpace: 'nowrap', color: isFiller ? C.danger : C.teal, opacity: isFiller ? fil : link(['slip', 'split', 'sound'].indexOf(s.role)) }}>
                {{ filler: 'filler: ignored', slip: 'a slip: 1 letter', split: 'split apart', sound: 'sounds the same' }[s.role]}
              </span>
            </div>
          );
        })}
        <Label x={960} y={720} size={22} color={C.dim} show={ease(f, words.from, words.from + 12)}>what was heard</Label>
        <div style={{ position: 'absolute', left: 1420, top: 450, opacity: shown(f, words.cue('ending')) }}>
          <Pill size={24}>open · opens · opening = one word</Pill>
        </div>
        {/* the score */}
        <Panel x={560} y={790} w={800} show={shown(f, accept.from)} pad={22}>
          <Meter label="Empty Recycle Bin" value={0.93 * ease(f, accept.from, accept.from + 20)} need={0.82} width={740} size={24} />
          <Meter label="Open Recycle Bin" value={0.71 * ease(f, accept.from + 6, accept.from + 26)} need={0.82} width={740} size={24} color={C.sky} />
        </Panel>
        <div style={{ position: 'absolute', left: 1400, top: 820, opacity: shown(f, accept.cue('score')) }}><Pill size={26} bg={C.accentSoft} color={C.accentText}>needs 0.82</Pill></div>
        <div style={{ position: 'absolute', left: 1400, top: 900, opacity: shown(f, accept.cue('margin')) }}><Pill size={26} bg={C.accentSoft} color={C.accentText}>and 0.08 ahead</Pill></div>
        <Sfx at={accept.cue('margin') + 10} name="chime" volume={0.2} />
      </Part>

      <Part from={split.from - 4} to={durationInFrames} last>
        <Heading kicker="Nothing clear as a whole?" text="Split it into pieces" at={split.from} y={140} />
        <div style={{ position: 'absolute', left: 0, right: 0, top: 320, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 18, opacity: shown(f, split.from) }}>
          {['open YouTube', 'and', 'empty the recycle bin'].map((p, i) => {
            const cut = i === 1 ? ease(f, split.cue('split'), split.cue('split') + 10) : 0;
            return <Pill key={i} size={40} mono bg={i === 1 ? `rgba(255,107,107,${0.15 * cut})` : 'rgba(255,255,255,0.08)'} color={i === 1 ? C.danger : C.text}>{p}</Pill>;
          })}
        </div>
        {[
          { piece: 'open YouTube', cmd: 'YouTube', x: 360 },
          { piece: 'empty the recycle bin', cmd: 'Empty Recycle Bin', x: 1000 },
        ].map((p, i) => {
          const k = shown(f, split.cue('papelera') + i * 10);
          return (
            <div key={i} style={{ position: 'absolute', left: p.x, top: 480, width: 560, display: 'flex', alignItems: 'center', gap: 18, padding: 22, borderRadius: 22, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 30}px)` }}>
              <Fluent name="checkmark_circle_24_filled" size={40} color={C.success} />
              <span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 700, color: C.dim }}>{p.piece}</span>
              <Fluent name="arrow_right_24_filled" size={30} color={C.faint} />
              <KdIcon icon="flash_24_filled" color="gold" size={44} />
              <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>{p.cmd}</span>
            </div>
          );
        })}
        <Label x={960} y={660} size={28} color={C.accentText} show={ease(f, split.cue('papelera') + 30, split.cue('papelera') + 44, OUT)}>Two commands, run in the order said</Label>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 780, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 22, opacity: shown(f, split.cue('it')) }}>
          <Pill size={32} mono>open Steam</Pill>
          <Pill size={32} mono bg={C.accentSoft} color={C.accentText}>and close it</Pill>
          <Fluent name="arrow_right_24_filled" size={36} color={C.faint} />
          <Pill size={32} mono color={C.teal} bg="rgba(62,220,193,0.15)">close Steam</Pill>
        </div>
        <Sfx at={split.cue('split')} name="pop-low" volume={0.2} />
        <Sfx at={split.cue('papelera')} name="pop" volume={0.18} />
      </Part>
    </AbsoluteFill>
  );
};
