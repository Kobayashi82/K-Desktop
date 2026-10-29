import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Heading, Label, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease, OUT, rand } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** The built-in tools, by name (what the model sees). */
export const TOOL_NAMES = [
  'get_time', 'set_timer', 'cancel_timer', 'save_note', 'find_notes', 'edit_note', 'delete_note', 'remember', 'forget', 'calculate', 'date_math', 'convert',
  'weather', 'open_web', 'search_web', 'look_up_web', 'read_web', 'open_app', 'control_app', 'end_process', 'list_windows', 'arrange_windows', 'find_files', 'open_file',
  'read_file', 'write_file', 'copy_file', 'move_file', 'delete_file', 'create_folder', 'zip_files', 'unzip', 'look_at_screen', 'pc_status', 'network_check', 'whats_playing',
  'control_media', 'notify_when', 'stop_watching', 'search_knowledge', 'read_clipboard', 'set_clipboard', 'transform_text', 'run_command', 'find_command', 'describe_command',
  'run_event', 'create_event', 'schedule', 'list_scheduled', 'cancel_scheduled', 'add_voice_phrase', 'stop_countdown', 'query_log', 'usage_stats', 'diagnose_command',
  'set_volume', 'audio_device', 'display', 'windows_theme', 'toggle_radio', 'lock_pc', 'power_pc', 'take_screenshot', 'power_plan', 'keep_awake', 'free_brain',
];

const RANK = [
  { t: 'weather', s: 0.79 }, { t: 'look_up_web', s: 0.76 }, { t: 'search_web', s: 0.73 }, { t: 'notify_when', s: 0.72 }, { t: 'date_math', s: 0.66 }, { t: 'set_timer', s: 0.58 }, { t: 'calculate', s: 0.41 },
];

