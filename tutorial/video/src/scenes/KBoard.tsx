import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../components/Camera';
import { Cursor } from '../components/Cursor';
import { Kinetic } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Mono } from '../components/Keys';
import { MouseShape } from '../components/Props';
import { Sfx } from '../components/Sfx';
import { Check, Toggle } from '../components/Ui';
import { ease, glide, inOut, pop } from '../lib/anim';
import { useBeats } from '../lib/timeline';
import { C, SANS } from '../theme';
import { t } from '../lib/i18n';

const U = 66; // key unit
const ROWS: Array<Array<[string, number]>> = [
  [['Esc', 1], ['F1', 1], ['F2', 1], ['F3', 1], ['F4', 1], ['F5', 1], ['F6', 1], ['F7', 1], ['F8', 1], ['F9', 1], ['F10', 1], ['F11', 1], ['F12', 1], ['ScrLk', 1.75]],
  [['`', 1], ['1', 1], ['2', 1], ['3', 1], ['4', 1], ['5', 1], ['6', 1], ['7', 1], ['8', 1], ['9', 1], ['0', 1], ['-', 1], ['=', 1], ['⌫', 2]],
  [['Tab', 1.5], ['Q', 1], ['W', 1], ['E', 1], ['R', 1], ['T', 1], ['Y', 1], ['U', 1], ['I', 1], ['O', 1], ['P', 1], ['[', 1], [']', 1], ['\\', 1.5]],
  [['Caps Lock', 1.75], ['A', 1], ['S', 1], ['D', 1], ['F', 1], ['G', 1], ['H', 1], ['J', 1], ['K', 1], ['L', 1], [';', 1], ["'", 1], ['Enter', 2.25]],
  [['Shift', 2.25], ['Z', 1], ['X', 1], ['C', 1], ['V', 1], ['B', 1], ['N', 1], ['M', 1], [',', 1], ['.', 1], ['/', 1], ['Shift ', 2.75]],
  [['Ctrl', 1.4], ['Win', 1.3], ['Alt', 1.3], ['Space', 6.4], ['Alt ', 1.3], ['Menu', 1.3], ['Ctrl ', 2]],
];

type Remap = { to: string; tone: 'gold' | 'red'; icon?: string };

