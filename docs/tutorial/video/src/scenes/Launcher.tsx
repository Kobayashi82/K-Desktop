import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../components/Camera';
import { AppWindow, Wallpaper } from '../components/Desktop';
import { Fluent, KdIcon } from '../components/Icon';
import { Keycap } from '../components/Keys';
import { Osd } from '../components/Osd';
import { Sfx } from '../components/Sfx';
import { ease, inOut, pop } from '../lib/anim';
import { useBeats } from '../lib/timeline';
import { C, SANS } from '../theme';

type Result = { title: string; sub: string; badge: string; icon: string; color: string; match?: number[]; uses?: number };

/** The launcher window (LauncherWindow.xaml): search box with its grip, then 48 px rows with icon, title, subtitle, badge. */
const LauncherBox: React.FC<{ query: string; results: Result[]; selected?: number; caret?: boolean; scale?: number }> = ({ query, results, selected = 0, caret = true, scale = 1.75 }) => {
  const f = useCurrentFrame();
  return (
    <div style={{
      width: 640 * scale, borderRadius: 14 * scale, background: 'rgba(28,28,30,0.86)', border: `${scale}px solid rgba(255,255,255,0.12)`,
      boxShadow: `0 ${24 * scale}px ${60 * scale}px rgba(0,0,0,0.6)`, padding: 8 * scale, boxSizing: 'border-box', backdropFilter: 'blur(20px)',
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 * scale, height: 52 * scale, padding: `0 ${12 * scale}px` }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 3px)', gap: 3, opacity: 0.4 }}>{Array.from({ length: 6 }, (_, i) => <div key={i} style={{ width: 3 * scale / 1.5, height: 3 * scale / 1.5, borderRadius: 2, background: 'white' }} />)}</div>
        <Fluent name="search_24_regular" size={20 * scale} color={C.dim} />
        <div style={{ flex: 1, fontFamily: SANS, fontSize: 18 * scale, color: query ? C.text : C.faint, display: 'flex', alignItems: 'center' }}>
          {query || 'Search commands and programs…'}
          {caret && query && <span style={{ width: 2, height: 22 * scale, marginLeft: 2, background: C.accentText, opacity: f % 24 < 14 ? 1 : 0 }} />}
        </div>
        <Img src={staticFile('brand/icon-64.png')} style={{ width: 22 * scale, height: 22 * scale, opacity: 0.85 }} />
      </div>
      {results.length > 0 && <div style={{ height: 1, background: 'rgba(255,255,255,0.08)', margin: `${4 * scale}px ${8 * scale}px` }} />}
      {results.map((r, i) => (
        <div key={r.title} style={{ position: 'relative', height: 48 * scale, margin: `${scale}px 0`, borderRadius: 6 * scale, display: 'flex', alignItems: 'center', gap: 12 * scale, padding: `0 ${12 * scale}px`, background: i === selected ? 'rgba(255,255,255,0.09)' : 'transparent' }}>
          {i === selected && <div style={{ position: 'absolute', left: scale, top: 14 * scale, width: 3 * scale, height: 20 * scale, borderRadius: 2 * scale, background: C.accent }} />}
          <KdIcon icon={r.icon} color={r.color} size={28 * scale} />
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontFamily: SANS, fontSize: 14 * scale, fontWeight: 600, color: C.text }}>
              {r.match ? r.title.split('').map((ch, k) => <span key={k} style={{ color: r.match!.includes(k) ? C.accentText : C.text }}>{ch}</span>) : r.title}
            </div>
            <div style={{ fontFamily: SANS, fontSize: 12 * scale, color: C.dim }}>{r.sub}</div>
          </div>
          {r.uses !== undefined && <div style={{ width: 70 * scale, height: 4 * scale, borderRadius: 2 * scale, background: 'rgba(255,255,255,0.1)' }}><div style={{ width: `${r.uses}%`, height: '100%', borderRadius: 2 * scale, background: C.accent }} /></div>}
          <div style={{ fontFamily: SANS, fontSize: 12 * scale, color: C.dim }}>{r.badge}</div>
        </div>
      ))}
    </div>
  );
};

const MOST_USED: Result[] = [
  { title: 'Work mode', sub: 'notepad.exe +4', badge: 'Command', icon: 'flash_24_filled', color: 'gold' },
  { title: 'Spotify', sub: 'Store app', badge: 'Program', icon: 'music_note_2_24_filled', color: 'green' },
  { title: 'Downloads', sub: 'Open folder', badge: 'Command', icon: 'arrow_download_24_filled', color: 'teal' },
  { title: 'Visual Studio Code', sub: 'Start menu', badge: 'Program', icon: 'code_24_filled', color: 'blue' },
];

