import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent } from '../components/Icon';
import { Code, Gate, Heading, Label, Lander, Packet, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { Wave } from '../components/Wave';
import { ease, inOut, OUT } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

// The audio of the order on a time line, 0–1 across the track: the word, a short pause, the rest, then silence.
const L = 160;
const W = 1600;
const SEGS = [
  { a: 0.02, b: 0.17, text: 'Lander,' },
  { a: 0.25, b: 0.44, text: 'what time' },
  { a: 0.5, b: 0.63, text: 'is it?' },
];
const amp = (x: number) => (SEGS.some(s => x >= s.a && x <= s.b) ? 0.3 + 0.7 * Math.abs(Math.sin(x * 70)) : 0.04);

/** Chapter: recording the order, its pauses and its end, choosing the text, and «heard». */
export const Listen: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const capture = B('capture');
  const vad = B('vad');
  const pause = B('pause');
  const end = B('end');
  const options = B('options');
  const echo = B('echo');
  const heard = B('heard');
  // The play head sweeps the order while it is said.
  const head = ease(f, capture.from, capture.to + 20);
  const x = (v: number) => L + v * W;
  const endSilence = ease(f, end.cue('eight') - 4, end.cue('eight') + 30);
  return (
    <AbsoluteFill>
      <Part from={0} to={options.from - 4} first>
        <Heading kicker="The order" text="Recording until you stop" at={capture.from} y={140} />
        {/* track */}
        <div style={{ position: 'absolute', left: L, top: 330, width: W, height: 200, borderRadius: 24, background: 'rgba(20,20,26,0.9)', border: `1.5px solid ${C.line2}`, overflow: 'hidden' }}>
          <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: `${head * 100}%`, overflow: 'hidden' }}>
            <Wave width={W} height={200} color={C.teal} amp={amp} scroll={false} seed={4} bars={200} />
          </div>
          {/* voice activity */}
          {SEGS.map((s, i) => (
            <div key={i} style={{ position: 'absolute', left: s.a * W, width: (s.b - s.a) * W, top: 0, bottom: 0, background: 'rgba(62,220,193,0.13)', borderLeft: `2px solid rgba(62,220,193,0.6)`, borderRight: `2px solid rgba(62,220,193,0.6)`, opacity: ease(f, vad.cue('silero') + i * 6, vad.cue('silero') + i * 6 + 12) }} />
          ))}
          {/* the end silence */}
          <div style={{ position: 'absolute', left: 0.63 * W, width: 0.2 * W * endSilence, top: 0, bottom: 0, background: 'repeating-linear-gradient(45deg, rgba(242,193,74,0.12) 0 12px, transparent 12px 24px)' }} />
          <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: 4, transform: `translateX(${head * W}px)`, background: C.danger, boxShadow: `0 0 16px ${C.danger}`, opacity: head < 1 ? 1 : 0 }} />
        </div>
        {SEGS.map((s, i) => <Label key={i} x={x((s.a + s.b) / 2)} y={560} size={28} color={i === 0 ? C.sky : C.text} show={ease(f, capture.from + 10 + i * 20, capture.from + 24 + i * 20)}>{s.text}</Label>)}
        <div style={{ position: 'absolute', left: L, top: 270, display: 'flex', alignItems: 'center', gap: 10, opacity: ease(f, capture.cue('rec') - 4, capture.cue('rec') + 8) }}>
          <div style={{ width: 18, height: 18, borderRadius: 9, background: C.danger, opacity: 0.5 + 0.5 * Math.sin(f / 5) }} />
          <span style={{ fontFamily: SANS, fontSize: 24, fontWeight: 700, color: C.text }}>Recording since the detector fired: nothing lost while the word was checked</span>
        </div>
        <Label x={x(0.47)} y={630} size={24} color={C.teal} show={ease(f, vad.cue('silero'), vad.cue('silero') + 14)}>Silero VAD: speech or silence?</Label>
        {/* the pause: transcribed ahead */}
        <div style={{ position: 'absolute', left: x(0.205), top: 320, transform: 'translateX(-50%)', opacity: shown(f, pause.cue('pause') - 4) }}>
          <div style={{ width: 4, height: 220, background: C.sky, margin: '0 auto', boxShadow: `0 0 12px ${C.sky}` }} />
        </div>
        <div style={{ position: 'absolute', left: x(0.205) - 200, top: 680, width: 400, textAlign: 'center', opacity: shown(f, pause.cue('pause')) }}>
          <Pill size={22} color={C.sky}>pause · 250 ms</Pill>
          <div style={{ marginTop: 14, opacity: ease(f, pause.cue('ahead'), pause.cue('ahead') + 10) }}><Pill mono size={22} bg="rgba(85,210,255,0.14)" color={C.text}>“Lander,” already transcribed</Pill></div>
        </div>
        <div style={{ position: 'absolute', left: x(0.73) - 230, top: 680, width: 460, textAlign: 'center', opacity: endSilence }}>
          <Pill size={22} color={C.accentText} bg={C.accentSoft}>800 ms of silence = the end</Pill>
          <div style={{ fontFamily: SANS, fontSize: 21, color: C.dim, marginTop: 12 }}>the floor of the response time</div>
        </div>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 860, display: 'flex', justifyContent: 'center', gap: 30 }}>
          <div style={{ opacity: shown(f, pause.cue('saves')) }}><Pill size={24}><Fluent name="flash_24_filled" size={24} color={C.accentText} />ended at a pause? that text is used</Pill></div>
          <div style={{ opacity: shown(f, end.cue('five')) }}><Pill size={24}><Fluent name="timer_24_filled" size={24} color={C.text} />nothing said: stop after 5 s</Pill></div>
          <div style={{ opacity: shown(f, end.cue('fifteen')) }}><Pill size={24}><Fluent name="timer_24_filled" size={24} color={C.text} />15 s at most</Pill></div>
        </div>
        <Sfx at={capture.cue('rec') - 4} name="blip-low" volume={0.2} />
        <Sfx at={pause.cue('ahead')} name="pop" volume={0.18} />
        <Sfx at={end.cue('eight') + 24} name="chime" volume={0.18} />
      </Part>

      <Part from={options.from - 4} to={durationInFrames} last>
        <Heading kicker="Which text?" text={f < heard.from - 6 ? 'Choosing the transcription' : 'Handing it over'} at={options.from} y={140} />
        <div style={{ opacity: 1 - ease(f, heard.from - 6, heard.from + 8) }}>
          {[
            { cue: 'after', label: 'After the pause', text: '“what time is it?”', pick: false },
            { cue: 'joined', label: 'Word + order together, word cut out', text: '“Lander, what time is it?” → “what time is it?”', pick: true },
            { cue: 'alone', label: 'The order alone', text: 'used when the other lost 3+ words', pick: false },
          ].map((o, i) => {
            const k = shown(f, options.cue(o.cue) - 4);
            const chosen = o.pick && f > options.to - 20;
            return (
              <div key={o.label} style={{ position: 'absolute', left: 260, top: 300 + i * 150, width: 1400, height: 120, boxSizing: 'border-box', padding: '0 34px', borderRadius: 24, display: 'flex', alignItems: 'center', gap: 26, background: 'rgba(28,28,34,0.94)', border: `2px solid ${chosen ? C.accentText : C.line2}`, boxShadow: chosen ? '0 0 40px rgba(242,193,74,0.35)' : undefined, opacity: Math.min(1, k * 1.5), transform: `translateX(${(1 - k) * -40}px)` }}>
                <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.faint, width: 30 }}>{i + 1}</span>
                <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text, width: 560 }}>{o.label}</span>
                <span style={{ fontFamily: MONO, fontSize: 24, color: chosen ? C.accentText : C.dim }}>{o.text}</span>
              </div>
            );
          })}
          <Gate x={560} y={790} w={800} label="Echo guard again" sub="what an assistant was saying is ignored" state={f > echo.cue('guard') + 20 ? 'pass' : 'wait'} show={shown(f, echo.cue('guard') - 4)} />
        </div>
        {f >= heard.from - 8 && (
          <>
            <Packet from={[300, 600]} to={[1300, 560]} start={heard.cue('heard') - 6} end={heard.cue('heard') + 20} label='heard' />
            <div style={{ position: 'absolute', left: 180, top: 520, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10, opacity: shown(f, heard.from) }}>
              <Fluent name="shield_checkmark_24_filled" size={70} color={C.accentText} />
              <span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.text }}>K-Desktop</span>
            </div>
            <Code x={560} y={320} w={720} at={heard.cue('heard') + 10} every={6} title="K-Desktop → assistant" show={shown(f, heard.cue('heard') + 6)} hi={f > heard.cue('voice') ? [4] : []}
              lines={['{', '  "t": "heard",', '  "assistant": "lander",', '  "text": "what time is it?",', '  "source": "voice"', '}']} />
            <Lander x={1560} y={560} size={260} state={f > heard.cue('heard') + 20 ? 'think' : 'listen'} />
          </>
        )}
        <Sfx at={options.to - 20} name="blip" volume={0.2} />
        <Sfx at={heard.cue('heard') - 6} name="whoosh-short" volume={0.22} />
      </Part>
    </AbsoluteFill>
  );
};
