import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Heading, Label, Lander, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT, pop } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';

/** The layers an order falls through, in order; the AI last. */
export const LAYERS = [
  { id: 'rules', title: 'Fixed rules', sub: 'the time, a timer, a note, cancel, undo…', icon: 'flash_24_filled', color: 'green' },
  { id: 'commands', title: 'Your commands', sub: 'by words, then by meaning, then one letter', icon: 'apps_list_24_filled', color: 'blue' },
  { id: 'programs', title: 'Programs', sub: 'minimize Chrome, quit Spotify, open Steam', icon: 'window_24_filled', color: 'orange' },
  { id: 'ai', title: 'The AI', sub: 'with a few tools, only for what is left', icon: 'brain_circuit_24_filled', color: 'purple' },
];

/** The four layers as a column of cards; `lit` is the index lit, `appear` when each shows up. */
export const Layers: React.FC<{ appear: number[]; lit?: number; x?: number; y?: number; w?: number; gap?: number; small?: boolean }> = ({ appear, lit = -1, x = 560, y = 300, w = 800, gap = 150, small }) => {
  const f = useCurrentFrame();
  return (
    <>
      {LAYERS.map((l, i) => {
        const k = shown(f, appear[i]);
        const on = i === lit;
        return (
          <React.Fragment key={l.id}>
            {i > 0 && <Wire from={[x + w / 2, y + (i - 1) * gap + (small ? 90 : 110)]} to={[x + w / 2 + 1, y + i * gap]} draw={ease(f, appear[i] - 8, appear[i] + 4)} flow={1} bend={0.1} width={3} />}
            <div style={{
              position: 'absolute', left: x, top: y + i * gap, width: w, height: small ? 90 : 110, boxSizing: 'border-box', padding: '0 28px', borderRadius: 24, display: 'flex', alignItems: 'center', gap: 22,
              background: i === 3 ? 'rgba(123,63,228,0.2)' : 'rgba(28,28,34,0.94)', border: `2px solid ${on ? C.accentText : i === 3 ? 'rgba(188,140,255,0.6)' : C.line2}`,
              boxShadow: on ? '0 0 50px rgba(242,193,74,0.35)' : '0 18px 40px rgba(0,0,0,0.4)', opacity: Math.min(1, k * 1.5), transform: `translateX(${(1 - k) * -40}px)`,
            }}>
              <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.faint, width: 26 }}>{i + 1}</span>
              <KdIcon icon={l.icon} color={l.color} size={small ? 54 : 64} glow={on ? 0.6 : 0} />
              <div>
                <div style={{ fontFamily: SANS, fontSize: small ? 28 : 32, fontWeight: 800, color: C.text }}>{l.title}</div>
                <div style={{ fontFamily: SANS, fontSize: small ? 19 : 21, color: C.dim }}>{l.sub}</div>
              </div>
            </div>
          </React.Fragment>
        );
      })}
    </>
  );
};

/** Chapter: the order reaches the assistant, and who decides what is done. */
export const Turn: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const arrive = B('arrive');
  const chat = B('chat');
  const files = B('files');
  const who = B('who');
  const replaced = ease(f, arrive.cue('replace') + 10, arrive.cue('replace') + 30);
  return (
    <AbsoluteFill>
      <Part from={0} to={who.from - 4} first>
        <Heading kicker="In the assistants' process" text="One conversation per assistant" at={arrive.from} y={140} size={54} />
        <Lander x={330} y={560} size={260} state="think" show={shown(f, arrive.from)} />
        <Panel x={620} y={330} w={620} show={shown(f, arrive.cue('conv'))} icon="chat_multiple_24_filled" color="purple" title="Lander's conversation" sub="a new order replaces the one going">
          <div style={{ position: 'relative', height: 160 }}>
            <div style={{ position: 'absolute', left: 0, right: 0, top: 0, padding: '14px 18px', borderRadius: 14, background: 'rgba(255,255,255,0.06)', fontFamily: SANS, fontSize: 24, color: C.dim, opacity: 1 - 0.6 * replaced, transform: `translateY(${replaced * 10}px)` }}>
              «tell me a long story…» <span style={{ color: C.danger, fontWeight: 700, opacity: replaced }}>cancelled</span>
              <div style={{ position: 'absolute', left: 12, right: 12, top: '50%', height: 3, background: C.danger, transformOrigin: 'left', transform: `scaleX(${replaced})` }} />
            </div>
            <div style={{ position: 'absolute', left: 0, right: 0, top: 84, padding: '14px 18px', borderRadius: 14, background: C.accentSoft, fontFamily: SANS, fontSize: 24, fontWeight: 700, color: C.accentText, opacity: pop(f, arrive.cue('replace')) }}>
              «what time is it?»
            </div>
          </div>
        </Panel>
        <Panel x={1300} y={330} w={500} show={shown(f, chat.cue('chat') - 4)} icon="chat_24_filled" color="indigo" title="Chat and log">
          <div style={{ display: 'flex', justifyContent: 'flex-end' }}><Pill size={22} bg={C.accentText} color="#1a1306">what time is it?</Pill></div>
          <div style={{ fontFamily: SANS, fontSize: 19, color: C.dim, marginTop: 14 }}>log: order for «lander» by voice: «what time is it?»</div>
        </Panel>
        {/* Files go straight to the AI. */}
        <div style={{ position: 'absolute', left: 620, top: 760, display: 'flex', alignItems: 'center', gap: 22, opacity: shown(f, files.cue('files') - 4) }}>
          <KdIcon icon="document_24_filled" color="gray" size={70} />
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.text }}>With files?</span>
          <Fluent name="arrow_right_24_filled" size={36} color={C.violet} />
          <KdIcon icon="brain_circuit_24_filled" color="purple" size={70} />
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.violet }}>straight to the AI</span>
        </div>
        <div style={{ position: 'absolute', left: 620, top: 870, display: 'flex', alignItems: 'center', gap: 22, opacity: shown(f, files.cue('router')) }}>
          <KdIcon icon="branch_fork_24_filled" color="gold" size={70} />
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.accentText }}>Otherwise: the routing</span>
        </div>
        <Sfx at={arrive.cue('replace')} name="pop" volume={0.2} />
        <Sfx at={files.cue('router')} name="whoosh-short" volume={0.2} />
      </Part>
      <Part from={who.from - 4} to={durationInFrames} last>
        <Heading kicker="Who decides?" text={f < who.cue('code') - 4 ? 'Who decides?' : 'Code decides, in layers'} at={who.from} y={140} />
        <Layers appear={[who.cue('rules'), who.cue('commands'), who.cue('programs'), who.cue('ai')]} lit={f >= who.cue('ai') ? 3 : -1} x={560} y={300} />
        <div style={{ position: 'absolute', left: 1400, top: 330, width: 380, opacity: ease(f, who.cue('code'), who.cue('code') + 14, OUT) }}>
          <div style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text, lineHeight: 1.35 }}>Always the same order.</div>
          <div style={{ fontFamily: SANS, fontSize: 24, color: C.dim, marginTop: 14, lineHeight: 1.4 }}>Each layer either settles the order, or passes on what it could not.</div>
        </div>
        <Label x={1600} y={780} size={26} color={C.violet} show={ease(f, who.cue('ai') + 10, who.cue('ai') + 24)}>Not the AI deciding</Label>
        <Sfx at={who.cue('code')} name="chime" volume={0.2} />
      </Part>
    </AbsoluteFill>
  );
};
