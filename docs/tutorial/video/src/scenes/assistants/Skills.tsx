import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../components/Capture';
import { AppWindow, Taskbar, Wallpaper } from '../../components/Desktop';
import { Fluent, KdIcon } from '../../components/Icon';
import { Mono } from '../../components/Keys';
import { Osd } from '../../components/Osd';
import { Sfx } from '../../components/Sfx';
import { ease, glide, inOut, keys, OUT, pop, SMOOTH } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, MONO, SANS } from '../../theme';
import { Bubble } from './Inside';

/** Rows of tools-builtin.png (top, height), by category. */
const ROWS: Record<string, [number, number]> = {
  commands: [166, 62], files: [256, 62], internet: [348, 58], music: [440, 58], notes: [526, 60], pc: [616, 58], screen: [706, 60], time: [795, 62], weather: [885, 64],
};
const CUE_ROW: Array<[string, string]> = [
  ['timers', 'time'], ['notes', 'notes'], ['calc', 'time'], ['weather', 'weather'], ['web', 'internet'], ['files', 'files'], ['apps', 'pc'], ['music', 'music'], ['pc', 'pc'], ['screen', 'screen'], ['commands', 'commands'],
];

/** What they can do (Settings › Tools › Built-in), a highlight moving with the words. */
export const ToolsPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const pts: Array<[number, number]> = CUE_ROW.map(([cue, row]) => [b.cue(cue) - 4, ROWS[row][0]]);
  const y = keys(f, pts);
  const h = keys(f, CUE_ROW.map(([cue, row]) => [b.cue(cue) - 4, ROWS[row][1]] as [number, number]));
  const show = ease(f, b.cue('timers') - 10, b.cue('timers') - 2);
  return (
    <CaptureShot name="tools-builtin"
      shots={[{ at: 0, x: 1000, y: 540, s: 1.15 }, { at: b.cue('timers') - 10, dur: 30, x: 1030, y: 640, s: 1.4 }, { at: b.cue('web') - 10, dur: 40, x: 1030, y: 380, s: 1.4 }, { at: b.cue('apps') - 10, dur: 40, x: 1030, y: 560, s: 1.4 }, { at: b.cue('commands') - 10, dur: 30, x: 1030, y: 400, s: 1.35 }]}>
      <div style={{ position: 'absolute', left: 566, top: y - 6, width: 930, height: h + 12, borderRadius: 12, border: `3px solid ${C.accentText}`, background: 'rgba(242,193,74,0.10)', boxShadow: '0 0 24px rgba(242,193,74,0.6)', opacity: show }} />
    </CaptureShot>
  );
};

/** Anything risky asks first; and it can be undone. */
export const RiskPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const lit = f >= b.cue('always') - 4 ? 1 : f >= b.cue('yes') - 4 ? 0 : -1;
  const undo = inOut(f, b.cue('undo') - 6, b.to + 40, 8);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 470, transform: `translate(-50%, -50%) scale(${pop(f, b.cue('risky') - 8)})`, width: 980, padding: 40, borderRadius: 30, background: 'rgba(32,32,38,0.98)', border: `1.5px solid ${C.line2}`, boxShadow: '0 40px 100px rgba(0,0,0,0.6)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 22, marginBottom: 30 }}>
          <Img src={staticFile('brand/kobii/mint.png')} style={{ width: 96, height: 96 }} />
          <div style={{ fontFamily: SANS, fontSize: 40, fontWeight: 800, color: C.text }}>May I delete <span style={{ color: C.accentText }}>old-report.pdf</span>?</div>
        </div>
        <div style={{ display: 'flex', gap: 18 }}>
          {['Yes', 'Yes, and don’t ask again', 'No'].map((t, i) => (
            <span key={t} style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, padding: '16px 28px', borderRadius: 16, background: i === lit ? C.accentText : 'rgba(255,255,255,0.07)', color: i === lit ? '#1a1306' : C.text }}>{t}</span>
          ))}
        </div>
      </div>
      <div style={{ position: 'absolute', left: 960, top: 820, transform: 'translateX(-50%)' }}>
        <Osd icon="arrow_undo_24_filled" color="orange" text="Undone: delete old-report.pdf" show={undo} />
      </div>
      <Sfx at={b.cue('risky') - 8} name="pop" volume={0.25} />
      <Sfx at={b.cue('yes') - 4} name="click" volume={0.25} />
      <Sfx at={b.cue('always') - 4} name="click" volume={0.25} />
      <Sfx at={b.cue('undo') - 6} name="whoosh-down" volume={0.2} />
    </AbsoluteFill>
  );
};