export const Launcher: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const intro = B('intro');
  const fuzzy = B('fuzzy');
  const calc = B('calc');
  const rank = B('rank');

  const boxIn = pop(f, intro.cue('launcher') - 4, { damping: 14, stiffness: 170 });
  const letters = 'cmfy';
  const typedAt = fuzzy.cue('c');
  const n = Math.max(0, Math.min(4, Math.floor((f - typedAt) / 7) + 1));
  let query = '';
  let results: Result[] = MOST_USED;
  let selected = 0;
  if (f >= typedAt && f < calc.cue('sum') - 4) {
    query = letters.slice(0, n);
    results = n < 4 ? [
      { title: 'ComfyUI', sub: 'Start menu', badge: 'Program', icon: 'image_sparkle_24_filled', color: 'purple', match: [0] },
      { title: 'Command Prompt', sub: 'Start menu', badge: 'Program', icon: 'window_console_20_filled', color: 'dark', match: [0, 2] },
      { title: 'Camera folder', sub: 'Open folder', badge: 'Command', icon: 'camera_24_filled', color: 'sky', match: [0] },
    ] : [
      { title: 'ComfyUI', sub: 'Start menu', badge: 'Program', icon: 'image_sparkle_24_filled', color: 'purple', match: [0, 2, 3, 4] },
      { title: 'ComfyUI outputs', sub: 'Open folder', badge: 'Command', icon: 'folder_24_filled', color: 'yellow', match: [0, 2, 3, 4] },
    ];
  } else if (f >= calc.cue('sum') - 4 && f < calc.cue('nothing') - 4) {
    query = '12*3.5'.slice(0, Math.max(0, Math.floor((f - calc.cue('sum') + 4) / 3)));
    results = query.length >= 6 ? [{ title: '42', sub: '12*3.5', badge: 'Enter copies', icon: 'calculator_24_filled', color: 'gray' }] : [];
  } else if (f >= calc.cue('nothing') - 4 && f < rank.from - 4) {
    query = 'weather in madrid'.slice(0, Math.max(0, Math.floor((f - calc.cue('nothing') + 4) / 1.5)));
    results = query.length > 14 ? [{ title: `Search the web for “${query}”`, sub: 'Default search engine', badge: 'Web', icon: 'globe_24_filled', color: 'sky' }] : [];
  } else if (f >= rank.from - 4) {
    const order = f > rank.cue('most') + 10 ? [0, 2, 1, 3] : [0, 1, 2, 3];
    results = order.map((i, k) => ({ ...MOST_USED[i], uses: [92, 40, 66, 22][i] * ease(f, rank.from, rank.from + 30) + (k === 0 ? 0 : 0) }));
  }
  if (f >= typedAt && f < calc.cue('sum') - 4) selected = 0;

  const sources = [
    { cue: 'commands', icon: 'flash_24_filled', color: 'gold', text: 'Your commands' },
    { cue: 'start', icon: 'apps_24_filled', color: 'blue', text: 'Start menu programs' },
    { cue: 'store', icon: 'store_microsoft_24_filled', color: 'sky', text: 'Store apps' },
  ];
  const copied = inOut(f, calc.cue('enter') + 6, calc.cue('nothing') - 6, 8);
  const hotkeyDown = inOut(f, intro.cue('hotkey') - 2, intro.cue('hotkey') + 10, 3);
  return (
    <AbsoluteFill>
      <Camera shots={[{ at: 0, x: 960, y: 540, s: 1.08 }, { at: intro.from, dur: 120, x: 960, y: 540, s: 1.0 }]}>
        <Wallpaper hue={-20} />
        <AppWindow kind="explorer" width={900} height={600} style={{ left: 140, top: 250 }} focus={false} />
        <AppWindow kind="browser" width={820} height={560} style={{ left: 980, top: 140 }} focus={false} />
        <AbsoluteFill style={{ background: 'rgba(5,5,10,0.55)' }} />
      </Camera>
      <div style={{ position: 'absolute', left: 960, top: 190, transform: `translateX(-50%) scale(${0.85 + 0.15 * boxIn})`, transformOrigin: 'top center', opacity: Math.min(1, boxIn * 2) }}>
        <LauncherBox query={query} results={results} selected={selected} />
      </div>
      {/* Its own hotkey */}
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 140, display: 'flex', justifyContent: 'center', gap: 18, opacity: inOut(f, intro.cue('hotkey') - 12, fuzzy.from, 8) }}>
        <Keycap label="Ctrl" height={100} width={160} size={34} down={hotkeyDown} />
        <Keycap label="Space" height={100} width={300} size={34} down={hotkeyDown} />
      </div>
      {/* Where it searches */}
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 130, display: 'flex', justifyContent: 'center', gap: 22 }}>
        {sources.map(s => {
          const k = pop(f, fuzzy.cue(s.cue) - 6) * (1 - ease(f, calc.from - 6, calc.from + 4));
          return (
            <div key={s.cue} style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 24px', borderRadius: 40, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 2), transform: `translateY(${(1 - k) * 40}px)` }}>
              <KdIcon icon={s.icon} color={s.color} size={44} />
              <span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 700, color: C.text }}>{s.text}</span>
            </div>
          );
        })}
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 140, display: 'flex', justifyContent: 'center' }}>
        <Osd icon="clipboard_text_edit_24_filled" color="brown" text="Copied: 42" show={copied} />
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 140, display: 'flex', justifyContent: 'center', gap: 16, opacity: inOut(f, calc.cue('enter') - 8, calc.cue('enter') + 8, 3) }}>
        <Keycap label="Enter" height={90} width={190} size={30} down={inOut(f, calc.cue('enter') - 2, calc.cue('enter') + 6, 2)} />
      </div>
      <Sfx at={intro.cue('hotkey') - 2} name="key" volume={0.5} />
      <Sfx at={intro.cue('launcher') - 4} name="pop" volume={0.3} />
      {[0, 7, 14, 21].map(d => <Sfx key={d} at={typedAt + d} name="key" volume={0.2} />)}
      {sources.map(s => <Sfx key={s.cue} at={fuzzy.cue(s.cue) - 6} name="pop-high" volume={0.18} />)}
      {[0, 3, 6, 9, 12, 15].map(d => <Sfx key={`c${d}`} at={calc.cue('sum') - 4 + d} name="key" volume={0.15} />)}
      <Sfx at={calc.cue('enter') - 2} name="key" volume={0.4} />
      <Sfx at={calc.cue('enter') + 6} name="chime" volume={0.18} />
      <Sfx at={rank.cue('most') + 10} name="whoosh-short" volume={0.2} />
    </AbsoluteFill>
  );
};
