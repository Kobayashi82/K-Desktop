import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Fluent } from '../components/Icon';
import { Gate, Heading, Item, Packet, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, inOut } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** Chapter: K-Desktop (administrator) and the assistants' process (normal user), the pipe between them and its rules. */
export const Processes: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const intro = B('intro');
  const kd = B('kd');
  const as = B('assistant');
  const pipe = B('pipe');
  const closed = B('closed');
  const beat = B('beat');
  const pipeDraw = ease(f, pipe.cue('pipe') - 10, pipe.cue('pipe') + 14);
  const checks = f >= pipe.cue('acl') - 8;
  // While the checks are shown the panels step back.
  const back = inOut(f, pipe.cue('acl') - 10, closed.from, 12);
  return (
    <AbsoluteFill>
      <Part from={0} to={closed.from - 4} first>
        <div style={{ position: 'absolute', inset: 0, opacity: 1 - back * 0.65, filter: back > 0.05 ? `blur(${back * 3}px)` : undefined }}>
          <Panel x={120} y={200} w={660} show={shown(f, intro.cue('two') - 4)} lit={inOut(f, kd.from, kd.to, 10)}
            title={<span style={{ display: 'flex', alignItems: 'center', gap: 14 }}><Img src={staticFile('brand/icon-192.png')} style={{ width: 52, height: 52 }} />K-Desktop</span>}
            sub={<span style={{ display: 'flex', alignItems: 'center', gap: 8, color: C.accentText }}><Fluent name="shield_checkmark_24_filled" size={22} color={C.accentText} />Administrator</span>}>
            <Item at={kd.cue('mic')} icon="mic_24_filled">The microphone</Item>
            <Item at={kd.cue('mic') + 10} icon="headphones_sound_wave_24_filled">The speech models</Item>
            <Item at={kd.cue('commands')} icon="flash_24_filled">Your commands</Item>
            <Item at={kd.cue('ask')} icon="question_circle_24_filled">The permission window</Item>
          </Panel>
          <Panel x={1140} y={200} w={660} show={shown(f, as.cue('proc') - 4)} lit={inOut(f, as.from, as.to, 10)}
            title={<span style={{ display: 'flex', alignItems: 'center', gap: 14 }}><Img src={staticFile('brand/lander/icon.png')} style={{ width: 52, height: 52 }} />Assistants' process</span>}
            sub={<span style={{ display: 'flex', alignItems: 'center', gap: 8 }}><Fluent name="person_24_filled" size={22} color={C.dim} />Normal user · no admin rights</span>}>
            <Item at={as.cue('brain')} icon="brain_circuit_24_filled" color={C.violet}>Its brain (the AI)</Item>
            <Item at={as.cue('tools')} icon="wrench_24_filled" color={C.violet}>Its tools</Item>
            <Item at={as.cue('voice')} icon="speaker_2_24_filled" color={C.violet}>Its voice</Item>
            <Item at={as.cue('body')} icon="emoji_24_filled" color={C.violet}>The character on screen</Item>
          </Panel>
          {/* The named pipe. */}
          <div style={{ position: 'absolute', left: 780, top: 470, width: 360 * pipeDraw, height: 70, borderRadius: 35, background: 'linear-gradient(180deg, #2c2c34, #1c1c22)', border: `2px solid ${C.line2}`, boxShadow: 'inset 0 6px 16px rgba(0,0,0,0.6)' }} />
          <div style={{ position: 'absolute', left: 960, top: 580, transform: 'translateX(-50%)', textAlign: 'center', opacity: pipeDraw }}>
            <div style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>Named pipe</div>
            <div style={{ fontFamily: MONO, fontSize: 18, color: C.dim, marginTop: 6 }}>\\.\pipe\KDesktop.Assistant…</div>
          </div>
          {[0, 1, 2, 3, 4, 5].map(i => {
            const s = pipe.cue('json') - 10 + i * 22;
            const right = i % 2 === 0;
            return <Packet key={i} from={right ? [790, 505] : [1130, 505]} to={right ? [1130, 505] : [790, 505]} start={s} end={s + 26} arc={0} size={17}
              label={['{"t":"heard",…}', '{"t":"chat",…}', '{"t":"step",…}', '{"t":"run",…}', '{"t":"listening",…}', '{"t":"embed",…}'][i]} />;
          })}
        </div>
        {checks && (
          <>
            <Heading kicker="Who may connect" text="The pipe checks the caller" at={pipe.cue('acl') - 6} y={170} size={50} />
            <Gate x={360} y={340} w={560} label="Only your user (and SYSTEM)" sub="the pipe's access list" state={f > pipe.cue('acl') + 8 ? 'pass' : 'wait'} show={shown(f, pipe.cue('acl') - 4)} />
            <Gate x={1000} y={340} w={560} label="The same program" sub="same full path as K-Desktop" state={f > pipe.cue('check') + 8 ? 'pass' : 'wait'} show={shown(f, pipe.cue('check'))} />
            <Gate x={360} y={490} w={560} label="In your session" sub="your user, your Windows session" state={f > pipe.cue('check') + 24 ? 'pass' : 'wait'} show={shown(f, pipe.cue('check') + 12)} />
            <Gate x={1000} y={490} w={560} label="The process it launched" sub="the PID K-Desktop started" state={f > pipe.cue('check') + 40 ? 'pass' : 'wait'} show={shown(f, pipe.cue('check') + 24)} />
            <div style={{ position: 'absolute', left: 0, right: 0, top: 690, textAlign: 'center', fontFamily: SANS, fontSize: 28, color: C.dim, opacity: ease(f, pipe.cue('check') + 50, pipe.cue('check') + 66) }}>
              Anything else: the connection is closed without an answer, and logged.
            </div>
          </>
        )}
        <Sfx at={intro.cue('two') - 4} name="pop" volume={0.22} />
        <Sfx at={as.cue('proc') - 4} name="pop" volume={0.22} />
        <Sfx at={pipe.cue('pipe') - 10} name="whoosh-short" volume={0.22} />
        {[0, 1, 2, 3].map(i => <Sfx key={i} at={[pipe.cue('acl') + 8, pipe.cue('check') + 8, pipe.cue('check') + 24, pipe.cue('check') + 40][i]} name="blip" volume={0.16} />)}
      </Part>
      <Part from={closed.from - 4} to={beat.from - 4}>
        <Heading kicker="What an assistant may ask for" text="A closed list" at={closed.from} y={160} />
        <Panel x={200} y={330} w={720} title="Allowed" icon="checkmark_24_filled" color="green" show={shown(f, closed.cue('list'))}>
          <Item at={closed.cue('exist')} icon="flash_24_filled">Run commands that already exist (by id)</Item>
          <Item at={closed.cue('exist') + 10} icon="window_24_filled">Operations on a window (move, minimize…)</Item>
          <Item at={closed.cue('exist') + 20} icon="data_scatter_24_filled">Vectors of meaning</Item>
          <Item at={closed.cue('exist') + 30} icon="mic_24_filled">Listening, keys for its game, a settings page</Item>
        </Panel>
        <Panel x={1000} y={330} w={720} title="Never" icon="dismiss_24_filled" color="red" show={shown(f, closed.cue('never') - 4)}>
          <Item at={closed.cue('never')} icon="dismiss_circle_24_filled" color={C.danger}>Launch any program it likes</Item>
          <Item at={closed.cue('never') + 8} icon="dismiss_circle_24_filled" color={C.danger}>Run any script</Item>
          <Item at={closed.cue('never') + 16} icon="dismiss_circle_24_filled" color={C.danger}>Open any address</Item>
          <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, marginTop: 6 }}>…with administrator rights.</div>
        </Panel>
        <Sfx at={closed.cue('never')} name="pop-low" volume={0.25} />
      </Part>
      <Part from={beat.from - 4} to={durationInFrames} last>
        <Heading kicker="Staying alive" text="Heartbeat and restart" at={beat.from} y={170} />
        <Panel x={260} y={360} w={420} show={shown(f, beat.from)} title="K-Desktop" icon="shield_checkmark_24_filled" color="gold" />
        <Panel x={1240} y={360} w={420} show={shown(f, beat.from + 6)} title="Assistants" icon="person_24_filled" color="purple" />
        {[0, 1].map(i => <Packet key={i} from={i ? [1240, 420] : [680, 420]} to={i ? [680, 420] : [1240, 420]} start={beat.cue('ping') + i * 30} end={beat.cue('ping') + 24 + i * 30} label={i ? 'pong' : 'ping'} arc={-40} />)}
        <div style={{ position: 'absolute', left: 960, top: 520, transform: 'translateX(-50%)', fontFamily: SANS, fontSize: 24, color: C.dim, opacity: ease(f, beat.cue('ping'), beat.cue('ping') + 12) }}>after 5 s without traffic · dead after 15 s without an answer</div>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 640, display: 'flex', justifyContent: 'center', gap: 18, alignItems: 'center' }}>
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.text, opacity: ease(f, beat.cue('relaunch') - 6, beat.cue('relaunch') + 6), marginRight: 10 }}>Crashed? Restart after</span>
          {['1 s', '2 s', '4 s', '8 s', '…', '60 s'].map((s, i) => (
            <Pill key={s} size={28} mono bg={i === 5 ? C.accentSoft : 'rgba(255,255,255,0.08)'} color={i === 5 ? C.accentText : C.text} style={{ opacity: ease(f, beat.cue('relaunch') + i * 6, beat.cue('relaunch') + i * 6 + 8) }}>{s}</Pill>
          ))}
        </div>
        <div style={{ position: 'absolute', left: 0, right: 0, top: 740, textAlign: 'center', fontFamily: SANS, fontSize: 24, color: C.dim, opacity: ease(f, beat.cue('relaunch') + 44, beat.cue('relaunch') + 60) }}>
          Three crashes in a row with the same assistant: it is taken off the desktop.
        </div>
        <Sfx at={beat.cue('ping')} name="blip" volume={0.2} />
        <Sfx at={beat.cue('ping') + 30} name="blip-low" volume={0.2} />
      </Part>
    </AbsoluteFill>
  );
};