/** «Lander, minimize Chrome»: it flies, fires, and the window goes on impact; then the grappling hook. */
export const GesturesPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const flies = b.cue('flies');
  const fires = b.cue('fires') + 6;
  const impact = Math.max(b.cue('impact'), fires + 12);
  const hook = b.cue('hook');
  const tows = b.cue('tows');
  // Chrome's minimise button (window at 420, 140, 1000 wide): x = 420 + 1000 - 135, y = 140 + 23.
  const btn = { x: 1285, y: 163 };
  const lx = keys(f, [[flies - 4, 260], [flies + 26, 1150], [hook - 6, 1150], [hook + 18, 1500], [tows, 1500], [tows + 40, 700]], SMOOTH);
  const ly = keys(f, [[flies - 4, 900], [flies + 26, 330], [hook - 6, 330], [hook + 18, 420], [tows, 420], [tows + 40, 340]], SMOOTH);
  const shot = ease(f, fires, impact, OUT);
  const gone = ease(f, impact, impact + 14, OUT);
  const flash = inOut(f, impact - 2, impact + 10, 3);
  const hookK = ease(f, hook + 18, hook + 30, OUT);
  const tow = ease(f, tows, tows + 40, SMOOTH);
  const win2 = { x: 1180 + (380 - 1180) * tow, y: 560 + (480 - 560) * tow };
  const titleY = win2.y + 23;
  return (
    <AbsoluteFill>
      <Wallpaper hue={30} />
      <div style={{ position: 'absolute', left: 420, top: 140, transformOrigin: '50% 100%', transform: `translateY(${gone * 600}px) scale(${1 - 0.92 * gone})`, opacity: 1 - gone }}>
        <AppWindow kind="chrome" width={1000} height={620} style={{ position: 'relative' }} />
      </div>
      <div style={{ position: 'absolute', left: win2.x, top: win2.y }}>
        <AppWindow kind="explorer" width={640} height={420} style={{ position: 'relative' }} focus={false} />
      </div>
      <Taskbar apps={[{ icon: 'globe_24_filled', color: 'green', active: true }, { icon: 'folder_24_filled', color: 'yellow', active: true }]} />
      {/* The shot */}
      {shot > 0 && shot < 1 && (
        <svg style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }} width={1} height={1}>
          <line x1={lx} y1={ly - 20} x2={lx + (btn.x - lx) * shot} y2={ly - 20 + (btn.y - ly + 20) * shot} stroke={C.gold1} strokeWidth={8} strokeLinecap="round" style={{ filter: `drop-shadow(0 0 12px ${C.gold1})` }} />
        </svg>
      )}
      <div style={{ position: 'absolute', left: btn.x - 60, top: btn.y - 60, width: 120, height: 120, borderRadius: 60, background: `radial-gradient(circle, rgba(255,240,180,${flash}), transparent 70%)`, transform: `scale(${1 + flash})` }} />
      {/* The grappling hook */}
      {hookK > 0 && (
        <svg style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }} width={1} height={1}>
          <line x1={lx} y1={ly + 40} x2={lx + (win2.x + 320 - lx) * hookK} y2={ly + 40 + (titleY - ly - 40) * hookK} stroke="#c9ccd3" strokeWidth={4} strokeDasharray="10 6" />
          <circle cx={lx + (win2.x + 320 - lx) * hookK} cy={ly + 40 + (titleY - ly - 40) * hookK} r={10} fill={C.accentText} />
        </svg>
      )}
      <Img src={staticFile('brand/lander/classic.png')} style={{ position: 'absolute', left: lx - 80, top: ly - 80 + Math.sin(f / 6) * 4, width: 160, height: 160, transform: `rotate(${(lx - keys(f - 2, [[flies - 4, 260], [flies + 26, 1150]])) * 0.02}deg)` }} />
      <Bubble user text="Lander, minimize Chrome" show={inOut(f, b.cue('ask') - 6, flies + 20, 8)} x={1300} y={820} size={40} />
      <Sfx at={b.cue('ask') - 6} name="pop" volume={0.25} />
      <Sfx at={flies} name="whoosh" volume={0.3} />
      <Sfx at={fires} name="blip" volume={0.35} />
      <Sfx at={impact} name="impact" volume={0.35} />
      <Sfx at={hook + 18} name="whoosh-short" volume={0.3} />
      <Sfx at={tows} name="whoosh" volume={0.25} />
    </AbsoluteFill>
  );
};

