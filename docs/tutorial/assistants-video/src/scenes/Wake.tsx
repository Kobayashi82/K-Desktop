import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Bubble, Gate, Heading, Label, Lander, Meter, Packet, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { speechAmp, Wave } from '../components/Wave';
import { ease, inOut, OUT, pop, rand, tween } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** A column of an embedding (96 numbers, drawn as 24 cells), its shades from a seed. */
const Column: React.FC<{ seed: number; h?: number; w?: number; lit?: number; color?: string }> = ({ seed, h = 300, w = 16, lit = 0, color = '85,210,255' }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 2, width: w }}>
    {Array.from({ length: 24 }, (_, r) => (
      <div key={r} style={{ height: (h - 46) / 24, borderRadius: 2, background: `rgba(${color},${0.12 + 0.8 * rand(seed * 31 + r * 7)})`, boxShadow: lit ? `0 0 8px rgba(242,193,74,${0.6 * lit})` : undefined }} />
    ))}
  </div>
);

/** Chapter: the wake word, from the microphone to «listening». */
export const Wake: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const mic = B('mic');
  const oww = B('oww');
  const det = B('detector');
  const agree = B('agree');
  const verify = B('verify');
  const loose = B('loose');
  const guards = B('guards');
  const owner = B('owner');
  const go = B('go');
  const other = B('other');

  // ── 1. microphone and echo cancellation
  const echoK = ease(f, mic.cue('echo') + 10, mic.cue('echo') + 50, OUT);

  // ── 2. openWakeWord: a new embedding every 80 ms (2.4 frames)
  const made = Math.max(0, Math.floor((f - oww.cue('eighty')) / 2.4));
  const strip = 40;

  // ── 3. the detector's two opinions
  const dtw = 0.15 + 0.75 * ease(f, det.cue('dtw') - 4, det.cue('dtw') + 40);
  const cls = 0.1 + 0.78 * ease(f, det.cue('cls') - 4, det.cue('cls') + 40);
  const example = f >= agree.cue('both') - 6 ? 1 : 0;

  // ── 4. phase two
  const typed = 'lander'.slice(0, Math.max(0, Math.floor((f - verify.cue('parakeet') - 6) / 3)));
  const verified = f > verify.cue('sounds') + 10;

  return (
    <AbsoluteFill>
      <Part from={0} to={oww.from - 4} first>
        <Heading kicker="Step 1" text="Echo cancellation" at={mic.from} y={150} />
        <Panel x={260} y={300} w={1400} show={shown(f, mic.cue('mic') - 6)} icon="mic_24_filled" color="teal" title="Microphone" sub="always open · nothing leaves the PC">
          <div style={{ position: 'relative', height: 120 }}>
            <div style={{ position: 'absolute', inset: 0, opacity: 1 - 0.85 * echoK }}><Wave width={1340} height={120} color="rgba(255,140,198,0.85)" amp={() => 0.55} seed={3} bars={140} /></div>
            <div style={{ position: 'absolute', inset: 0 }}><Wave width={1340} height={120} color={C.teal} amp={speechAmp(0.2, 0.8, 5)} seed={7} bars={140} /></div>
          </div>
        </Panel>
        <Panel x={260} y={620} w={1400} show={shown(f, mic.cue('echo'))} icon="speaker_2_24_filled" color="pink" title="What the speakers play" sub="music, a film, the assistant's own voice — subtracted">
          <Wave width={1340} height={80} color="rgba(255,140,198,0.85)" amp={() => 0.55} seed={3} bars={140} />
        </Panel>
        <div style={{ position: 'absolute', left: 1700, top: 420, display: 'flex', alignItems: 'center', gap: 10, opacity: echoK }}>
          <Fluent name="checkmark_circle_24_filled" size={40} color={C.success} />
        </div>
        <Label x={960} y={940} size={32} color={C.accentText} show={echoK}>Just your voice is left</Label>
        <Sfx at={mic.cue('echo')} name="whoosh-short" volume={0.2} />
      </Part>

      <Part from={oww.from - 4} to={verify.from - 4}>
        <Heading kicker="Step 2 · phase one" text={f < det.from - 6 ? 'openWakeWord' : f < agree.from - 6 ? 'Your personal detector' : 'Both must agree'} at={oww.from} y={140} size={54} />
        {/* Sound → spectrogram → embedding */}
        <Panel x={100} y={270} w={520} h={420} show={shown(f, oww.cue('mel') - 6)} icon="data_histogram_24_filled" color="sky" title="Spectrogram" sub="the sound, as energy per pitch">
          <div style={{ display: 'flex', gap: 3, height: 230, alignItems: 'flex-end', overflow: 'hidden' }}>
            {Array.from({ length: 26 }, (_, c) => {
              const n = Math.floor(f / 2) + c;
              return (
                <div key={c} style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                  {Array.from({ length: 12 }, (_, r) => <div key={r} style={{ width: 14, height: 15, borderRadius: 3, background: `rgba(255,176,97,${0.1 + 0.85 * rand(n * 13 + r) * speechAmp(0, 1, 6)((n % 50) / 50)})` }} />)}
                </div>
              );
            })}
          </div>
        </Panel>
        <Wire from={[620, 480]} to={[700, 480]} draw={ease(f, oww.cue('emb') - 10, oww.cue('emb'))} flow={1} />
        <Panel x={700} y={270} w={1120} h={420} show={shown(f, oww.cue('emb') - 6)} icon="table_24_filled" color="indigo" title="One embedding every 80 ms" sub="96 numbers that sum up 80 ms of sound">
          <div style={{ position: 'relative', display: 'flex', gap: 6, height: 230, overflow: 'hidden', justifyContent: 'flex-end' }}>
            {Array.from({ length: strip }, (_, c) => {
              const n = made - strip + 1 + c;
              const inWindow = c >= strip - 16 && f >= det.cue('sixteen') - 4;
              return n < 0 ? <div key={c} style={{ width: 16 }} /> : <Column key={c} seed={n} h={276} lit={inWindow ? 1 : 0} />;
            })}
            {f >= det.cue('sixteen') - 4 && <div style={{ position: 'absolute', right: -4, top: -6, bottom: -6, width: 16 * 22 + 4, border: `3px solid ${C.accentText}`, borderRadius: 10, opacity: ease(f, det.cue('sixteen') - 4, det.cue('sixteen') + 8) }} />}
          </div>
        </Panel>
        <Label x={1640} y={730} size={22} color={C.accentText} show={ease(f, det.cue('sixteen'), det.cue('sixteen') + 12)}>the last 16 = 1.28 s</Label>
        {/* The two opinions */}
        <Panel x={260} y={780} w={1400} show={shown(f, det.cue('two') - 4)} pad={22}>
          <div style={{ display: 'flex', gap: 60, alignItems: 'flex-start' }}>
            <div>
              <Meter label={<span><b>Like your takes</b> <span style={{ color: C.dim }}>(DTW)</span></span>} value={example ? 0.9 : dtw} width={560} show={ease(f, det.cue('dtw') - 6, det.cue('dtw') + 6)} />
            </div>
            <div>
              <Meter label={<span><b>Classifier</b> <span style={{ color: C.dim }}>(you vs. silence, noise, speech)</span></span>} value={example ? (f > agree.cue('both') + 40 ? 0.1 : 0.8) : cls} width={560} show={ease(f, det.cue('cls') - 6, det.cue('cls') + 6)} />
            </div>
            <div style={{ textAlign: 'center', minWidth: 160, opacity: ease(f, agree.cue('mean') - 6, agree.cue('mean') + 8) }}>
              <div style={{ fontFamily: MONO, fontSize: 22, color: C.dim }}>√(dtw × cls)</div>
              <div style={{ fontFamily: SANS, fontSize: 54, fontWeight: 800, color: example && f > agree.cue('both') + 40 ? C.danger : C.accentText }}>
                {(Math.sqrt((example ? 0.9 : dtw) * (example ? (f > agree.cue('both') + 40 ? 0.1 : 0.8) : cls))).toFixed(2)}
              </div>
            </div>
          </div>
        </Panel>
        <Sfx at={oww.cue('emb') - 6} name="whoosh-short" volume={0.2} />
        <Sfx at={agree.cue('both') + 40} name="pop-low" volume={0.25} />
      </Part>

      <Part from={verify.from - 4} to={guards.from - 4}>
        <Heading kicker="Step 3 · phase two" text="Is it really the word?" at={verify.from} y={140} />
        <div style={{ position: 'absolute', left: 0, right: 0, top: 290, display: 'flex', justifyContent: 'center', gap: 40 }}>
          {[['speaker_mute_24_filled', 'No sound'], ['eye_off_24_filled', 'No notice'], ['music_note_2_24_filled', 'Music not lowered']].map(([icon, text], i) => (
            <div key={text} style={{ display: 'flex', alignItems: 'center', gap: 12, opacity: shown(f, verify.cue('quiet') + i * 6, verify.cue('phase2') + 10) }}>
              <Fluent name={icon} size={36} color={C.dim} /><span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.dim }}>{text}</span>
            </div>
          ))}
        </div>
        <Panel x={260} y={400} w={1400} show={shown(f, verify.cue('phase2'))} icon="text_t_24_filled" color="indigo" title="Parakeet transcribes only the clip of the word" sub="speech recognition, on your PC">
          <div style={{ display: 'flex', alignItems: 'center', gap: 30 }}>
            <Wave width={420} height={90} color={C.teal} amp={speechAmp(0.1, 0.9, 2)} scroll={false} seed={21} bars={50} />
            <Fluent name="arrow_right_24_filled" size={40} color={C.accentText} />
            <div style={{ minWidth: 220, height: 80, borderRadius: 16, background: '#15151a', display: 'flex', alignItems: 'center', padding: '0 24px' }}><span style={{ fontFamily: MONO, fontSize: 40, color: C.text }}>“{typed}”</span></div>
            <span style={{ fontFamily: SANS, fontSize: 44, color: C.faint, opacity: ease(f, verify.cue('sounds') - 10, verify.cue('sounds')) }}>≈</span>
            <span style={{ fontFamily: MONO, fontSize: 40, color: C.accentText, opacity: ease(f, verify.cue('sounds') - 10, verify.cue('sounds')) }}>Lander</span>
            <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 10, transform: `scale(${verified ? pop(f, verify.cue('sounds') + 10) : 0})` }}>
              <Fluent name="checkmark_circle_24_filled" size={50} color={C.success} /><span style={{ fontFamily: SANS, fontSize: 36, fontWeight: 800, color: C.success }}>0.91</span>
            </div>
          </div>
        </Panel>
        {/* How sure it must be */}
        <div style={{ position: 'absolute', left: 260, top: 690, width: 1400, display: 'flex', gap: 24 }}>
          {[
            { at: loose.from, icon: 'checkmark_circle_24_filled', color: C.success, t: 'Sounds like the word', s: 'verification ≥ the word\'s threshold' },
            { at: loose.cue('sure'), icon: 'checkmark_circle_24_filled', color: C.success, t: 'Detector very sure', s: 'then 0.15 less is enough' },
            { at: loose.cue('provisional'), icon: 'question_circle_24_filled', color: C.warning, t: 'Still in doubt', s: 'confirmed later, with the order' },
          ].map(r => {
            const k = shown(f, r.at);
            return (
              <div key={r.t} style={{ flex: 1, padding: 24, borderRadius: 22, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 30}px)` }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}><Fluent name={r.icon} size={38} color={r.color} /><span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>{r.t}</span></div>
                <div style={{ fontFamily: SANS, fontSize: 21, color: C.dim, marginTop: 8 }}>{r.s}</div>
              </div>
            );
          })}
        </div>
        <Sfx at={verify.cue('sounds') + 10} name="blip" volume={0.22} />
      </Part>

      <Part from={guards.from - 4} to={go.from - 4}>
        <Heading kicker="Step 4" text={f < owner.from - 6 ? 'Two more filters' : 'Whose word is it?'} at={guards.from} y={140} />
        <div style={{ opacity: 1 - ease(f, owner.from - 8, owner.from + 4) }}>
          <Gate x={160} y={330} w={760} label="Its own voice?" sub="it just said «Hi, I'm Lander» → thrown away" state={f > guards.cue('echo') + 30 ? 'fail' : 'wait'} show={shown(f, guards.cue('echo') - 4)} />
          <Gate x={1000} y={330} w={760} label="Said in passing?" sub="«…showing my brother the Lander» → about it, not to it" state={f > guards.cue('passing') + 40 ? 'fail' : 'wait'} show={shown(f, guards.cue('passing') - 4)} />
          <Bubble text="Hi, I'm Lander." show={pop(f, guards.cue('echo'))} x={240} y={560} size={30} />
          <Bubble user text="I was showing my brother the Lander" show={pop(f, guards.cue('passing'))} x={1760} y={560} size={28} />
          <Label x={960} y={720} size={26} color={C.dim} show={ease(f, guards.cue('passing') + 50, guards.cue('passing') + 66)}>Our order passes both: «Lander, what time is it?»</Label>
        </div>
        {f >= owner.from - 8 && (
          <>
            <div style={{ position: 'absolute', left: 960, top: 340, transform: 'translateX(-50%)', opacity: shown(f, owner.cue('whose')) }}><Pill size={40} bg={C.accentSoft} color={C.accentText}>«Lander»</Pill></div>
            {[
              { cue: 'kdc', icon: 'flash_24_filled', color: 'gold', t: "K-Desktop's commands", s: '«K-Desktop, open the browser»', x: 260 },
              { cue: 'asst', icon: 'person_24_filled', color: 'purple', t: 'One assistant', s: 'its own word', x: 760 },
              { cue: 'asst', icon: 'dismiss_24_filled', color: 'gray', t: 'Nobody', s: 'not listened to', x: 1260 },
            ].map((o, i) => (
              <Panel key={o.t} x={o.x} y={560} w={420} show={shown(f, owner.cue(o.cue) + i * 6)} lit={i === 1 && f > owner.to - 50 ? 1 : 0} icon={o.icon} color={o.color} title={o.t} sub={o.s} dim={i !== 1 && f > owner.to - 50 ? 1 : 0} />
            ))}
            <Wire from={[960, 400]} to={[970, 560]} draw={ease(f, owner.to - 56, owner.to - 40)} flow={1} bend={0.2} />
            <Lander x={970} y={840} size={170} show={shown(f, owner.to - 46)} />
          </>
        )}
        <Sfx at={guards.cue('echo') + 30} name="pop-low" volume={0.22} />
        <Sfx at={guards.cue('passing') + 40} name="pop-low" volume={0.22} />
        <Sfx at={owner.to - 46} name="chime" volume={0.2} />
      </Part>

      <Part from={go.from - 4} to={other.from - 4}>
        <Heading kicker="Step 5" text="Now it is listening" at={go.from} y={140} />
        <Panel x={140} y={300} w={560} show={shown(f, go.cue('duck') - 4)} icon="speaker_2_24_filled" color="pink" title="Other sounds">
          <div style={{ height: 22, borderRadius: 11, background: 'rgba(255,255,255,0.08)' }}>
            <div style={{ width: `${tween(f, go.cue('duck'), go.cue('duck') + 24, 80, 25)}%`, height: '100%', borderRadius: 11, background: '#FF8CC6' }} />
          </div>
          <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, marginTop: 12 }}>turned down while you speak</div>
        </Panel>
        <Panel x={140} y={560} w={560} show={shown(f, go.cue('listen') - 4)} icon="alert_24_filled" color="teal" title="The listening sound" sub="short, over before you speak" />
        <Packet from={[700, 760]} to={[1300, 640]} start={go.cue('listening') - 4} end={go.cue('listening') + 22} label='{"t":"listening","assistant":"lander","active":true}' size={18} />
        <Lander x={1460} y={560} size={300} state={f > go.cue('listening') + 18 ? 'listen' : 'idle'} />
        <div style={{ position: 'absolute', left: 1240, top: 790, width: 520, display: 'flex', flexDirection: 'column', gap: 16 }}>
          <div style={{ opacity: shown(f, go.cue('twenty')) }}><Pill size={26}><Fluent name="speaker_1_24_filled" size={26} color={C.text} />its own voice → 20 %</Pill></div>
          <div style={{ opacity: shown(f, go.cue('keep')) }}><Pill size={26} bg={C.accentSoft} color={C.accentText}><Fluent name="brain_circuit_24_filled" size={26} color={C.accentText} />model in memory? kept there</Pill></div>
        </div>
        <Sfx at={go.cue('listen') - 4} name="blip" volume={0.25} />
        <Sfx at={go.cue('listening') - 4} name="whoosh-short" volume={0.2} />
      </Part>

      <Part from={other.from - 4} to={durationInFrames} last>
        <Heading kicker="Other ways in" text="No wake word needed" at={other.from} y={140} />
        {[0, 1, 2, 3, 4, 5].map(i => <Wire key={i} from={[200 + (i % 3) * 300 + 130, 360 + Math.floor(i / 3) * 260 + 43]} to={[1280, 560]} draw={ease(f, other.to - 40, other.to - 20)} flow={1} seed={i} />)}
        {[
          { cue: 'key', icon: 'keyboard_24_filled', t: 'Hold its key' },
          { cue: 'click', icon: 'cursor_click_24_filled', t: 'Hold a click (450 ms)' },
          { cue: 'chat', icon: 'chat_24_filled', t: 'Type to it' },
          { cue: 'phone', icon: 'phone_24_filled', t: 'Your phone' },
          { cue: 'phone', icon: 'flash_24_filled', t: 'A command' },
          { cue: 'scheduled', icon: 'clock_alarm_24_filled', t: 'A scheduled order' },
        ].map((e, i) => {
          const x = 200 + (i % 3) * 300;
          const y = 360 + Math.floor(i / 3) * 260;
          const k = shown(f, other.cue(e.cue) + (i === 4 ? 10 : 0));
          return (
            <React.Fragment key={e.t}>
              <div style={{ position: 'absolute', left: x, top: y, width: 260, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 14, opacity: Math.min(1, k * 1.5), transform: `scale(${0.7 + 0.3 * k})` }}>
                <KdIcon icon={e.icon} color={['teal', 'sky', 'indigo', 'green', 'gold', 'orange'][i]} size={86} />
                <span style={{ fontFamily: SANS, fontSize: 25, fontWeight: 700, color: C.text, textAlign: 'center' }}>{e.t}</span>
              </div>
            </React.Fragment>
          );
        })}
        <Panel x={1280} y={470} w={480} show={shown(f, other.to - 30)} lit={1} icon="send_24_filled" color="purple" title="The same place" sub="an order for the assistant" />
      </Part>
    </AbsoluteFill>
  );
};
