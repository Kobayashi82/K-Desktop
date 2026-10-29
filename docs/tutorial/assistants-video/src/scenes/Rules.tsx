import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Bubble, Heading, Label, Lander, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT, pop } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** Chapter: the fixed rules, answered at once, and the cases that skip them. */
export const Rules: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const what = B('what');
  const clean = B('clean');
  const list = B('list');
  const tool = B('tool');
  const later = B('later');
  const json = B('json');
  const wrong = B('wrong');
  // «Lander, could you please set a timer for ten minutes»: what is taken off, and when.
  const words: Array<[string, string | null]> = [
    ['Lander,', 'name'], ['could', 'polite'], ['you', 'polite'], ['please', 'filler'], ['set', null], ['a', null], ['timer', null], ['for', null], ['ten', null], ['minutes', null],
  ];
  const off = (kind: string | null) => (kind ? ease(f, clean.cue(kind), clean.cue(kind) + 12) : 0);
  const fits = f > clean.to - 24;
  const rules = [
    { cue: 'time', icon: 'clock_24_filled', t: 'What time is it?' },
    { cue: 'timer', icon: 'timer_24_filled', t: 'A timer for {dur}' },
    { cue: 'note', icon: 'note_24_filled', t: 'Note down {text}' },
    { cue: 'cancel', icon: 'dismiss_circle_24_filled', t: 'Cancel' },
    { cue: 'repeat', icon: 'arrow_repeat_all_24_filled', t: 'Repeat that' },
    { cue: 'undo', icon: 'arrow_undo_24_filled', t: 'Undo' },
    { cue: 'remember', icon: 'bookmark_24_filled', t: 'Remember that {text}' },
    { cue: 'weather', icon: 'weather_partly_cloudy_day_24_filled', t: 'The weather' },
    { cue: 'dictate', icon: 'pen_24_filled', t: 'Write what I say' },
    { cue: 'free', icon: 'brain_circuit_24_filled', t: 'Free your brain' },
    { cue: 'think', icon: 'lightbulb_24_filled', t: 'Think it over' },
  ];
  return (
    <AbsoluteFill>
      <Part from={0} to={list.from - 4} first>
        <Heading kicker="Layer 1" text="Patterns, with gaps" at={what.from} y={140} />
        <Panel x={300} y={290} w={1320} show={shown(f, what.cue('patterns') - 4)} icon="document_text_24_filled" color="green" title="assistant.rule.timer" sub="in the language files, for every installed language">
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, fontFamily: MONO, fontSize: 36, color: C.text, flexWrap: 'wrap' }}>
            <span>set a timer for</span>
            <Pill mono size={32} bg="rgba(62,220,193,0.18)" color={C.teal} style={{ opacity: ease(f, what.cue('gaps'), what.cue('gaps') + 10) }}>{'{dur}'}</Pill>
            <Pill mono size={32} bg="rgba(255,255,255,0.08)" color={C.dim} style={{ opacity: ease(f, what.to - 30, what.to - 16) }}>[please]</Pill>
            <span style={{ color: C.faint }}>|</span>
            <span>a</span>
            <Pill mono size={32} bg="rgba(62,220,193,0.18)" color={C.teal} style={{ opacity: ease(f, what.cue('gaps'), what.cue('gaps') + 10) }}>{'{dur}'}</Pill>
            <span>timer</span>
          </div>
        </Panel>
        {/* The order, cleaned word by word */}
        <div style={{ position: 'absolute', left: 0, right: 0, top: 620, display: 'flex', justifyContent: 'center', gap: 16, opacity: shown(f, clean.from) }}>
          {words.map(([w, kind], i) => {
            const k = off(kind);
            return (
              <span key={i} style={{ position: 'relative', fontFamily: SANS, fontSize: 44, fontWeight: 800, color: kind ? `rgba(255,255,255,${1 - 0.7 * k})` : fits ? C.accentText : C.text }}>
                {w}
                {kind && <span style={{ position: 'absolute', left: -4, right: -4, top: '52%', height: 4, background: C.danger, transformOrigin: 'left', transform: `scaleX(${k})` }} />}
                {kind && <span style={{ position: 'absolute', left: '50%', top: 66, transform: 'translateX(-50%)', fontFamily: SANS, fontSize: 18, fontWeight: 700, color: C.danger, opacity: k, whiteSpace: 'nowrap' }}>{kind}</span>}
              </span>
            );
          })}
        </div>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 820, display: 'flex', justifyContent: 'center', gap: 24, alignItems: 'center', opacity: fits ? pop(f, clean.to - 24) : 0 }}>
          <Fluent name="checkmark_circle_24_filled" size={44} color={C.success} />
          <span style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.success }}>The whole order fits:</span>
          <Pill mono size={30} bg="rgba(62,220,193,0.18)" color={C.teal}>{'{dur}'} = 600 s</Pill>
        </div>
        <Sfx at={clean.cue('name')} name="pop-low" volume={0.18} />
        <Sfx at={clean.cue('filler')} name="pop-low" volume={0.18} />
        <Sfx at={clean.cue('polite')} name="pop-low" volume={0.18} />
        <Sfx at={clean.to - 24} name="chime" volume={0.2} />
      </Part>

      <Part from={list.from - 4} to={tool.from - 4}>
        <Heading kicker="Answered at once, no AI" text="The fixed rules" at={list.from} y={140} />
        {rules.map((r, i) => {
          const x = 160 + (i % 4) * 410;
          const y = 300 + Math.floor(i / 4) * 210;
          const k = shown(f, list.cue(r.cue) - 2);
          return (
            <div key={r.cue} style={{ position: 'absolute', left: x, top: y, width: 380, height: 170, boxSizing: 'border-box', padding: 22, borderRadius: 24, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `scale(${0.7 + 0.3 * k})` }}>
              <KdIcon icon={r.icon} color={['green', 'teal', 'gold', 'red', 'sky', 'orange', 'pink', 'blue', 'indigo', 'purple', 'yellow'][i]} size={58} />
              <div style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.text, marginTop: 16 }}>{r.t}</div>
            </div>
          );
        })}
        {rules.map((r, i) => <Sfx key={i} at={list.cue(r.cue) - 2} name="pop" volume={0.12} />)}
      </Part>

      <Part from={tool.from - 4} to={later.from - 4}>
        <Heading kicker="But never on their own" text="A rule calls the same tool" at={tool.from} y={140} />
        <Panel x={120} y={380} w={380} show={shown(f, tool.from)} icon="flash_24_filled" color="green" title="Rule: timer" sub="{dur} = 600 s" />
        <Wire from={[500, 450]} to={[620, 450]} draw={ease(f, tool.cue('same') - 6, tool.cue('same') + 6)} flow={1} />
        <Panel x={620} y={360} w={460} show={shown(f, tool.cue('runner') - 4)} lit={1} icon="wrench_24_filled" color="gold" title="Tool runner" sub={<span style={{ fontFamily: MONO }}>set_timer {'{ "minutes": 10 }'}</span>} />
        <Wire from={[1080, 450]} to={[1200, 450]} draw={ease(f, tool.cue('activity') - 6, tool.cue('activity') + 6)} flow={1} />
        <Panel x={1200} y={330} w={600} show={shown(f, tool.cue('activity') - 4)} icon="history_24_filled" color="indigo" title="Activity">
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, fontFamily: SANS, fontSize: 22, color: C.text }}>
            <Fluent name="checkmark_24_filled" size={24} color={C.success} />Timer for 17:42
            <span style={{ marginLeft: 'auto', opacity: ease(f, tool.cue('undoit'), tool.cue('undoit') + 10) }}><Pill size={20} bg={C.accentSoft} color={C.accentText}><Fluent name="arrow_undo_24_filled" size={20} color={C.accentText} />Undo</Pill></span>
          </div>
        </Panel>
        <Lander x={560} y={790} size={200} state={f > tool.cue('phrases') ? 'speak' : 'idle'} show={shown(f, tool.cue('phrases') - 10)} />
        <Bubble text="Copy that. I'll ring at 17:42." show={pop(f, tool.cue('phrases'))} x={690} y={760} size={34} />
        <Sfx at={tool.cue('phrases')} name="blip" volume={0.2} />
      </Part>

      <Part from={later.from - 4} to={durationInFrames} last>
        <Heading kicker="Before the commands" text="Three special cases" at={later.from} y={140} />
        {[
          { at: later.from, icon: 'clock_alarm_24_filled', color: 'orange', said: '«At seven, empty the recycle bin»', then: 'for later → the AI, with the schedule tool', tc: C.violet },
          { at: json.from, icon: 'braces_24_filled', color: 'gold', said: '«Good night»', then: 'the exact phrase of a JSON tool → that tool, no AI', tc: C.accentText },
          { at: wrong.from, icon: 'arrow_counterclockwise_24_filled', color: 'red', said: '«You got that wrong»', then: 'right after the AI answered → it thinks it over', tc: C.violet },
        ].map((c, i) => {
          const k = shown(f, c.at);
          return (
            <div key={i} style={{ position: 'absolute', left: 200, top: 300 + i * 200, width: 1520, height: 160, boxSizing: 'border-box', padding: '0 34px', borderRadius: 26, display: 'flex', alignItems: 'center', gap: 28, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 30}px)` }}>
              <KdIcon icon={c.icon} color={c.color} size={78} />
              <span style={{ fontFamily: SANS, fontSize: 34, fontWeight: 800, color: C.text, width: 640 }}>{c.said}</span>
              <Fluent name="arrow_right_24_filled" size={38} color={C.faint} />
              <span style={{ fontFamily: SANS, fontSize: 27, fontWeight: 700, color: c.tc }}>{c.then}</span>
            </div>
          );
        })}
        <Label x={960} y={940} size={24} color={C.dim} show={ease(f, later.cue('schedule'), later.cue('schedule') + 14, OUT)}>Running it now would be wrong.</Label>
      </Part>
    </AbsoluteFill>
  );
};
