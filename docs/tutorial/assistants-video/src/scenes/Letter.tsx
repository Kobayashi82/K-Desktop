import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent, KdIcon } from '../components/Icon';
import { Code, Heading, Label, Panel, Pill, Stat, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

const OPTIONS = ['Empty Recycle Bin', 'Open Recycle Bin', 'Clean temp files', 'Open Downloads', 'Game mode'];
const PROBS = [0.94, 0.03, 0.01, 0.0, 0.0, 0.02];

/** Chapter: when nothing is clear, the model picks a command with one letter; then the programs. */
export const Letter: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const intro = B('intro');
  const options = B('options');
  const prompt = B('prompt');
  const probs = B('probs');
  const fast = B('fast');
  const programs = B('programs');
  const letters = [...'ABCDE', 'Z'];
  const grow = ease(f, probs.cue('probs'), probs.cue('probs') + 30);
  return (
    <AbsoluteFill>
      <Part from={0} to={fast.from - 4} first>
        <Heading kicker="A shortcut before the AI" text="Deciding with one letter" at={intro.from} y={130} size={56} />
        <div style={{ position: 'absolute', left: 0, right: 0, top: 250, textAlign: 'center', opacity: shown(f, intro.cue('letter')) }}><Pill size={34}>“get rid of the junk in the bin”</Pill></div>
        <div style={{ position: 'absolute', left: 120, top: 330, display: 'flex', gap: 14, opacity: shown(f, options.cue('near')) }}>
          <Pill size={22} color={C.teal}>the 10 closest by meaning</Pill>
          <Pill size={22} style={{ opacity: ease(f, options.cue('ten') + 10, options.cue('ten') + 20) }}>best ≥ 0.55</Pill>
          <Pill size={22} style={{ opacity: ease(f, options.cue('words'), options.cue('words') + 10) }}>≤ 10 words</Pill>
        </div>
        <Code x={120} y={420} w={820} at={prompt.from} every={4} size={22} title="to the assistant's own model" show={shown(f, prompt.from - 4)} hi={f > prompt.cue('one') ? [9] : []}
          lines={['Order: «get rid of the junk in the bin»', 'Which of the user\'s commands does it ask to run?', '', ...OPTIONS.map((o, i) => `${'ABCDE'[i]}) ${o}`), 'Z) None of these', 'Answer with the letter.']} />
        {/* probabilities of the first token */}
        <Panel x={1000} y={420} w={800} show={shown(f, probs.cue('probs') - 4)} icon="data_bar_horizontal_24_filled" color="indigo" title="Probability of each letter" sub="its answer is not read: its odds are">
          {letters.map((l, i) => {
            const p = PROBS[i] * grow;
            const top = i === 0 && f > probs.cue('ninety');
            return (
              <div key={l} style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 12 }}>
                <span style={{ fontFamily: MONO, fontSize: 28, fontWeight: 600, width: 30, color: top ? C.success : C.text }}>{l}</span>
                <div style={{ flex: 1, height: 22, borderRadius: 11, background: 'rgba(255,255,255,0.07)', position: 'relative' }}>
                  <div style={{ width: `${Math.max(1, p * 100)}%`, height: '100%', borderRadius: 11, background: top ? C.success : C.sky }} />
                  {i === 0 && <div style={{ position: 'absolute', left: '90%', top: -6, bottom: -6, width: 3, background: C.accentText, opacity: ease(f, probs.cue('ninety') - 6, probs.cue('ninety')) }} />}
                </div>
                <span style={{ fontFamily: MONO, fontSize: 22, color: C.dim, width: 64, textAlign: 'right' }}>{p.toFixed(2)}</span>
              </div>
            );
          })}
        </Panel>
        <div style={{ position: 'absolute', left: 1000, top: 900, width: 800, display: 'flex', gap: 14, flexWrap: 'wrap' }}>
          <div style={{ opacity: shown(f, probs.cue('ninety')) }}><Pill size={21} bg="rgba(108,203,95,0.16)" color={C.success}>≥ 0.90 → run it</Pill></div>
          <div style={{ opacity: shown(f, probs.cue('ask')) }}><Pill size={21} bg="rgba(252,179,63,0.16)" color={C.warning}>two reach 0.90 → ask</Pill></div>
          <div style={{ opacity: shown(f, probs.cue('ai')) }}><Pill size={21} bg="rgba(188,140,255,0.16)" color={C.violet}>Z or unclear → the AI</Pill></div>
        </div>
        <Sfx at={probs.cue('ninety')} name="chime" volume={0.2} />
      </Part>

      <Part from={fast.from - 4} to={programs.from - 4}>
        <Heading kicker="Measured" text="Cheap and right" at={fast.from} y={150} />
        <Stat x={520} y={520} value="0.1 s" unit="instead of a whole answer" show={shown(f, fast.cue('tenth'))} />
        <Stat x={1100} y={520} value={<span>98 <span style={{ color: C.faint }}>→</span> 123</span>} unit="of 184 orders right without the AI" show={shown(f, fast.cue('more'))} color={C.success} />
        <Label x={960} y={780} size={28} color={C.dim} show={ease(f, fast.cue('more') + 10, fast.cue('more') + 24)}>117 real commands · never shuts down the PC from here</Label>
        <div style={{ position: 'absolute', left: 1500, top: 470, opacity: shown(f, fast.to - 40) }}><KdIcon icon="power_24_filled" color="red" size={90} /></div>
        <Sfx at={fast.cue('more')} name="pop" volume={0.2} />
      </Part>

      <Part from={programs.from - 4} to={durationInFrames} last>
        <Heading kicker="Layer 3" text="Programs" at={programs.from} y={150} />
        {[
          { cue: 'minimize', said: 'minimize Chrome', op: 'minimize', icon: 'arrow_minimize_24_filled' },
          { cue: 'quit', said: 'quit Spotify', op: 'quit', icon: 'dismiss_24_filled' },
          { cue: 'open', said: 'open Steam', op: 'open (if installed)', icon: 'open_24_filled' },
        ].map((p, i) => {
          const k = shown(f, programs.cue(p.cue));
          return (
            <div key={p.cue} style={{ position: 'absolute', left: 300, top: 320 + i * 150, width: 1320, height: 120, boxSizing: 'border-box', padding: '0 30px', display: 'flex', alignItems: 'center', gap: 24, borderRadius: 24, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateX(${(1 - k) * -40}px)` }}>
              <Fluent name={p.icon} size={44} color={C.accentText} />
              <span style={{ fontFamily: MONO, fontSize: 34, color: C.text, width: 480 }}>“{p.said}”</span>
              <Fluent name="arrow_right_24_filled" size={34} color={C.faint} />
              <Pill size={26} color={C.teal} bg="rgba(62,220,193,0.14)">rule: {p.op}</Pill>
            </div>
          );
        })}
        <Label x={960} y={820} size={28} color={C.dim} show={ease(f, programs.to - 50, programs.to - 34, OUT)}>K-Desktop looks among the open windows and the running processes</Label>
      </Part>
    </AbsoluteFill>
  );
};