/** A drawn keyboard (KBoardSurface): remapped keys light gold with their new job, disabled ones red. */
const Keyboard: React.FC<{ remaps: Record<string, Remap>; hot?: string; dots?: string[] }> = ({ remaps, hot, dots = [] }) => (
  <div style={{ padding: 22, borderRadius: 26, background: 'linear-gradient(180deg,#26262c,#1a1a1f)', border: `1px solid ${C.line2}`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)' }}>
    {ROWS.map((row, r) => (
      <div key={r} style={{ display: 'flex', gap: 8, marginBottom: r === 0 ? 18 : 8 }}>
        {row.map(([label, w]) => {
          const m = remaps[label];
          const isHot = hot === label;
          return (
            <div key={label} style={{
              position: 'relative', width: U * w + 8 * (w - 1), height: U, borderRadius: 10, boxSizing: 'border-box', display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center',
              background: m ? (m.tone === 'gold' ? 'rgba(224,168,21,0.36)' : 'rgba(255,107,107,0.35)') : 'linear-gradient(180deg,#3a3a42,#2c2c33)',
              border: `${isHot ? 2.5 : 1}px solid ${isHot ? C.gold1 : m ? (m.tone === 'gold' ? 'rgba(242,193,74,0.8)' : 'rgba(255,107,107,0.8)') : 'rgba(255,255,255,0.06)'}`,
              boxShadow: isHot ? `0 0 26px ${C.gold1}` : '0 3px 0 #141418', fontFamily: SANS, color: C.text,
            }}>
              <span style={{ fontSize: m ? 12 : 17, fontWeight: 600, opacity: m ? 0.7 : 0.9 }}>{label.trim()}</span>
              {m && (
                <span style={{ fontSize: 14, fontWeight: 800, color: m.tone === 'gold' ? C.gold1 : '#ffb3b3', display: 'flex', alignItems: 'center', gap: 4, whiteSpace: 'nowrap' }}>
                  {m.icon && <Fluent name={m.icon} size={14} color={C.gold1} />}{m.to}
                </span>
              )}
              {dots.includes(label) && <div style={{ position: 'absolute', right: 6, top: 6, width: 7, height: 7, borderRadius: 4, background: C.accentText }} />}
            </div>
          );
        })}
      </div>
    ))}
  </div>
);

const OPTIONS = [
  { cue: 'key', icon: 'keyboard_24_filled', label: t('Key or combination') },
  { cue: 'text', icon: 'text_t_24_filled', label: t('Text') },
  { cue: 'command', icon: 'flash_24_filled', label: t('Command') },
  { cue: 'nothing', icon: 'prohibited_24_filled', label: t('Disabled') },
];

export const KBoard: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const intro = B('intro');
  const surface = B('surface');
  const targets = B('targets');
  const examples = B('examples');
  const layers = B('layers');
  const scope = B('scope');

  const kbIn = glide(f, intro.from, 30);
  const clickAt = surface.cue('click');
  const chooseAt = surface.cue('choose');
  const lightsAt = surface.cue('lights');
  const popover = inOut(f, clickAt + 4, examples.from - 6, 8);
  const optIdx = f < targets.from ? (f > chooseAt ? 0 : -1) : OPTIONS.reduce((m, o, i) => (f >= targets.cue(o.cue === 'key' ? 'key' : o.cue) - 4 ? i : m), 0);
  const ctrlLayer = f > layers.cue('ctrlj') - 4 && f < layers.cue('j') - 2;

  const remaps: Record<string, Remap> = {};
  if (f > lightsAt - 4) remaps['Caps Lock'] = { to: 'Ctrl', tone: 'gold' };
  if (f > targets.cue('combo') - 4) remaps['F1'] = { to: 'Alt+F4', tone: 'gold' };
  if (f > targets.cue('text') - 4) remaps['F2'] = { to: '“Hi!”', tone: 'gold' };
  if (f > targets.cue('command') - 4) remaps['F3'] = { to: 'Work', tone: 'gold', icon: 'flash_24_filled' };
  if (f > targets.cue('nothing') - 4) remaps['ScrLk'] = { to: 'Off', tone: 'red' };
  if (f > examples.cue('win') - 4) remaps['Win'] = { to: 'Off', tone: 'red' };
  if (ctrlLayer) {
    for (const k of Object.keys(remaps)) delete remaps[k];
    remaps['J'] = { to: 'Vol +', tone: 'gold' };
    remaps['K'] = { to: 'Vol −', tone: 'gold' };
  }
  const hot = f > examples.cue('caps') - 4 && f < examples.cue('win') - 4 ? 'Caps Lock' : f > examples.cue('win') - 4 && f < examples.cue('side') - 4 ? 'Win' : f > clickAt - 2 && f < lightsAt + 20 ? 'Caps Lock' : undefined;
  const sideLit = f > examples.cue('side') - 4;
  const toScope = glide(f, scope.from - 8, 26);
  const shots = [
    { at: 0, x: 960, y: 560, s: 0.9, rx: 14 },
    { at: intro.from, dur: 50, x: 960, y: 540, s: 1.0, rx: 6 },
    { at: clickAt - 10, dur: 30, x: 700, y: 680, s: 1.25, rx: 0 },
    { at: targets.from - 4, dur: 30, x: 860, y: 640, s: 1.12 },
    { at: examples.from - 4, dur: 30, x: 960, y: 560, s: 1.02 },
    { at: layers.from - 4, dur: 30, x: 900, y: 560, s: 1.1 },
    { at: scope.from - 6, dur: 30, x: 960, y: 900, s: 0.8, rx: 8 },
  ];
  return (
    <AbsoluteFill>
      <Kinetic text={t('K-Board')} at={intro.from - 4} x={960} y={150} size={96} out={surface.from + 10} />
      <Camera shots={shots}>
        <div style={{ position: 'absolute', left: 170, top: 330, transform: `translateY(${(1 - kbIn) * 300}px)`, opacity: kbIn }}>
          <Keyboard remaps={remaps} hot={hot} dots={f > layers.cue('modifiers') ? ['J', 'K'] : []} />
        </div>
        <div style={{ position: 'absolute', left: 1440, top: 400, transform: `translateY(${(1 - kbIn) * 300}px)`, opacity: kbIn }}>
          <MouseShape scale={1.15} glow={sideLit ? 1 : 0} />
          <div style={{ position: 'absolute', left: -100, top: 160, display: 'flex', flexDirection: 'column', gap: 50, opacity: ease(f, examples.cue('side'), examples.cue('side') + 10) }}>
            {[t('Copy'), t('Paste')].map(t => <span key={t} style={{ fontFamily: SANS, fontSize: 24, fontWeight: 800, color: '#1a1306', background: C.gold1, padding: '6px 14px', borderRadius: 10, textAlign: 'center' }}>{t}</span>)}
          </div>
        </div>
        {/* What the key does */}
        <div style={{ position: 'absolute', left: 340, top: 780, width: 560, padding: 20, borderRadius: 20, background: 'rgba(40,40,48,0.99)', border: `1.5px solid rgba(242,193,74,0.6)`, boxShadow: '0 20px 60px rgba(0,0,0,0.6)', opacity: popover, transform: `translateY(${(1 - popover) * 20}px)` }}>
          <div style={{ fontFamily: SANS, fontSize: 20, fontWeight: 700, color: C.dim, marginBottom: 10 }}>{t('Caps Lock sends…')}</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10 }}>
            {OPTIONS.map((o, i) => (
              <span key={o.cue} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '9px 14px', borderRadius: 12, fontFamily: SANS, fontSize: 19, fontWeight: 700, background: i === optIdx ? C.accentText : 'rgba(255,255,255,0.06)', color: i === optIdx ? '#1a1306' : C.text }}>
                <Fluent name={o.icon} size={20} color={i === optIdx ? '#1a1306' : C.accentText} />{o.label}
              </span>
            ))}
          </div>
        </div>
        {/* Layers */}
        <div style={{ position: 'absolute', left: 170, top: 210, display: 'flex', alignItems: 'center', gap: 18, opacity: inOut(f, layers.from - 6, scope.from, 8) }}>
          <span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 700, color: C.text }}>{t('With modifiers:')}</span>
          {['Ctrl', 'Alt', 'Shift', 'Win'].map(m => (
            <span key={m} style={{ display: 'flex', alignItems: 'center', gap: 10, fontFamily: SANS, fontSize: 24, color: C.text }}>
              <Check on={m === 'Ctrl' && ctrlLayer} scale={1.3} />{m}
            </span>
          ))}
        </div>
        <Cursor path={[{ at: 0, x: 900, y: 900 }, { at: clickAt - 18, x: 253, y: 617, dur: 14 }, { at: chooseAt - 8, x: 520, y: 870, dur: 10 }, { at: layers.cue('ctrlj') - 16, x: 420, y: 212, dur: 12 }]} clicks={[{ at: clickAt }, { at: chooseAt }, { at: layers.cue('ctrlj') - 4 }]} />
        {/* Scope: everywhere or some apps; a command or the tray */}
        <div style={{ position: 'absolute', left: 170, top: 1010, display: 'flex', gap: 30, opacity: toScope }}>
          <div style={{ width: 760, padding: 26, borderRadius: 24, background: 'rgba(32,32,38,0.97)', border: `1px solid ${C.line2}` }}>
            <div style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.text, marginBottom: 14 }}>{t('Gaming profile')}</div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 14 }}>
              <span style={{ fontFamily: SANS, fontSize: 22, color: f < scope.cue('apps') - 4 ? C.accentText : C.dim }}>{t('Everywhere')}</span>
              <span style={{ fontFamily: SANS, fontSize: 22, color: C.faint }}>·</span>
              <span style={{ fontFamily: SANS, fontSize: 22, color: f >= scope.cue('apps') - 4 ? C.accentText : C.dim }}>{t('Only in:')}</span>
              {['eldenring.exe', 'cs2.exe'].map((a, i) => (
                <span key={a} style={{ opacity: pop(f, scope.cue('apps') + i * 6), padding: '6px 12px', borderRadius: 10, background: 'rgba(255,255,255,0.08)' }}><Mono size={19}>{a}</Mono></span>
              ))}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 18px', borderRadius: 14, background: f > scope.cue('command') - 4 ? 'rgba(242,193,74,0.12)' : 'rgba(255,255,255,0.05)' }}>
              <KdIcon icon="keyboard_layout_float_24_filled" color="purple" size={44} />
              <span style={{ fontFamily: SANS, fontSize: 22, color: C.text }}>{t('Action «K-Board»: toggle “Gaming”')}</span>
            </div>
          </div>
          <div style={{ width: 420, padding: 18, borderRadius: 18, background: 'rgba(40,40,48,0.98)', border: `1.5px solid ${f > scope.cue('tray') - 4 ? 'rgba(242,193,74,0.6)' : C.line2}`, opacity: ease(f, scope.cue('tray') - 6, scope.cue('tray') + 6) }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 10 }}>
              <Img src={staticFile('brand/icon-64.png')} style={{ width: 30, height: 30 }} />
              <span style={{ fontFamily: SANS, fontSize: 20, fontWeight: 700, color: C.text }}>{t('K-Board')}</span>
            </div>
            {[t('Gaming'), t('Photo editing'), t('Writing')].map((p, i) => (
              <div key={p} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '8px 10px', fontFamily: SANS, fontSize: 20, color: C.text }}>
                <Toggle on={i === 0 && f > scope.cue('tray') + 10 ? 1 : 0} />{p}
              </div>
            ))}
          </div>
        </div>
      </Camera>
      <Sfx at={intro.from} name="whoosh" volume={0.25} />
      <Sfx at={clickAt} name="click" volume={0.3} />
      <Sfx at={chooseAt} name="click" volume={0.3} />
      <Sfx at={lightsAt - 4} name="chime" volume={0.2} />
      {['combo', 'text', 'command', 'nothing'].map(c => <Sfx key={c} at={targets.cue(c) - 4} name="blip" volume={0.16} />)}
      {['caps', 'win', 'side'].map(c => <Sfx key={c} at={examples.cue(c) - 4} name="pop-high" volume={0.18} />)}
      <Sfx at={layers.cue('ctrlj') - 4} name="click" volume={0.3} />
      <Sfx at={scope.from - 6} name="whoosh-short" volume={0.2} />
    </AbsoluteFill>
  );
};
