import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent, KdIcon } from '../components/Icon';
import { Bubble, Heading, Label, Lander, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, pop } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** Chapter: the AI's turn: is there a model, saying it is on it, and the conversation sent to it. */
export const Brain: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const when = B('when');
  const check = B('check');
  const wait = B('wait');
  const prompt = B('prompt');
  const memory = B('memory');
  const ring = ease(f, wait.cue('twofive') - 10, wait.cue('twofive') + 40);
  const blocks = [
    { at: prompt.cue('inst'), icon: 'person_24_filled', color: 'purple', title: 'Instructions', lines: ['You are Lander… (its personality)', 'How to use the tools · short, speakable sentences', 'Answer in English'], role: 'system' },
    { at: prompt.cue('date'), icon: 'calendar_24_filled', color: 'teal', title: 'Date and time', lines: ['Thursday, 29 October 2026, 12:52'], role: 'system' },
    { at: memory.cue('mem'), icon: 'bookmark_24_filled', color: 'pink', title: 'What it remembers, if it fits', lines: ['found by meaning: order vs. each memory'], role: 'system' },
    { at: memory.cue('cmds'), icon: 'flash_24_filled', color: 'gold', title: 'Your commands seen close by', lines: ['«Empty Recycle Bin», «Open Recycle Bin»'], role: 'system' },
    { at: memory.cue('hist'), icon: 'history_24_filled', color: 'indigo', title: 'The last 3 exchanges', lines: ['forgotten after 10 minutes of silence'], role: 'history' },
    { at: memory.to - 40, icon: 'mic_24_filled', color: 'green', title: 'The order', lines: ['what is left of it'], role: 'user' },
  ];
  return (
    <AbsoluteFill>
      <Part from={0} to={prompt.from - 4} first>
        <Heading kicker="Layer 4" text="Enter the AI" at={when.from} y={130} />
        <Lander x={360} y={560} size={260} state="think" />
        <Panel x={640} y={300} w={1140} show={shown(f, check.cue('model') - 4)} icon="brain_circuit_24_filled" color="purple" title="Is there a model?">
          <div style={{ display: 'flex', gap: 14 }}>
            {['llama server · on this PC', 'Ollama', 'OpenAI compatible'].map((s, i) => <Pill key={s} size={24} style={{ opacity: ease(f, check.cue('model') + 10 + i * 8, check.cue('model') + 20 + i * 8) }}>{s}</Pill>)}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginTop: 22, opacity: shown(f, check.cue('load')) }}>
            <Fluent name="arrow_download_24_filled" size={30} color={C.warning} />
            <span style={{ fontFamily: SANS, fontSize: 26, color: C.text }}>Not loaded yet: loading takes a few seconds</span>
          </div>
        </Panel>
        <Bubble text="One moment." show={pop(f, check.cue('moment'))} x={150} y={330} size={36} />
        <Panel x={640} y={620} w={1140} show={shown(f, wait.from)} icon="timer_24_filled" color="teal" title="Silent for 2.5 s?">
          <div style={{ display: 'flex', alignItems: 'center', gap: 30 }}>
            <svg width={90} height={90} viewBox="0 0 90 90"><circle cx={45} cy={45} r={38} stroke="rgba(255,255,255,0.1)" strokeWidth={8} fill="none" /><circle cx={45} cy={45} r={38} stroke={C.teal} strokeWidth={8} fill="none" strokeDasharray={`${239 * ring} 239`} transform="rotate(-90 45 45)" /></svg>
            <span style={{ fontFamily: SANS, fontSize: 26, color: C.text }}>then a waiting phrase</span>
            <span style={{ marginLeft: 'auto', opacity: shown(f, wait.cue('three')) }}><Pill size={22} bg={C.accentSoft} color={C.accentText}>not again for 3 minutes</Pill></span>
          </div>
        </Panel>
        <Sfx at={check.cue('moment')} name="blip" volume={0.2} />
      </Part>

      <Part from={prompt.from - 4} to={durationInFrames} last>
        <Heading kicker="What the model gets" text="The conversation, put together" at={prompt.from} y={120} size={54} />
        {blocks.map((b, i) => {
          const k = shown(f, b.at);
          const col = i < 3 ? 0 : 1;
          const row = i % 3;
          return (
            <div key={b.title} style={{ position: 'absolute', left: col ? 990 : 130, top: 260 + row * 230, width: 800, height: 200, boxSizing: 'border-box', padding: '22px 28px', borderRadius: 24, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 30}px)` }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
                <KdIcon icon={b.icon} color={b.color} size={50} />
                <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>{b.title}</span>
                <span style={{ marginLeft: 'auto', fontFamily: MONO, fontSize: 18, color: C.faint }}>{b.role}</span>
              </div>
              <div style={{ marginTop: 14 }}>{b.lines.map(l => <div key={l} style={{ fontFamily: SANS, fontSize: 22, color: C.dim, lineHeight: 1.45 }}>{l}</div>)}</div>
            </div>
          );
        })}
        <Label x={960} y={985} size={22} color={C.faint} show={ease(f, memory.to - 30, memory.to - 14)}>…and the tools for this order, chosen next</Label>
        {blocks.map((b, i) => <Sfx key={i} at={b.at} name="pop" volume={0.14} />)}
      </Part>
    </AbsoluteFill>
  );
};
