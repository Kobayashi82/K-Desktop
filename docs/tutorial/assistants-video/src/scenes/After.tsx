import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent, KdIcon } from '../components/Icon';
import { Bubble, Heading, Label, Lander, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, pop } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** Chapter: what happens once its voice is over. */
export const After: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const question = B('question');
  const dictate = B('dictate');
  const history = B('history');
  const phrase = B('phrase');
  const idle = B('idle');
  const secs = Math.max(1, 6 - Math.floor(Math.max(0, f - question.cue('six')) / 30));
  return (
    <AbsoluteFill>
      <Part from={0} to={history.from - 4} first>
        <Heading kicker="Its voice is over" text="A few things are left" at={question.from} y={120} size={54} />
        <Lander x={330} y={480} size={220} state={f > question.cue('six') ? 'listen' : 'idle'} />
        <Bubble text="Shall I remind you at 13:30?" show={pop(f, question.cue('question') - 6)} x={150} y={300} size={32} />
        <Panel x={940} y={280} w={840} show={shown(f, question.cue('six'))} icon="mic_24_filled" color="teal" title="It ended with a question" sub="K-Desktop listens with no wake word">
          <div style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
            <Pill size={40} mono color={C.teal} bg="rgba(62,220,193,0.14)">{secs} s</Pill>
            <span style={{ fontFamily: SANS, fontSize: 26, color: C.text }}>just answer: «yes, please»</span>
          </div>
        </Panel>
        <Panel x={940} y={600} w={840} show={shown(f, dictate.cue('dictate'))} icon="pen_24_filled" color="indigo" title="«Write what I say»" sub="dictation starts now, once its voice is over">
          <div style={{ fontFamily: MONO, fontSize: 24, color: C.text }}>Dear Sam, thanks for…<span style={{ color: C.accentText, opacity: Math.sin(f / 3) > 0 ? 1 : 0 }}>▍</span></div>
        </Panel>
        <Sfx at={question.cue('six')} name="blip-low" volume={0.2} />
      </Part>

      <Part from={history.from - 4} to={idle.from - 4}>
        <Heading kicker="Remembering" text={f < phrase.from - 6 ? 'History and memory' : 'Learning a voice phrase'} at={history.from} y={120} size={54} />
        <div style={{ opacity: 1 - ease(f, phrase.from - 8, phrase.from + 4) }}>
          <Panel x={140} y={290} w={780} show={shown(f, history.cue('history'))} icon="history_24_filled" color="indigo" title="The conversation's history" sub="the last 3 exchanges">
            {['what time is it? → 12:52', 'a timer for tea → 17:15', 'remind me at 13:30? → yes'].map((h, i) => <div key={h} style={{ fontFamily: SANS, fontSize: 23, color: i === 2 ? C.accentText : C.dim, marginBottom: 8 }}>{h}</div>)}
          </Panel>
          <Panel x={1000} y={290} w={780} show={shown(f, history.cue('learn'))} icon="bookmark_24_filled" color="pink" title="In the background" sub="the same model reads the exchange">
            <div style={{ fontFamily: SANS, fontSize: 24, color: C.text }}>remembers: «has lunch around 13:30»</div>
            <div style={{ marginTop: 16, opacity: ease(f, history.to - 30, history.to - 14) }}><Pill size={20} bg={C.accentSoft} color={C.accentText}><Fluent name="arrow_undo_24_filled" size={20} color={C.accentText} />in the activity: see it, undo it</Pill></div>
          </Panel>
          <Label x={960} y={680} size={22} color={C.faint} show={ease(f, history.cue('learn') + 20, history.cue('learn') + 34)}>not for questions, short orders or failures</Label>
        </div>
        {f >= phrase.from - 8 && (
          <>
            <div style={{ position: 'absolute', left: 200, top: 300, display: 'flex', flexDirection: 'column', gap: 26 }}>
              {[
                { at: phrase.from, said: '«open Kamovis»', r: 'nothing happened', c: C.danger, icon: 'dismiss_circle_24_filled' },
                { at: phrase.cue('correct'), said: '«it\'s called K-Movies»', r: 'ran the command K-Movies', c: C.success, icon: 'checkmark_circle_24_filled' },
              ].map(o => (
                <div key={o.said} style={{ display: 'flex', alignItems: 'center', gap: 20, opacity: shown(f, o.at) }}>
                  <Fluent name={o.icon} size={44} color={o.c} />
                  <span style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text, width: 520 }}>{o.said}</span>
                  <span style={{ fontFamily: SANS, fontSize: 26, color: o.c }}>{o.r}</span>
                </div>
              ))}
            </div>
            <Panel x={200} y={560} w={1520} show={shown(f, phrase.cue('phrase'))} lit={1} icon="flash_24_filled" color="gold" title="K-Movies · new voice phrase: «Kamovis»" sub="added with add_voice_phrase · next time it runs straight away, no AI" />
          </>
        )}
        <Sfx at={phrase.cue('phrase')} name="chime" volume={0.2} />
      </Part>

      <Part from={idle.from - 4} to={durationInFrames} last>
        <Heading kicker="Back to rest" text="Idle, and the brain set free" at={idle.from} y={120} size={54} />
        <Lander x={560} y={540} size={280} state="idle" show={shown(f, idle.cue('idle'))} />
        <Panel x={960} y={380} w={760} show={shown(f, idle.cue('thirty'))} icon="brain_circuit_24_filled" color="purple" title="30 minutes unused" sub="the model is unloaded · the graphics card is free">
          <div style={{ height: 20, borderRadius: 10, background: 'rgba(255,255,255,0.08)' }}><div style={{ width: `${(1 - ease(f, idle.cue('thirty') + 10, idle.cue('thirty') + 40)) * 100}%`, height: '100%', borderRadius: 10, background: C.violet }} /></div>
        </Panel>
        <div style={{ position: 'absolute', left: 960, top: 640, opacity: shown(f, idle.cue('thirty') + 20) }}><KdIcon icon="checkmark_24_filled" color="green" size={60} /></div>
      </Part>
    </AbsoluteFill>
  );
};
