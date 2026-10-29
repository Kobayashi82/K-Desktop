import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent, KdIcon } from '../components/Icon';
import { Code, Heading, Item, Label, Lander, Packet, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** Chapter: assistants and K-Desktop's commands and events, both ways; scheduled orders. */
export const Events: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const two = B('two');
  const a2k = B('a2k');
  const schedule = B('schedule');
  const fire = B('fire');
  const k2a = B('k2a');
  const observed = B('observed');
  const clock = ease(f, fire.from - 10, fire.cue('heard'));
  return (
    <AbsoluteFill>
      <Part from={0} to={schedule.from - 4} first>
        <Heading kicker="Both ways" text="Assistants → your events" at={two.from} y={120} size={54} />
        <Lander x={250} y={420} size={200} show={shown(f, two.cue('both'))} />
        <Panel x={1440} y={330} w={400} show={shown(f, two.cue('both') + 6)} icon="shield_checkmark_24_filled" color="gold" title="K-Desktop" />
        <Packet from={[360, 400]} to={[1440, 390]} start={a2k.cue('runevent') - 6} end={a2k.cue('runevent') + 22} label='runEvent · "Gaming"' size={20} />
        <Packet from={[360, 440]} to={[1440, 430]} start={a2k.cue('create') - 6} end={a2k.cue('create') + 22} label="makeEvent" size={20} arc={80} />
        <Code x={160} y={560} w={820} at={a2k.cue('create') + 10} every={4} size={21} title="create_event" show={shown(f, a2k.cue('create') + 6)}
          lines={['{ "when": "program_opens",', '  "programs": ["steam"],', '  "commands": ["Game mode"],', '  "onlyFrom": "18:00", "onlyTo": "23:59" }']} />
        <Panel x={1060} y={560} w={760} show={shown(f, a2k.cue('saves'))} icon="calendar_clock_24_filled" color="orange" title="Events" sub="saved at once">
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, fontFamily: SANS, fontSize: 23, color: C.text }}>
            When Steam opens → Game mode
            <span style={{ marginLeft: 'auto' }}><Pill size={18} color={C.violet} bg="rgba(188,140,255,0.14)">made by Lander</Pill></span>
          </div>
          <div style={{ marginTop: 16, opacity: ease(f, a2k.to - 30, a2k.to - 14) }}><Pill size={20} bg={C.accentSoft} color={C.accentText}><Fluent name="arrow_undo_24_filled" size={20} color={C.accentText} />«undo» removes it</Pill></div>
        </Panel>
        <Label x={610} y={880} size={22} color={C.dim} show={ease(f, a2k.cue('create') + 30, a2k.cue('create') + 44)}>at a time · a program opens · idle · headphones · a file · the battery · sunset…</Label>
      </Part>

      <Part from={schedule.from - 4} to={k2a.from - 4}>
        <Heading kicker="Orders for later" text="Scheduled orders" at={schedule.from} y={120} size={54} />
        <div style={{ position: 'absolute', left: 0, right: 0, top: 260, textAlign: 'center', opacity: shown(f, schedule.cue('seven') - 4) }}><Pill size={34}>“At seven, check the weather and send it to me”</Pill></div>
        <Panel x={160} y={380} w={900} show={shown(f, schedule.cue('askasst') - 10)} icon="clock_alarm_24_filled" color="orange" title="Event: at 19:00, once" sub="action: ask an assistant">
          <div style={{ fontFamily: MONO, fontSize: 23, color: C.text }}>«check the weather and send it to me»</div>
          <div style={{ display: 'flex', gap: 10, marginTop: 16, opacity: ease(f, schedule.cue('approved'), schedule.cue('approved') + 12) }}>
            <span style={{ fontFamily: SANS, fontSize: 21, color: C.dim }}>approved now:</span><Pill size={20} mono>weather</Pill><Pill size={20} mono>send_telegram</Pill>
          </div>
        </Panel>
        {/* the clock */}
        <div style={{ position: 'absolute', left: 1240, top: 380, width: 480, textAlign: 'center', opacity: shown(f, fire.from - 10) }}>
          <div style={{ fontFamily: SANS, fontSize: 96, fontWeight: 800, color: clock > 0.98 ? C.accentText : C.text }}>{clock > 0.98 ? '19:00' : `${12 + Math.floor(clock * 6)}:${String(Math.floor((clock * 360) % 60)).padStart(2, '0')}`}</div>
        </div>
        <Packet from={[1480, 520]} to={[1480, 640]} start={fire.cue('heard') - 4} end={fire.cue('heard') + 16} label='heard · source "scheduled"' size={18} arc={0} />
        <div style={{ position: 'absolute', left: 160, top: 720, width: 1600 }}>
          <Item at={fire.cue('nobody')} icon="checkmark_circle_24_filled" color={C.success}>Nobody is asked: what was approved is done</Item>
          <Item at={fire.cue('not')} icon="dismiss_circle_24_filled" color={C.danger}>Anything else that would ask: not done (the answer says so)</Item>
          <Item at={fire.cue('late')} icon="power_24_filled" color={C.warning}>The PC was off at 19:00? It is removed, and you are told — never run late</Item>
        </div>
        <Sfx at={fire.cue('heard')} name="chime" volume={0.2} />
      </Part>

      <Part from={k2a.from - 4} to={durationInFrames} last>
        <Heading kicker="The other way" text={f < observed.from - 6 ? 'Your commands → the assistants' : 'Acting out your commands'} at={k2a.from} y={120} size={54} />
        <div style={{ opacity: 1 - ease(f, observed.from - 8, observed.from + 4) }}>
          {[
            { cue: 'action', icon: 'star_24_filled', color: 'gold', t: 'Assistant: action', s: 'one of its own extras' },
            { cue: 'ask', icon: 'chat_24_filled', color: 'indigo', t: 'Assistant: ask', s: 'an order, as if you said it' },
            { cue: 'say', icon: 'speaker_2_24_filled', color: 'pink', t: 'Assistant: say', s: 'a text, with no AI' },
            { cue: 'brain', icon: 'brain_circuit_24_filled', color: 'purple', t: 'Assistant: brain', s: 'free it before a game' },
          ].map((a, i) => {
            const k = shown(f, k2a.cue(a.cue));
            return (
              <div key={a.cue} style={{ position: 'absolute', left: 130 + i * 425, top: 320, width: 395, height: 300, boxSizing: 'border-box', padding: 28, borderRadius: 26, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 40}px)` }}>
                <KdIcon icon={a.icon} color={a.color} size={74} />
                <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text, marginTop: 22 }}>{a.t}</div>
                <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, marginTop: 8 }}>{a.s}</div>
              </div>
            );
          })}
          <Label x={960} y={720} size={26} color={C.dim} show={ease(f, k2a.cue('four'), k2a.cue('four') + 14)}>four actions any command or event can use</Label>
        </div>
        {f >= observed.from - 8 && (
          <>
            <div style={{ position: 'absolute', left: 260, top: 400, display: 'flex', alignItems: 'center', gap: 20, opacity: shown(f, observed.cue('observed')) }}>
              <KdIcon icon="keyboard_24_filled" color="teal" size={80} />
              <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>Your hotkey</span>
            </div>
            <Packet from={[620, 440]} to={[1250, 440]} start={observed.cue('observed') + 10} end={observed.cue('observed') + 30} label="observed" size={20} />
            <Lander x={1400} y={440} size={220} show={shown(f, observed.cue('observed'))} />
            <Label x={960} y={680} size={28} color={C.dim} show={ease(f, observed.to - 40, observed.to - 24)}>just decoration: nothing waits for it</Label>
          </>
        )}
      </Part>
    </AbsoluteFill>
  );
};
