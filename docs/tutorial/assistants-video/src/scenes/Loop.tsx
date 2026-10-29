import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent, KdIcon } from '../components/Icon';
import { Code, Gate, Heading, Item, Label, Packet, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** Chapter: the model and the tool runner, round after round, and the checks around them. */
export const Loop: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const round = B('round');
  const runner = B('runner');
  const checks = B('checks');
  const result = B('result');
  const limits = B('limits');
  const nudge = B('nudge');
  const claims = B('claims');
  const think = B('think');
  const streamed = 'Let me set that for you.'.slice(0, Math.max(0, Math.floor((f - round.cue('stream')) / 1.6)));
  const roundsDone = f < limits.cue('three') ? 0 : Math.min(3, 1 + Math.floor((f - limits.cue('three')) / 14));
  const cut = ease(f, claims.cue('cut') - 4, claims.cue('cut') + 12);
  return (
    <AbsoluteFill>
      <Part from={0} to={checks.from - 4} first>
        <Heading kicker="The loop" text="Model ⇄ tool runner" at={round.from} y={120} size={56} />
        <Panel x={100} y={250} w={760} show={shown(f, round.from)} icon="brain_circuit_24_filled" color="purple" title="The model" sub="the reply streams in">
          <div style={{ fontFamily: SANS, fontSize: 26, color: C.text, minHeight: 40, opacity: 1 - ease(f, round.cue('calls') - 4, round.cue('calls') + 6) }}>{streamed}<span style={{ opacity: Math.sin(f / 3) > 0 ? 1 : 0, color: C.accentText }}>▍</span></div>
          <div style={{ display: 'flex', gap: 12, marginTop: 10 }}>
            <div style={{ opacity: shown(f, round.cue('text')) }}><Pill size={22}>text → done</Pill></div>
            <div style={{ opacity: shown(f, round.cue('calls')) }}><Pill size={22} bg={C.accentSoft} color={C.accentText}>or tool calls</Pill></div>
          </div>
        </Panel>
        <Code x={100} y={560} w={760} at={round.cue('calls')} every={4} size={23} title="tool call" show={shown(f, round.cue('calls'))}
          lines={['{ "name": "set_timer",', '  "arguments": { "minutes": 10,', '                 "reason": "tea" } }']} />
        <Packet from={[860, 640]} to={[1060, 400]} start={runner.cue('runner') - 8} end={runner.cue('runner') + 12} label="set_timer" />
        <Panel x={1060} y={250} w={760} show={shown(f, runner.cue('runner'))} lit={0.7} icon="wrench_24_filled" color="gold" title="Tool runner">
          <Item at={runner.cue('exists')} icon="checkmark_circle_24_filled" color={C.success}>It exists, and is switched on</Item>
          <Item at={runner.cue('intent')} icon="text_quote_24_filled">In words: «set a 10 min timer: tea»</Item>
          <Item at={runner.cue('risk')} icon="shield_24_filled">Its risk, with your preferences</Item>
          <Item at={runner.cue('ask')} icon="question_circle_24_filled" color={C.warning}>Asks you, if it has to (10 s)</Item>
          <Item at={runner.to - 16} icon="play_24_filled" color={C.success}>Runs it</Item>
        </Panel>
        <Sfx at={round.cue('calls')} name="pop" volume={0.2} />
        <Sfx at={runner.cue('runner') - 8} name="whoosh-short" volume={0.2} />
      </Part>

      <Part from={checks.from - 4} to={limits.from - 4}>
        <Heading kicker="Against making things up" text={f < result.from - 6 ? 'Checks on every call' : 'The result goes back'} at={checks.from} y={120} size={54} />
        <div style={{ opacity: 1 - ease(f, result.from - 8, result.from + 4) }}>
          <Gate x={120} y={290} w={1680} label="A web address must come from what was said, or from what the tools said"
            sub="open_web «www.some-site-i-made-up.com» → not in the order, nor in any tool's result: not done" state={f > checks.cue('sources') + 30 ? 'fail' : 'wait'} show={shown(f, checks.cue('sources') - 4)} />
          <Gate x={120} y={460} w={1680} label="To run your command, your order must really ask for it"
            sub="«the Track Series tab» only sounds like your command «TrackIR 5»: not done (it must name it, or match it at 0.70)" state={f > checks.cue('really') + 30 ? 'fail' : 'wait'} show={shown(f, checks.cue('really') - 4)} />
          <Label x={960} y={680} size={26} color={C.dim} show={ease(f, checks.cue('really') + 40, checks.cue('really') + 56)}>The model is told why, and can do it right, or say it could not</Label>
        </div>
        {f >= result.from - 8 && (
          <>
            <Code x={120} y={280} w={820} at={result.cue('msg')} every={4} size={22} title="back to the model" show={shown(f, result.cue('msg') - 4)}
              lines={['{ "role": "tool", "name": "set_timer",', '  "content": "Timer set for 17:15 (tea)." }']} />
            <Panel x={1020} y={280} w={780} show={shown(f, result.cue('activity'))} icon="history_24_filled" color="indigo" title="Activity">
              <div style={{ display: 'flex', alignItems: 'center', gap: 14, fontFamily: SANS, fontSize: 24, color: C.text }}>
                <Fluent name="checkmark_24_filled" size={24} color={C.success} />Timer for 17:15 · tea
                <span style={{ marginLeft: 'auto', opacity: ease(f, result.cue('keep'), result.cue('keep') + 10) }}><Pill size={20} bg={C.accentSoft} color={C.accentText}><Fluent name="arrow_undo_24_filled" size={20} color={C.accentText} />undo kept · survives restarts</Pill></span>
              </div>
            </Panel>
          </>
        )}
        <Sfx at={checks.cue('sources') + 30} name="pop-low" volume={0.2} />
        <Sfx at={checks.cue('really') + 30} name="pop-low" volume={0.2} />
      </Part>

      <Part from={limits.from - 4} to={claims.from - 4}>
        <Heading kicker="Round after round" text="Limits and nudges" at={limits.from} y={120} size={56} />
        <div style={{ position: 'absolute', left: 0, right: 0, top: 280, display: 'flex', justifyContent: 'center', gap: 30 }}>
          {[1, 2, 3].map(r => (
            <div key={r} style={{ width: 200, height: 140, borderRadius: 24, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', background: r <= roundsDone ? C.accentSoft : 'rgba(28,28,34,0.94)', border: `2px solid ${r <= roundsDone ? C.accentText : C.line2}`, opacity: shown(f, limits.from + r * 4) }}>
              <span style={{ fontFamily: SANS, fontSize: 22, color: C.dim }}>round</span><span style={{ fontFamily: SANS, fontSize: 54, fontWeight: 800, color: r <= roundsDone ? C.accentText : C.text }}>{r}</span>
            </div>
          ))}
          <div style={{ width: 360, height: 140, borderRadius: 24, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', background: 'rgba(188,140,255,0.15)', border: `2px solid ${C.violet}`, opacity: shown(f, limits.cue('five')) }}>
              <span style={{ fontFamily: SANS, fontSize: 22, color: C.dim }}>a big model</span><span style={{ fontFamily: SANS, fontSize: 40, fontWeight: 800, color: C.violet }}>5 rounds · 8 calls</span>
          </div>
        </div>
        <div style={{ position: 'absolute', left: 260, top: 500, width: 1400 }}>
          <Item at={limits.cue('failed')} icon="dismiss_circle_24_filled" color={C.danger}>The same call that already failed: not run again</Item>
          <Item at={limits.cue('failed') + 16} icon="dismiss_circle_24_filled" color={C.danger}>The same call that already worked: not repeated</Item>
          <Item at={limits.cue('answer')} icon="chat_24_filled" color={C.accentText}>Limit reached: «answer now», with no tools offered</Item>
          <Item at={nudge.cue('named')} icon="arrow_redo_24_filled" color={C.teal}>Named your command without running it → one more round</Item>
          <Item at={nudge.cue('nudge') - 10} icon="arrow_redo_24_filled" color={C.teal}>Said it would send something, sent nothing → one more round</Item>
        </div>
        <Sfx at={limits.cue('answer')} name="blip" volume={0.2} />
      </Part>

      <Part from={claims.from - 4} to={durationInFrames} last>
        <Heading kicker="Before it is said" text={f < think.from - 6 ? 'What it claims is checked' : 'Stuck, or too long'} at={claims.from} y={120} size={54} />
        <div style={{ opacity: 1 - ease(f, think.from - 8, think.from + 4) }}>
          <Panel x={160} y={280} w={1600} show={shown(f, claims.from)} icon="chat_24_filled" color="purple" title="The model's answer" sub="tools that worked in this order: set_timer">
            <div style={{ fontFamily: SANS, fontSize: 32, lineHeight: 1.5, color: C.text }}>
              <span style={{ position: 'relative', opacity: 1 - 0.6 * cut }}>I've sent the list to your Telegram.
                <span style={{ position: 'absolute', left: 0, right: 0, top: '52%', height: 4, background: C.danger, transformOrigin: 'left', transform: `scaleX(${cut})` }} />
              </span>{' '}
              <span>Your tea timer is set.</span>
            </div>
            <div style={{ fontFamily: SANS, fontSize: 22, color: C.danger, marginTop: 10, opacity: cut }}>«sent» — but no tool of the «send» kind worked: removed</div>
          </Panel>
          <div style={{ position: 'absolute', left: 160, top: 640, width: 1600, display: 'flex', alignItems: 'center', gap: 20, padding: 26, boxSizing: 'border-box', borderRadius: 24, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.accentText}`, opacity: shown(f, claims.cue('exact') - 20) }}>
            <Fluent name="wrench_24_filled" size={40} color={C.accentText} />
            <span style={{ fontFamily: SANS, fontSize: 26, color: C.dim }}>The tool's own words, exactly:</span>
            <span style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text }}>«I'll remind you at 5:15.»</span>
          </div>
        </div>
        {f >= think.from - 8 && (
          <>
            <Panel x={160} y={300} w={760} show={shown(f, think.cue('again'))} icon="lightbulb_24_filled" color="yellow" title="Stuck, or silent?" sub="it thinks it over once, with more time">
              <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim }}>reasoning on (or the model set for thinking), up to 45 s</div>
            </Panel>
            <Panel x={1000} y={300} w={760} show={shown(f, think.cue('long'))} icon="document_text_24_filled" color="blue" title="Too long for a bubble?" sub="written on a reading sheet">
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>{[1, 0.8, 0.9, 0.6].map((w, i) => <div key={i} style={{ height: 14, width: `${w * 100}%`, borderRadius: 7, background: 'rgba(255,255,255,0.1)' }} />)}</div>
              <div style={{ fontFamily: SANS, fontSize: 22, color: C.accentText, marginTop: 16 }}>only the summary is spoken</div>
            </Panel>
          </>
        )}
        <Sfx at={claims.cue('cut')} name="pop-low" volume={0.22} />
        <Sfx at={claims.cue('exact')} name="chime" volume={0.18} />
      </Part>
    </AbsoluteFill>
  );
};