const TOOL_JSON = `{
  "name": "living_room_light",
  "type": "http",
  "description": "Turns the living room light on or off.",
  "examples": ["turn on the living room light"],
  "parameters": { "state": { "enum": ["on", "off"] } },
  "risk": "safe"
}`;

/** Their own tools, as JSON files; secrets; MCP servers. */
export const OwnToolsPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const typed = TOOL_JSON.slice(0, Math.max(0, Math.floor((f - b.cue('json') + 10) * 9)));
  const types = [
    { cue: 'api', icon: 'arrow_swap_24_filled', color: 'teal', t: 'http' },
    { cue: 'command', icon: 'flash_24_filled', color: 'gold', t: 'kdesktop' },
    { cue: 'routine', icon: 'task_list_ltr_24_filled', color: 'purple', t: 'routine' },
    { cue: 'script', icon: 'window_console_20_filled', color: 'dark', t: 'script' },
  ];
  const toMcp = glide(f, b.cue('mcp') - 10, 26);
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ opacity: 1 - toMcp }}>
        <div style={{ position: 'absolute', left: 140, top: 220, width: 980, height: 520, borderRadius: 18, background: '#0c0c10', border: `1px solid ${C.line2}`, boxShadow: '0 40px 90px rgba(0,0,0,0.6)', overflow: 'hidden', transform: `translateY(${(1 - glide(f, b.from - 10, 24)) * 60}px)` }}>
          <div style={{ height: 46, display: 'flex', alignItems: 'center', gap: 10, padding: '0 18px', background: '#1a1a20' }}>
            <Fluent name="document_24_filled" size={20} color={C.dim} /><Mono size={18} color={C.dim}>Assistant\Tools\living_room_light.json</Mono>
          </div>
          <pre style={{ margin: 0, padding: '18px 24px', fontFamily: MONO, fontSize: 24, lineHeight: 1.5, color: C.text, whiteSpace: 'pre-wrap' }}>{typed}</pre>
        </div>
        <div style={{ position: 'absolute', left: 1200, top: 240, display: 'flex', flexDirection: 'column', gap: 20 }}>
          {types.map(t => {
            const k = pop(f, b.cue(t.cue) - 6);
            return (
              <div key={t.cue} style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '16px 24px', borderRadius: 20, background: 'rgba(32,32,38,0.97)', border: `1.5px solid ${C.line2}`, opacity: k, transform: `translateX(${(1 - k) * 60}px)` }}>
                <KdIcon icon={t.icon} color={t.color} size={50} />
                <Mono size={28} color={C.text}>{`"type": "${t.t}"`}</Mono>
              </div>
            );
          })}
          <div style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '16px 24px', borderRadius: 20, background: 'rgba(108,203,95,0.12)', border: `1.5px solid ${C.success}`, opacity: pop(f, b.cue('secrets') - 6) }}>
            <Fluent name="lock_closed_24_filled" size={40} color={C.success} />
            <div>
              <Mono size={24} color={C.text}>token: ••••••••••••</Mono>
              <div style={{ fontFamily: SANS, fontSize: 20, color: C.dim }}>Encrypted · the model never sees it</div>
            </div>
          </div>
        </div>
      </AbsoluteFill>
      <AbsoluteFill style={{ opacity: toMcp }}>
        <CaptureShot name="tools-mcp-server"
          shots={[{ at: b.cue('mcp') - 10, x: 1000, y: 420, s: 1.3 }, { at: b.cue('mcp') + 10, dur: 90, x: 1000, y: 440, s: 1.4 }]}
          focus={[{ from: b.cue('mcp'), to: b.to + 40, rect: [556, 90, 970, 130], label: 'An MCP server and its tools' }]} />
        <div style={{ position: 'absolute', left: 0, right: 0, bottom: 110, display: 'flex', justifyContent: 'center', opacity: pop(f, b.cue('claude') - 6) }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '16px 28px', borderRadius: 30, background: 'rgba(30,30,36,0.97)', border: `1.5px solid ${C.line2}`, fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>
            <Fluent name="arrow_import_24_regular" size={32} color={C.accentText} /> Import from Claude Desktop
          </div>
        </div>
      </AbsoluteFill>
      {types.map(t => <Sfx key={t.cue} at={b.cue(t.cue) - 6} name="pop-high" volume={0.16} />)}
      <Sfx at={b.cue('secrets') - 6} name="blip-low" volume={0.25} />
      <Sfx at={b.cue('mcp') - 10} name="whoosh-short" volume={0.2} />
    </AbsoluteFill>
  );
};
