import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent, KdIcon } from '../components/Icon';
import { Code, Heading, Label, Lander, Packet, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, IN, OUT, pop, tween } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

const Light: React.FC<{ color: string; on: number; label: string; sub: string }> = ({ color, on, label, sub }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 22, marginBottom: 22, opacity: 0.35 + 0.65 * on }}>
    <div style={{ width: 64, height: 64, borderRadius: 32, background: color, boxShadow: on > 0.5 ? `0 0 40px ${color}` : 'none' }} />
    <div><div style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text }}>{label}</div><div style={{ fontFamily: SANS, fontSize: 22, color: C.dim }}>{sub}</div></div>
  </div>
);

/** Chapter: running a command: risk, permission, the steps with the assistant's gestures, the end; and «act». */
export const Run: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const run = B('run');
  const risk = B('risk');
  const ask = B('ask');
  const steps = B('steps');
  const safety = B('safety');
  const end = B('end');
  const act = B('act');
  // The window being minimized, and the Lander flying to its button.
  const fly = ease(f, steps.cue('gesture') - 4, steps.cue('gesture') + 30);
  const impact = steps.cue('impact');
  const shot = ease(f, impact - 14, impact);
  const flash = Math.max(0, 1 - Math.abs(f - impact) / 8);
  const gone = ease(f, steps.cue('does'), steps.cue('does') + 18, IN);
  const winX = 1080;
  const winY = 300;
  const btnX = winX + 560;
  const btnY = winY + 26;
  const lx = tween(f, steps.cue('gesture') - 4, steps.cue('gesture') + 30, 700, btnX - 230);
  const ly = tween(f, steps.cue('gesture') - 4, steps.cue('gesture') + 30, 800, btnY + 150);
  return (
    <AbsoluteFill>
      <Part from={0} to={ask.from - 4} first>
        <Heading kicker="Running a command" text="run, and its risk" at={run.from} y={130} size={56} />
        <Lander x={260} y={420} size={200} />
        <Packet from={[360, 420]} to={[960, 420]} start={run.cue('run') - 6} end={run.cue('run') + 20} label='run · "Empty Recycle Bin" · choreography' size={20} />
        <Panel x={980} y={340} w={460} show={shown(f, run.cue('run') + 16)} icon="shield_checkmark_24_filled" color="gold" title="K-Desktop" sub="works out the risk" />
        <Panel x={160} y={580} w={760} show={shown(f, risk.cue('risk'))} title="The command's actions">
          {[['Open folder', C.success], ['Empty the recycle bin', C.success], ['Close app', C.warning]].map(([a, c]) => (
            <div key={a} style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 10 }}>
              <div style={{ width: 22, height: 22, borderRadius: 11, background: c }} /><span style={{ fontFamily: SANS, fontSize: 26, color: C.text }}>{a}</span>
            </div>
          ))}
          <div style={{ fontFamily: SANS, fontSize: 22, color: C.accentText, marginTop: 14, opacity: ease(f, risk.cue('risk') + 20, risk.cue('risk') + 34) }}>The command = its riskiest action, at least yellow</div>
        </Panel>
        <Panel x={1080} y={580} w={680} show={shown(f, risk.cue('green') - 6)}>
          <Light color={C.success} on={ease(f, risk.cue('green'), risk.cue('green') + 8) - ease(f, risk.cue('yellow'), risk.cue('yellow') + 8)} label="Green" sub="never asks" />
          <Light color={C.warning} on={ease(f, risk.cue('yellow'), risk.cue('yellow') + 8) - ease(f, risk.cue('red'), risk.cue('red') + 8) + ease(f, risk.to - 20, risk.to)} label="Yellow" sub="asks the first time" />
          <Light color={C.danger} on={ease(f, risk.cue('red'), risk.cue('red') + 8) - ease(f, risk.to - 20, risk.to)} label="Red" sub="asks every time" />
        </Panel>
        <Sfx at={run.cue('run') - 6} name="whoosh-short" volume={0.2} />
      </Part>

      <Part from={ask.from - 4} to={steps.from - 4}>
        <Heading kicker="Asking first" text="The permission window" at={ask.from} y={130} size={56} />
        <div style={{ position: 'absolute', left: 560, top: 290, width: 800, borderRadius: 26, background: '#232329', border: `1.5px solid ${C.line2}`, boxShadow: '0 40px 90px rgba(0,0,0,0.6)', padding: 34, boxSizing: 'border-box', transform: `scale(${0.85 + 0.15 * shown(f, ask.cue('window'))})`, opacity: shown(f, ask.cue('window')) }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 18 }}>
            <KdIcon icon="person_24_filled" color="purple" size={64} />
            <div style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text }}>Lander: may I run «Empty Recycle Bin»?</div>
          </div>
          <div style={{ display: 'flex', gap: 16, marginTop: 34 }}>
            {[['Yes', 'yes'], ['No', 'no'], ["Yes, and don't ask again", 'always']].map(([t, cue], i) => (
              <div key={cue} style={{ padding: '16px 26px', borderRadius: 14, fontFamily: SANS, fontSize: 26, fontWeight: 700, background: i === 0 ? '#FFD970' : 'rgba(255,255,255,0.08)', color: i === 0 ? '#2a1d02' : C.text, transform: `scale(${1 + 0.08 * Math.max(0, 1 - Math.abs(f - ask.cue(cue)) / 10)})`, boxShadow: Math.abs(f - ask.cue(cue)) < 10 ? `0 0 30px ${C.accentText}` : 'none' }}>{t}</div>
            ))}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginTop: 30 }}>
            <div style={{ opacity: shown(f, ask.cue('listens')) }}><Pill size={22} color={C.teal} bg="rgba(62,220,193,0.14)"><Fluent name="mic_24_filled" size={22} color={C.teal} />listening for «yes» / «no»</Pill></div>
            <div style={{ marginLeft: 'auto', opacity: shown(f, ask.cue('tensec')) }}><Pill size={22} mono>{Math.max(0, 10 - Math.floor(Math.max(0, f - ask.cue('tensec')) / 9))} s → no</Pill></div>
          </div>
        </div>
        <Label x={960} y={760} size={26} color={C.dim} show={ease(f, ask.cue('window') + 10, ask.cue('window') + 24)}>shown without stealing your focus</Label>
        <Sfx at={ask.cue('window')} name="pop" volume={0.22} />
      </Part>

      <Part from={steps.from - 4} to={end.from - 4}>
        <Heading kicker="With choreography" text={f < safety.from - 6 ? 'Step, gesture, go' : 'It never hangs'} at={steps.from} y={120} size={54} />
        <Code x={100} y={260} w={760} at={steps.cue('step')} every={3} size={20} title="K-Desktop → assistant" show={shown(f, steps.cue('step') - 4)} hi={f > steps.cue('go') ? [] : [3]}
          lines={['{ "t": "step", "run": "r7", "index": 1,', '  "kind": "Window", "op": "Minimize",', '  "summary": "Minimize: Chrome",', '  "target": { "hwnd": 655360, "process": "chrome",', '    "rect": [1080, 300, 640, 420] },', '  "timeoutMs": 6000 }']} />
        {/* the window */}
        <div style={{ position: 'absolute', left: winX, top: winY, width: 640, height: 420, borderRadius: 14, background: '#202027', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)', transformOrigin: '50% 140%', transform: `scale(${1 - 0.9 * gone}) translateY(${gone * 300}px)`, opacity: 1 - gone }}>
          <div style={{ height: 52, borderRadius: '14px 14px 0 0', background: '#2b2b33', display: 'flex', alignItems: 'center', padding: '0 18px', gap: 12 }}>
            <KdIcon icon="globe_24_filled" color="blue" size={30} /><span style={{ fontFamily: SANS, fontSize: 20, color: C.text }}>Chrome</span>
            <div style={{ marginLeft: 'auto', display: 'flex', gap: 26 }}>
              <Fluent name="subtract_24_filled" size={24} color={flash > 0.1 ? C.accentText : C.text} />
              <Fluent name="maximize_24_regular" size={22} color={C.text} />
              <Fluent name="dismiss_24_regular" size={22} color={C.text} />
            </div>
          </div>
          <div style={{ padding: 30, display: 'flex', flexDirection: 'column', gap: 14 }}>{[0.9, 0.7, 0.8, 0.5].map((w, i) => <div key={i} style={{ height: 18, width: `${w * 100}%`, borderRadius: 9, background: 'rgba(255,255,255,0.07)' }} />)}</div>
        </div>
        {/* the shot */}
        <svg style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }} width={1} height={1}>
          <line x1={lx + 40} y1={ly - 60} x2={lx + 40 + (btnX - lx - 40) * shot} y2={ly - 60 + (btnY - ly + 60) * shot} stroke={C.gold1} strokeWidth={6} opacity={shot > 0 && f < impact + 4 ? 1 : 0} style={{ filter: `drop-shadow(0 0 8px ${C.gold2})` }} />
        </svg>
        <div style={{ position: 'absolute', left: btnX - 60, top: btnY - 60, width: 120, height: 120, borderRadius: 60, background: `radial-gradient(circle, rgba(255,230,160,${flash}) 0%, transparent 70%)` }} />
        <Lander x={lx} y={ly} size={180} tilt={fly * -12} show={shown(f, steps.from)} />
        <Packet from={[lx + 60, ly - 30]} to={[600, 800]} start={steps.cue('go')} end={steps.cue('go') + 20} label='go' />
        <Panel x={160} y={720} w={460} show={shown(f, steps.cue('does') - 10)} icon="shield_checkmark_24_filled" color="gold" title="K-Desktop does it" sub="it has the rights" />
        {/* safety */}
        <div style={{ position: 'absolute', left: 1080, top: 790, width: 700, display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div style={{ opacity: shown(f, safety.cue('six')) }}><Pill size={22}><Fluent name="timer_24_filled" size={22} color={C.text} />no answer in 6 s (10 s to tow a window): done anyway</Pill></div>
          <div style={{ opacity: shown(f, safety.cue('verify')) }}><Pill size={22} bg={C.accentSoft} color={C.accentText}><Fluent name="checkmark_24_filled" size={22} color={C.accentText} />«done» by the assistant → checked, exact result set</Pill></div>
        </div>
        <Sfx at={steps.cue('gesture') - 4} name="whoosh" volume={0.25} />
        <Sfx at={impact} name="impact" volume={0.35} />
        <Sfx at={steps.cue('does')} name="whoosh-down" volume={0.2} />
      </Part>

      <Part from={end.from - 4} to={durationInFrames} last>
        <Heading kicker="Over" text={f < act.from - 6 ? 'end, and the history' : 'act: a throwaway command'} at={end.from} y={130} size={56} />
        <div style={{ opacity: 1 - ease(f, act.from - 8, act.from + 4) }}>
          <Code x={200} y={300} w={720} at={end.cue('end')} every={3} size={22} title="K-Desktop → assistant" show={shown(f, end.cue('end') - 4)}
            lines={['{ "t": "end", "run": "r7",', '  "status": "ok",', '  "messages": [] }']} />
          <Panel x={1000} y={300} w={720} show={shown(f, end.cue('history'))} icon="history_24_filled" color="indigo" title="K-Desktop · History">
            <div style={{ display: 'flex', alignItems: 'center', gap: 14, fontFamily: SANS, fontSize: 24, color: C.text }}>
              <Fluent name="checkmark_circle_24_filled" size={26} color={C.success} />Empty Recycle Bin
              <span style={{ marginLeft: 'auto' }}><Pill size={20} color={C.violet} bg="rgba(188,140,255,0.14)">Assistant · Lander</Pill></span>
            </div>
          </Panel>
        </div>
        {f >= act.from - 8 && (
          <>
            <div style={{ position: 'absolute', left: 0, right: 0, top: 300, textAlign: 'center', opacity: shown(f, act.from) }}><Pill size={40} mono>“minimize Chrome”</Pill></div>
            <Code x={200} y={420} w={700} at={act.cue('act')} every={3} size={22} title="assistant → K-Desktop" show={shown(f, act.cue('act') - 4)}
              lines={['{ "t": "act", "run": "r8",', '  "op": "minimize", "app": "Chrome",', '  "choreography": true }']} />
            <Panel x={1000} y={420} w={720} show={shown(f, act.cue('disposable'))} lit={0.6} icon="flash_24_filled" color="gold" title="A throwaway command" sub="from K-Desktop's own catalog of actions">
              <div style={{ fontFamily: MONO, fontSize: 24, color: C.text }}>Window → Minimize · «chrome»</div>
              <div style={{ display: 'flex', gap: 10, marginTop: 18, flexWrap: 'wrap' }}>{['asks like a command', 'in the history', 'same gestures'].map(t => <Pill key={t} size={20}>{t}</Pill>)}</div>
            </Panel>
          </>
        )}
        <Sfx at={end.cue('end') - 4} name="blip" volume={0.2} />
        <Sfx at={act.cue('disposable')} name="pop" volume={0.2} />
      </Part>
    </AbsoluteFill>
  );
};
