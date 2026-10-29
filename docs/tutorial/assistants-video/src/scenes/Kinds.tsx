import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Code, Heading, Label, Panel, Pill, shown } from '../components/Kit';
import { Sfx } from '../components/Sfx';
import { ease } from '../lib/anim';
import { useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

/** Chapter: the kinds of tools: built-in, the ones that ask K-Desktop, your JSON tools, MCP servers; and what every tool has. */
export const Kinds: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const builtin = B('builtin');
  const bridge = B('bridge');
  const json = B('json');
  const secrets = B('secrets');
  const mcp = B('mcp');
  const props = B('props');
  const groups = [
    ['clock_24_filled', 'Timers'], ['note_24_filled', 'Notes'], ['bookmark_24_filled', 'Memory'], ['calculator_24_filled', 'Sums & dates'], ['weather_partly_cloudy_day_24_filled', 'Weather'],
    ['globe_24_filled', 'The web'], ['folder_24_filled', 'Files'], ['eye_24_filled', 'The screen'], ['desktop_24_filled', 'PC & network'], ['window_24_filled', 'Windows'], ['music_note_2_24_filled', 'Music'],
  ];
  const bridged = ['run_command', 'find_command', 'describe_command', 'run_event', 'control_app', 'set_volume', 'display', 'power_pc', 'lock_pc', 'create_event', 'schedule', 'query_log'];
  const types = [
    { cue: 'http', icon: 'globe_24_filled', color: 'blue', t: 'http', s: 'call a web API', code: ['"request": {', '  "url": "http://ha.local/api/…",', '  "headers": { "Authorization":', '    "Bearer {{secret:ha_token}}" } }'] },
    { cue: 'kdesktop', icon: 'flash_24_filled', color: 'gold', t: 'kdesktop', s: 'run one of your commands', code: ['"command": "Movie mode"'] },
    { cue: 'routine', icon: 'list_24_filled', color: 'green', t: 'routine', s: 'a series of steps', code: ['"steps": [', '  { "tool": "living_room_light" },', '  { "wait": 1 },', '  { "command": "Screen off" } ]'] },
    { cue: 'script', icon: 'code_24_filled', color: 'indigo', t: 'script', s: 'a PowerShell script', code: ['"script": { "file": "check.ps1" }', '// asks first: new or changed'] },
  ];
  return (
    <AbsoluteFill>
      <Part from={0} to={json.from - 4} first>
        <Heading kicker="Kinds of tools" text="Built in, and through K-Desktop" at={builtin.from} y={120} size={54} />
        <Panel x={100} y={250} w={820} show={shown(f, builtin.cue('builtin'))} icon="toolbox_24_filled" color="green" title="Built in" sub="they run in the assistants' process">
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12 }}>
            {groups.map(([icon, t], i) => (
              <div key={t} style={{ opacity: ease(f, builtin.cue('builtin') + 10 + i * 6, builtin.cue('builtin') + 20 + i * 6) }}><Pill size={23}><Fluent name={icon} size={24} color={C.accentText} />{t}</Pill></div>
            ))}
          </div>
        </Panel>
        <Panel x={1000} y={250} w={820} show={shown(f, bridge.cue('kd'))} icon="shield_checkmark_24_filled" color="gold" title="Through K-Desktop" sub="the assistant asks over the pipe">
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10 }}>
            {bridged.map((t, i) => <span key={t} style={{ fontFamily: MONO, fontSize: 21, padding: '6px 12px', borderRadius: 10, background: 'rgba(255,255,255,0.07)', color: C.text, opacity: ease(f, bridge.cue('kd') + 8 + i * 5, bridge.cue('kd') + 16 + i * 5) }}>{t}</span>)}
          </div>
        </Panel>
        <div style={{ position: 'absolute', left: 1000, top: 740, width: 820, display: 'flex', alignItems: 'center', gap: 18, opacity: shown(f, bridge.cue('asks')) }}>
          <KdIcon icon="person_24_filled" color="purple" size={56} /><span style={{ fontFamily: SANS, fontSize: 26, color: C.text }}>asks</span>
          <Fluent name="arrow_right_24_filled" size={32} color={C.faint} />
          <KdIcon icon="shield_checkmark_24_filled" color="gold" size={56} /><span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.accentText }}>does it, with its rights</span>
        </div>
      </Part>

      <Part from={json.from - 4} to={mcp.from - 4}>
        <Heading kicker="Your own, with nothing to compile" text="JSON tools: four types" at={json.from} y={120} size={54} />
        {types.map((t, i) => {
          const k = shown(f, json.cue(t.cue));
          return (
            <div key={t.t} style={{ position: 'absolute', left: 100 + i * 440, top: 250, width: 410, height: 470, boxSizing: 'border-box', padding: 24, borderRadius: 24, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 40}px)` }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}><KdIcon icon={t.icon} color={t.color} size={56} /><span style={{ fontFamily: MONO, fontSize: 32, fontWeight: 600, color: C.text }}>{t.t}</span></div>
              <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim, margin: '12px 0 18px' }}>{t.s}</div>
              <div style={{ fontFamily: MONO, fontSize: 17, lineHeight: 1.55, whiteSpace: 'pre', color: C.text, background: '#141419', borderRadius: 14, padding: 14, overflow: 'hidden' }}>
                {t.code.map((l, j) => <div key={j} style={{ color: l.startsWith('//') ? C.faint : l.includes('secret') ? C.accentText : C.text }}>{l}</div>)}
              </div>
            </div>
          );
        })}
        <div style={{ position: 'absolute', left: 100, top: 770, width: 1720, display: 'flex', gap: 24 }}>
          <div style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 18, padding: 22, borderRadius: 22, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: shown(f, secrets.cue('secrets')) }}>
            <KdIcon icon="lock_closed_24_filled" color="gold" size={60} />
            <div><div style={{ fontFamily: SANS, fontSize: 27, fontWeight: 800, color: C.text }}>Secrets: encrypted</div><div style={{ fontFamily: SANS, fontSize: 21, color: C.dim }}>{'{{secret:ha_token}}'} is filled in at the last moment: the model never sees it</div></div>
          </div>
          <div style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 18, padding: 22, borderRadius: 22, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: shown(f, secrets.cue('changed')) }}>
            <KdIcon icon="fingerprint_24_filled" color="indigo" size={60} />
            <div><div style={{ fontFamily: SANS, fontSize: 27, fontWeight: 800, color: C.text }}>Scripts: a fingerprint</div><div style={{ fontFamily: SANS, fontSize: 21, color: C.dim }}>file + script hashed; not approved → asks, whatever you said</div></div>
          </div>
        </div>
        <Sfx at={secrets.cue('secrets')} name="click" volume={0.25} />
      </Part>

      <Part from={mcp.from - 4} to={durationInFrames} last>
        <Heading kicker={f < props.from - 6 ? "Other programs' tools" : 'Anatomy of a tool'} text={f < props.from - 6 ? 'MCP servers' : 'What every tool has'} at={mcp.from} y={120} size={54} />
        <div style={{ opacity: 1 - ease(f, props.from - 8, props.from + 4) }}>
          <Panel x={160} y={330} w={560} show={shown(f, mcp.cue('mcp'))} icon="person_24_filled" color="purple" title="Assistants' process" sub="an MCP client" />
          {[['Home Assistant', 'standard input'], ['A music player', 'standard input'], ['A web service', 'HTTP']].map(([n, t], i) => (
            <React.Fragment key={n}>
              <Wire from={[720, 410]} to={[1100, 300 + i * 160 + 50]} draw={ease(f, mcp.cue('mcp') + 10 + i * 8, mcp.cue('mcp') + 24 + i * 8)} flow={1} seed={i} />
              <div style={{ position: 'absolute', left: 1100, top: 300 + i * 160, width: 620, height: 110, boxSizing: 'border-box', padding: '0 26px', display: 'flex', alignItems: 'center', gap: 18, borderRadius: 22, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: shown(f, mcp.cue('mcp') + 10 + i * 8) }}>
                <KdIcon icon="plug_connected_24_filled" color="purple" size={54} /><span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>{n}</span><span style={{ marginLeft: 'auto' }}><Pill size={20} mono>{t}</Pill></span>
              </div>
            </React.Fragment>
          ))}
          <div style={{ position: 'absolute', left: 160, top: 560, display: 'flex', alignItems: 'center', gap: 16, opacity: shown(f, mcp.cue('import')) }}>
            <Fluent name="arrow_import_24_filled" size={40} color={C.accentText} />
            <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.text }}>imported from Claude Desktop's config</span>
          </div>
        </div>
        {f >= props.from - 8 && (
          <>
            <Code x={160} y={260} w={900} at={props.from} every={5} size={23} title="a tool, as the model sees it" show={shown(f, props.from)}
              hi={[f > props.cue('name') && f < props.cue('desc') ? 1 : -1, f > props.cue('desc') && f < props.cue('params') ? 2 : -1, f > props.cue('params') && f < props.cue('risk') ? 3 : -1].filter(i => i >= 0)}
              lines={['{', '  "name": "set_timer",', '  "description": "Sets a timer that rings…",', '  "parameters": { "minutes": "number", "reason": "string" }', '}']} />
            <div style={{ position: 'absolute', left: 1120, top: 270, width: 680, display: 'flex', flexDirection: 'column', gap: 18 }}>
              {[
                { at: props.cue('name'), t: 'A name in English', s: 'models follow it better in any language' },
                { at: props.cue('params') + 20, t: 'Example phrases', s: 'to be chosen by meaning' },
                { at: props.cue('risk'), t: 'A risk: safe · ask once · ask always', s: 'your choice in Options wins' },
                { at: props.cue('reads'), t: 'Only reads?', s: 'the time, a sum, the notes' },
                { at: props.cue('undo'), t: 'Undo', s: 'how to take it back, when it can' },
              ].map(p => (
                <div key={p.t} style={{ padding: '16px 22px', borderRadius: 18, background: 'rgba(28,28,34,0.94)', border: `1.5px solid ${C.line2}`, opacity: shown(f, p.at) }}>
                  <div style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.text }}>{p.t}</div>
                  <div style={{ fontFamily: SANS, fontSize: 20, color: C.dim }}>{p.s}</div>
                </div>
              ))}
            </div>
            <Label x={610} y={640} size={22} color={C.faint} show={ease(f, props.to - 30, props.to - 14)}>only the name, description and parameters go to the model</Label>
          </>
        )}
      </Part>
    </AbsoluteFill>
  );
};