/** Chapter: which tools reach the model for an order, chosen by meaning. */
export const Choose: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const problem = B('problem');
  const examples = B('examples');
  const rank = B('rank');
  const pick = B('pick');
  const parts = B('parts');
  const pinned = B('pinned');
  const find = B('find');
  const nomodel = B('nomodel');
  const fewK = ease(f, problem.cue('few') - 4, problem.cue('few') + 30);
  return (
    <AbsoluteFill>
      <Part from={0} to={examples.from - 4} first>
        <Heading kicker="Too many to offer" text="About 70 built-in tools" at={problem.from} y={120} size={56} />
        <div style={{ position: 'absolute', left: 120, top: 250, width: 1680, display: 'flex', flexWrap: 'wrap', gap: 10, justifyContent: 'center' }}>
          {TOOL_NAMES.map((t, i) => {
            const keep = ['weather', 'look_up_web', 'search_web', 'notify_when'].includes(t);
            return <span key={t} style={{ fontFamily: MONO, fontSize: 21, padding: '7px 12px', borderRadius: 10, background: keep && fewK > 0.5 ? C.accentSoft : 'rgba(255,255,255,0.07)', color: keep && fewK > 0.5 ? C.accentText : C.text, opacity: ease(f, problem.cue('many') + i * 0.6, problem.cue('many') + i * 0.6 + 8) * (keep ? 1 : 1 - 0.75 * fewK) }}>{t}</span>;
          })}
          {Array.from({ length: 24 }, (_, i) => <span key={i} style={{ fontFamily: MONO, fontSize: 21, padding: '7px 12px', borderRadius: 10, background: 'rgba(188,140,255,0.12)', color: C.violet, opacity: ease(f, problem.cue('many') + 50 + i, problem.cue('many') + 58 + i) * (1 - 0.75 * fewK) }}>mcp:{['ha', 'aimp', 'github', 'notion', 'files', 'calendar'][i % 6]}.{['get', 'list', 'set', 'search'][i % 4]}</span>)}
        </div>
        <Label x={960} y={940} size={30} color={C.accentText} show={fewK}>A small model chooses well among a few, badly among many</Label>
      </Part>

      <Part from={examples.from - 4} to={parts.from - 4}>
        <Heading kicker="By meaning" text="Example phrases → vectors" at={examples.from} y={120} size={54} />
        <Panel x={100} y={260} w={700} show={shown(f, examples.cue('ex'))} icon="weather_partly_cloudy_day_24_filled" color="blue" title={<span style={{ fontFamily: MONO }}>weather</span>} sub="its example phrases, in your language">
          {['what\'s the weather like', 'will it rain tomorrow', 'temperature in Madrid'].map((e, i) => (
            <div key={e} style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 14, opacity: ease(f, examples.cue('ex') + 8 + i * 6, examples.cue('ex') + 18 + i * 6) }}>
              <span style={{ fontFamily: SANS, fontSize: 24, color: C.text, width: 360 }}>“{e}”</span>
              <Fluent name="arrow_right_24_filled" size={24} color={C.faint} />
              <span style={{ display: 'flex', gap: 2, opacity: ease(f, examples.cue('once'), examples.cue('once') + 12) }}>{Array.from({ length: 16 }, (_, j) => <span key={j} style={{ width: 7, height: 26, background: rand(i * 40 + j) > 0.5 ? C.accentText : C.sky, opacity: 0.4 + 0.6 * rand(j + i * 3) }} />)}</span>
            </div>
          ))}
          <div style={{ fontFamily: SANS, fontSize: 20, color: C.dim, marginTop: 8, opacity: ease(f, examples.cue('once') + 10, examples.cue('once') + 22) }}>worked out once · kept by K-Desktop</div>
        </Panel>
        <div style={{ position: 'absolute', left: 100, top: 700, opacity: shown(f, rank.cue('vec')) }}><Pill size={30} bg={C.accentSoft} color={C.accentText}>“will I need an umbrella tomorrow?”</Pill></div>
        <Wire from={[800, 720]} to={[900, 420]} draw={ease(f, rank.cue('vec'), rank.cue('vec') + 14)} flow={1} />
        <Panel x={900} y={260} w={900} show={shown(f, rank.cue('best') - 6)} title="Each tool: its closest example">
          {RANK.map((r, i) => {
            const v = r.s * ease(f, rank.cue('best') + i * 3, rank.cue('best') + i * 3 + 20);
            const chosen = (i < 3 && f > pick.cue('three')) || (i === 3 && f > pick.cue('spread'));
            const out = i >= 4 && f > pick.cue('spread') + 10;
            return (
              <div key={r.t} style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 11, opacity: out ? 0.4 : 1 }}>
                <span style={{ fontFamily: MONO, fontSize: 22, width: 200, color: chosen ? C.accentText : C.text }}>{r.t}</span>
                <div style={{ flex: 1, height: 20, borderRadius: 10, background: 'rgba(255,255,255,0.07)', position: 'relative' }}>
                  <div style={{ width: `${v * 100}%`, height: '100%', borderRadius: 10, background: chosen ? C.accentText : C.sky }} />
                  {i === 0 && <div style={{ position: 'absolute', left: `${(0.79 - 0.08) * 100}%`, top: -8, height: 7 * 31 + 16, width: 2, background: C.warning, opacity: ease(f, pick.cue('spread') - 6, pick.cue('spread') + 4) }} />}
                </div>
                <span style={{ fontFamily: MONO, fontSize: 20, color: C.dim, width: 50 }}>{v.toFixed(2)}</span>
                <Fluent name={chosen ? 'checkmark_circle_24_filled' : 'circle_24_regular'} size={26} color={chosen ? C.success : C.faint} />
              </div>
            );
          })}
        </Panel>
        <div style={{ position: 'absolute', left: 900, top: 680, display: 'flex', gap: 14 }}>
          <div style={{ opacity: shown(f, pick.cue('three')) }}><Pill size={22}>at least the 3 best</Pill></div>
          <div style={{ opacity: shown(f, pick.cue('five')) }}><Pill size={22}>up to 5</Pill></div>
          <div style={{ opacity: shown(f, pick.cue('spread')) }}><Pill size={22} color={C.warning} bg="rgba(252,179,63,0.14)">within 0.08 of the best</Pill></div>
        </div>
        <Sfx at={pick.cue('spread') + 4} name="chime" volume={0.18} />
      </Part>

      <Part from={parts.from - 4} to={durationInFrames} last>
        <Heading kicker="On top of those" text={f < pinned.from - 6 ? 'Each part gets its best' : f < nomodel.from - 6 ? 'Always at hand' : 'Without the meaning model'} at={parts.from} y={120} size={54} />
        <div style={{ opacity: 1 - ease(f, pinned.from - 8, pinned.from + 4) }}>
          <div style={{ position: 'absolute', left: 0, right: 0, top: 320, display: 'flex', justifyContent: 'center', gap: 16, opacity: shown(f, parts.cue('parts')) }}>
            <Pill size={36} mono>check the weather</Pill><Pill size={36} mono color={C.danger} bg="transparent">and</Pill><Pill size={36} mono>send it to me on Telegram</Pill>
          </div>
          {[{ x: 420, t: 'weather', s: '0.81' }, { x: 1040, t: 'send_telegram', s: '0.68 ≥ 0.60 → in' }].map((p, i) => (
            <div key={p.t} style={{ position: 'absolute', left: p.x, top: 480, display: 'flex', alignItems: 'center', gap: 16, padding: '20px 28px', borderRadius: 22, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${i ? C.accentText : C.line2}`, opacity: shown(f, parts.cue('each') + i * 12) }}>
              <Fluent name="wrench_24_filled" size={34} color={C.accentText} /><span style={{ fontFamily: MONO, fontSize: 30, color: C.text }}>{p.t}</span><span style={{ fontFamily: MONO, fontSize: 22, color: C.dim }}>{p.s}</span>
            </div>
          ))}
          <Label x={960} y={680} size={24} color={C.dim} show={ease(f, parts.cue('each') + 30, parts.cue('each') + 44)}>the whole order meant only the weather; the second part brings its own tool</Label>
        </div>
        {f >= pinned.from - 8 && f < nomodel.from + 4 && (
          <div style={{ opacity: 1 - ease(f, nomodel.from - 8, nomodel.from + 4) }}>
            {[
              { at: pinned.cue('pinned'), icon: 'history_24_filled', color: 'indigo', t: 'Used in the last exchange', s: '«and how long is left?»' },
              { at: pinned.cue('screen'), icon: 'eye_24_filled', color: 'teal', t: 'The screen tools', s: '«that window», «what you see»' },
              { at: pinned.cue('files'), icon: 'document_24_filled', color: 'gray', t: 'The file tools', s: 'you gave it a file' },
              { at: pinned.cue('runcmd'), icon: 'flash_24_filled', color: 'gold', t: 'run_command', s: 'K-Desktop saw commands close by' },
              { at: find.cue('find'), icon: 'search_24_filled', color: 'sky', t: 'find_tools', s: 'look for more (≥ 0.65 · 3 searches)' },
              { at: find.cue('think'), icon: 'lightbulb_24_filled', color: 'yellow', t: 'think_harder', s: 'more time to reason' },
            ].map((p, i) => {
              const k = shown(f, p.at);
              return (
                <div key={p.t} style={{ position: 'absolute', left: i % 2 ? 990 : 130, top: 270 + Math.floor(i / 2) * 200, width: 800, height: 160, boxSizing: 'border-box', padding: '0 28px', display: 'flex', alignItems: 'center', gap: 22, borderRadius: 24, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${i >= 4 ? 'rgba(242,193,74,0.5)' : C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 30}px)` }}>
                  <KdIcon icon={p.icon} color={p.color} size={66} />
                  <div><div style={{ fontFamily: i >= 3 ? MONO : SANS, fontSize: 29, fontWeight: 800, color: C.text }}>{p.t}</div><div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, marginTop: 4 }}>{p.s}</div></div>
                </div>
              );
            })}
          </div>
        )}
        {f >= nomodel.from - 8 && (
          <>
            <div style={{ position: 'absolute', left: 0, right: 0, top: 330, display: 'flex', justifyContent: 'center', gap: 16, opacity: shown(f, nomodel.cue('words')) }}>
              {[['umbrella', 3.9, true], ['tomorrow', 1.4, false], ['need', 0.6, false]].map(([w, s, rare]) => (
                <div key={w as string} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12 }}>
                  <Pill size={36} mono color={rare ? C.accentText : C.text} bg={rare ? C.accentSoft : undefined}>{w as string}</Pill>
                  <span style={{ fontFamily: MONO, fontSize: 22, color: C.dim }}>weight {(s as number).toFixed(1)}</span>
                </div>
              ))}
            </div>
            <Label x={960} y={560} size={30} color={C.text} show={ease(f, nomodel.cue('words') + 20, nomodel.cue('words') + 34, OUT)}>Rare words, used by few tools, weigh more</Label>
            <Label x={960} y={630} size={24} color={C.dim} show={ease(f, nomodel.to - 30, nomodel.to - 16)}>worse than meaning, but it works</Label>
          </>
        )}
      </Part>
    </AbsoluteFill>
  );
};
