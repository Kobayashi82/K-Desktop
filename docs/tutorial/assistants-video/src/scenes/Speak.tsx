import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Bubble, Gate, Heading, Label, Lander, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { speechAmp, Wave } from '../components/Wave';
import { ease, OUT, pop } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

const ANSWER = "It's **12:52**. Lunch is in about an hour, so there's time for a short walk. 🚶";
const SENTENCES = ["It's 12:52.", "Lunch is in about an hour, so there's time for a short walk."];

/** Chapter: the answer spoken sentence by sentence while it is written: Kokoro, effects, position; the echo guard; times. */
export const Speak: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const stream = B('stream');
  const sentences = B('sentences');
  const kokoro = B('kokoro');
  const effects = B('effects');
  const echo = B('echo');
  const times = B('times');
  const written = ANSWER.slice(0, Math.max(0, Math.floor((f - stream.from - 10) / 2.2)));
  const cleanK = ease(f, stream.cue('clean'), stream.cue('clean') + 14);
  const firstSent = f > sentences.cue('done');
  const secondSent = f > sentences.to - 10;
  // Kokoro: the pieces made (above) and played (below) on a timeline.
  const tl = ease(f, kokoro.cue('first') - 6, kokoro.cue('fast') + 30);
  return (
    <AbsoluteFill>
      <Part from={0} to={kokoro.from - 4} first>
        <Heading kicker="Back to the answer" text="Spoken while it is written" at={stream.from} y={120} size={54} />
        <Panel x={120} y={260} w={1680} show={shown(f, stream.from)} icon="brain_circuit_24_filled" color="purple" title="The model writes" sub="up to 8 updates a second">
          <div style={{ fontFamily: MONO, fontSize: 28, color: C.text, minHeight: 44 }}>{written}<span style={{ color: C.accentText, opacity: Math.sin(f / 3) > 0 ? 1 : 0 }}>▍</span></div>
        </Panel>
        <Panel x={120} y={500} w={820} show={shown(f, stream.cue('clean') - 4)} icon="chat_24_filled" color="indigo" title="Chat bubble" sub="cleaned: no Markdown, no emojis">
          <div style={{ fontFamily: SANS, fontSize: 26, color: C.text, opacity: cleanK }}>{written.replace(/\*\*/g, '').replace(/🚶/g, '')}</div>
        </Panel>
        <Panel x={980} y={500} w={820} show={shown(f, sentences.cue('early') - 4)} icon="speaker_2_24_filled" color="pink" title="To the voice" sub="each sentence as soon as it ends">
          {SENTENCES.map((s, i) => {
            const on = i === 0 ? firstSent : secondSent;
            return (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12, opacity: on ? pop(f, i === 0 ? sentences.cue('done') : sentences.to - 10) : 0.15 }}>
                <Fluent name="speaker_2_24_filled" size={26} color={C.accentText} />
                <span style={{ fontFamily: SANS, fontSize: 24, color: C.text }}>{s}</span>
              </div>
            );
          })}
        </Panel>
        <div style={{ position: 'absolute', left: 980, top: 850, opacity: shown(f, sentences.cue('comma')) }}><Pill size={22}>no full stop after 160 letters? cut at the last comma</Pill></div>
        <Sfx at={sentences.cue('done')} name="blip" volume={0.2} />
      </Part>

      <Part from={kokoro.from - 4} to={echo.from - 4}>
        <Heading kicker="Kokoro, on your processor" text={f < effects.from - 6 ? 'Text → phonemes → sound' : 'Its own voice, from its place'} at={kokoro.from} y={120} size={54} />
        <div style={{ opacity: 1 - ease(f, effects.from - 8, effects.from + 4) }}>
          <div style={{ position: 'absolute', left: 120, top: 290, display: 'flex', alignItems: 'center', gap: 26 }}>
            <Pill size={30}>“Lunch is in about an hour, so…”</Pill>
            <Fluent name="arrow_right_24_filled" size={34} color={C.faint} />
            <span style={{ opacity: shown(f, kokoro.cue('phon')) }}><Pill size={30} mono color={C.teal} bg="rgba(62,220,193,0.14)">lˈʌntʃ ɪz ɪn əbˈaʊt ən ˈaʊɚ…</Pill></span>
          </div>
          {/* timeline: made vs played */}
          <div style={{ position: 'absolute', left: 120, top: 440, width: 1680, height: 340, borderRadius: 24, background: 'rgba(20,20,26,0.9)', border: `1.5px solid ${C.line2}`, opacity: shown(f, kokoro.cue('first') - 6) }}>
            <div style={{ position: 'absolute', left: 24, top: 40, fontFamily: SANS, fontSize: 24, fontWeight: 700, color: C.dim }}>making</div>
            <div style={{ position: 'absolute', left: 24, top: 200, fontFamily: SANS, fontSize: 24, fontWeight: 700, color: C.dim }}>playing</div>
            {[
              { make: [0.0, 0.06], play: [0.06, 0.36], t: 'up to the first comma' },
              { make: [0.06, 0.16], play: [0.36, 0.86], t: 'the rest' },
            ].map((p, i) => {
              const L = 180;
              const W = 1440;
              const mk = Math.min(1, Math.max(0, (tl - p.make[0]) / (p.make[1] - p.make[0])));
              const pl = Math.min(1, Math.max(0, (tl - p.play[0]) / (p.play[1] - p.play[0])));
              return (
                <React.Fragment key={i}>
                  <div style={{ position: 'absolute', left: L + p.make[0] * W, top: 30, width: (p.make[1] - p.make[0]) * W * mk, height: 60, borderRadius: 12, background: i ? C.violet : C.accentText, opacity: 0.85 }} />
                  <div style={{ position: 'absolute', left: L + p.play[0] * W, top: 180, width: (p.play[1] - p.play[0]) * W, height: 80, borderRadius: 12, overflow: 'hidden', opacity: pl > 0 ? 1 : 0.15 }}>
                    <div style={{ width: `${pl * 100}%`, height: '100%', overflow: 'hidden' }}><Wave width={(p.play[1] - p.play[0]) * W} height={80} color={i ? C.violet : C.accentText} amp={speechAmp(0.02, 0.98, 6)} scroll={false} seed={i + 30} bars={40} /></div>
                  </div>
                  <div style={{ position: 'absolute', left: L + p.play[0] * W + 10, top: 280, fontFamily: SANS, fontSize: 20, color: C.dim }}>{p.t}</div>
                </React.Fragment>
              );
            })}
          </div>
          <Label x={960} y={850} size={28} color={C.accentText} show={ease(f, kokoro.cue('fast'), kokoro.cue('fast') + 14)}>made in about a sixth of the time it lasts</Label>
        </div>
        {f >= effects.from - 8 && (
          <>
            <Panel x={120} y={290} w={720} show={shown(f, effects.cue('profile'))} icon="headphones_sound_wave_24_filled" color="orange" title="Its voice profile" sub="the Lander: mission control radio">
              <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>{['a narrow band', 'a little overdrive', 'hiss', 'a beep at the end'].map(t => <Pill key={t} size={20}>{t}</Pill>)}</div>
            </Panel>
            {/* the screen with the character on the right: louder on the right speaker */}
            <div style={{ position: 'absolute', left: 940, top: 290, width: 860, height: 480, borderRadius: 20, border: `2px solid ${C.line2}`, background: 'rgba(20,20,26,0.8)', opacity: shown(f, effects.cue('pan')) }}>
              <Lander x={680} y={260} size={170} state="speak" />
              {[['L', 0.35, -40], ['R', 1, 900]].map(([s, v, x]) => (
                <div key={s as string} style={{ position: 'absolute', left: x as number, top: 180, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
                  <Fluent name="speaker_2_24_filled" size={50} color={C.text} />
                  <div style={{ width: 16, height: 120, borderRadius: 8, background: 'rgba(255,255,255,0.08)', position: 'relative' }}><div style={{ position: 'absolute', bottom: 0, width: '100%', height: `${(v as number) * (60 + 40 * Math.abs(Math.sin(f / 4)))}%`, borderRadius: 8, background: C.accentText }} /></div>
                  <span style={{ fontFamily: MONO, fontSize: 20, color: C.dim }}>{s as string}</span>
                </div>
              ))}
            </div>
            <div style={{ position: 'absolute', left: 940, top: 800, opacity: shown(f, effects.cue('level')) }}><Pill size={22}><Fluent name="pulse_24_filled" size={22} color={C.accentText} />its voice level moves the character 30 times a second</Pill></div>
          </>
        )}
        <Sfx at={kokoro.cue('first')} name="blip" volume={0.18} />
      </Part>

      <Part from={echo.from - 4} to={durationInFrames} last>
        <Heading kicker="Last details" text={f < times.from - 6 ? 'It does not wake itself' : 'How long it all takes'} at={echo.from} y={120} size={54} />
        <div style={{ opacity: 1 - ease(f, times.from - 8, times.from + 4) }}>
          <Lander x={360} y={520} size={240} state="speak" />
          <Bubble text="Hi, I'm Lander." show={pop(f, echo.cue('remember'))} x={490} y={380} size={34} />
          <Panel x={900} y={300} w={860} show={shown(f, echo.cue('remember'))} icon="shield_checkmark_24_filled" color="gold" title="K-Desktop remembers" sub="what each assistant is saying, and until when" />
          <Gate x={900} y={540} w={860} label="«Lander» heard inside its own sentence" sub="3+ words, 70 % alike and in order → ignored" state={f > echo.cue('ignore') ? 'fail' : 'wait'} show={shown(f, echo.cue('ignore') - 10)} />
        </div>
        {f >= times.from - 8 && (
          <>
            <Label x={960} y={260} size={24} color={C.dim} show={shown(f, times.from)}>from the moment you stop talking (with the 800 ms of silence)</Label>
            {[
              { at: times.cue('cmd'), t: 'A command', v: 0.95, c: C.success },
              { at: times.cue('cmd') + 30, t: 'A rule', v: 1.15, c: C.teal },
              { at: times.cue('ai'), t: 'The AI, model loaded', v: 1.55, c: C.violet },
            ].map((b, i) => {
              const k = ease(f, b.at, b.at + 24, OUT);
              return (
                <div key={b.t} style={{ position: 'absolute', left: 200, top: 360 + i * 170, width: 1520, opacity: shown(f, b.at) }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 30 }}>
                    <span style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text, width: 420 }}>{b.t}</span>
                    <div style={{ height: 50, width: b.v * 520 * k, borderRadius: 14, background: b.c, opacity: 0.85 }} />
                    <span style={{ fontFamily: MONO, fontSize: 36, color: b.c, whiteSpace: 'nowrap' }}>{(b.v * k).toFixed(2)} s</span>
                  </div>
                </div>
              );
            })}
            <Label x={960} y={900} size={22} color={C.faint} show={ease(f, times.to - 20, times.to)}>to the first sound · measured on the author's PC</Label>
          </>
        )}
        <Sfx at={echo.cue('ignore')} name="pop-low" volume={0.2} />
      </Part>
    </AbsoluteFill>
  );
};
