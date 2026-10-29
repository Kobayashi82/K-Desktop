import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Kinetic } from '../components/Diagram';
import { KdIcon } from '../components/Icon';
import { Bubble, Lander, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, pop, tween } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';
import { Journey } from './Journey';

/** Chapter: the hook, what happens between your voice and its voice, and the promise. */
export const Intro: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const hook = B('hook');
  const hook2 = B('hook2');
  const promise = B('promise');
  // The Lander moves aside when the hidden machinery shows up.
  const aside = ease(f, hook2.from - 6, hook2.from + 24);
  const things = [
    { cue: 'checks', icon: 'shield_checkmark_24_filled', color: 'green', title: 'Checks', x: 1040, y: 330 },
    { cue: 'models', icon: 'cube_24_filled', color: 'purple', title: 'Models', x: 1500, y: 330 },
    { cue: 'vectors', icon: 'data_scatter_24_filled', color: 'sky', title: 'Vectors', x: 1040, y: 640 },
    { cue: 'decisions', icon: 'branch_fork_24_filled', color: 'gold', title: 'Decisions', x: 1500, y: 640 },
  ];
  return (
    <AbsoluteFill>
      <Part from={0} to={promise.from - 4} first>
        <div style={{ position: 'absolute', left: 96, top: 70, display: 'flex', alignItems: 'center', gap: 16, opacity: ease(f, 4, 24) }}>
          <Img src={staticFile('brand/icon-192.png')} style={{ width: 54, height: 54 }} />
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.dim }}>K-Desktop · how the assistants work</span>
        </div>
        <Lander x={tween(f, hook2.from - 6, hook2.from + 24, 1180, 440)} y={tween(f, hook2.from - 6, hook2.from + 24, 600, 560)} size={tween(f, hook2.from - 6, hook2.from + 24, 340, 300)}
          state={f < hook.cue('answers') - 4 ? (f > hook.cue('ask') ? 'listen' : 'idle') : f < hook.to ? 'speak' : 'idle'} />
        <div style={{ opacity: 1 - aside }}>
          <Bubble user text="Lander, what time is it?" show={pop(f, hook.cue('ask') - 4)} x={960} y={330} size={46} />
          <Bubble text="It's 12:52. Over." show={pop(f, hook.cue('answers'))} x={1380} y={820} size={42} />
        </div>
        {/* What happens in between. */}
        {things.map((t, i) => {
          const k = shown(f, hook2.cue(t.cue) - 4);
          return (
            <div key={t.cue} style={{ position: 'absolute', left: t.x, top: t.y, transform: `translate(-50%,-50%) scale(${0.6 + 0.4 * k})`, opacity: Math.min(1, k * 1.5), display: 'flex', alignItems: 'center', gap: 22, padding: '24px 34px 24px 24px', borderRadius: 28, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, boxShadow: '0 24px 60px rgba(0,0,0,0.45)', width: 330 }}>
              <KdIcon icon={t.icon} color={t.color} size={84} />
              <span style={{ fontFamily: SANS, fontSize: 40, fontWeight: 800, color: C.text }}>{t.title}</span>
              <Sfx at={hook2.cue(t.cue) - 4} name={i === 3 ? 'chime' : 'pop'} volume={0.22} />
            </div>
          );
        })}
        <div style={{ position: 'absolute', left: 1270, top: 820, transform: 'translate(-50%,-50%)', fontFamily: SANS, fontSize: 30, fontWeight: 700, color: C.accentText, opacity: ease(f, hook2.cue('decisions') + 30, hook2.cue('decisions') + 50) }}>…and only sometimes the AI</div>
      </Part>
      <Part from={promise.from - 4} to={durationInFrames} last>
        <Kinetic text="Assistants, inside" at={promise.from} x={960} y={300} size={110} />
        <div style={{ position: 'absolute', left: 0, right: 0, top: 400, textAlign: 'center', fontFamily: SANS, fontSize: 36, color: C.dim, opacity: ease(f, promise.cue('trip'), promise.cue('trip') + 16) }}>
          one order, step by step
        </div>
        <Journey appear={[promise.cue('wake'), ...Array.from({ length: 6 }, (_, i) => promise.cue('wake') + 6 + i * 5), promise.cue('ends')]} current={f > promise.cue('ends') ? 7 : f > promise.cue('wake') ? 1 : -1} y={700} scale={0.95} />
        <Sfx at={promise.from} name="whoosh" volume={0.3} />
        <Sfx at={promise.cue('ends')} name="chime" volume={0.2} />
      </Part>
      <Sfx at={hook.cue('ask') - 4} name="pop" volume={0.25} />
      <Sfx at={hook.cue('answers')} name="blip" volume={0.25} />
      <Sfx at={hook2.from} name="whoosh-short" volume={0.25} />
    </AbsoluteFill>
  );
};
