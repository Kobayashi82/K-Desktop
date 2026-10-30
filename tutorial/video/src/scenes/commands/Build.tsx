import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Camera } from '../../components/Camera';
import { Part } from '../../components/Chapter';
import { WORK_MODE } from '../../components/CommandCard';
import { COMMAND_LIST, CommandsPage } from '../../components/CommandsPage';
import { Cursor } from '../../components/Cursor';
import { AppWindow, Taskbar, Wallpaper } from '../../components/Desktop';
import { Fluent, KdIcon } from '../../components/Icon';
import { Keycap } from '../../components/Keys';
import { KdWindow } from '../../components/KdWindow';
import { Osd } from '../../components/Osd';
import { MouseShape } from '../../components/Props';
import { Sfx } from '../../components/Sfx';
import { ease, glide, inOut, OUT, pop, SMOOTH, tween } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';
import { t } from '../../lib/i18n';

const WIN = { x: 180, y: 70 };
// Stage points of the page (content starts at WIN + sidebar 250, title bar 46).
const NEW_BTN = { x: 590, y: 312 };
const ADD_HOTKEY = { x: 880, y: 326 };
const ADD_ACTION = { x: 1620, y: 508 };

/** Big keys pressed one after another (Ctrl, Alt, W), held until `release`. */
const ComboKeys: React.FC<{ keys: string[]; at: number[]; release: number; show: number }> = ({ keys, at, release, show }) => {
  const f = useCurrentFrame();
  return (
    <div style={{ position: 'absolute', left: 0, right: 0, bottom: 230, display: 'flex', justifyContent: 'center', gap: 26, opacity: show, transform: `translateY(${(1 - show) * 80}px)` }}>
      {keys.map((k, i) => {
        const down = Math.min(ease(f, at[i] - 3, at[i] + 2), 1 - ease(f, release, release + 6));
        return (
          <React.Fragment key={k}>
            {i > 0 && <div style={{ alignSelf: 'center', fontFamily: SANS, fontSize: 50, fontWeight: 700, color: C.faint }}>+</div>}
            <Keycap label={k} height={120} width={k.length > 1 ? 190 : 120} size={40} down={down} />
          </React.Fragment>
        );
      })}
    </div>
  );
};

export const BuildPart: React.FC<{ page: BeatTimes; hotkey: BeatTimes; hotkey2: BeatTimes; actions: BeatTimes; to: number }> = ({ page, hotkey, hotkey2, actions, to }) => {
  const f = useCurrentFrame();
  const newAt = page.cue('new');
  const nameAt = page.cue('name');
  const name = t('Work mode').slice(0, Math.max(0, Math.floor((f - nameAt + 4) / 2.2)));
  const keyCues = [hotkey.cue('ctrl'), hotkey.cue('alt'), hotkey.cue('w')];
  const captureOn = f > hotkey.from + 8 && f < keyCues[2] + 6;
  const chip1 = ease(f, keyCues[2] + 4, keyCues[2] + 14);
  const variants = [
    { cue: hotkey2.cue('mouse'), keys: ['Mouse 5'] },
    { cue: hotkey2.cue('chord'), keys: ['Mouse 4', 'Mouse 1'] },
    { cue: hotkey2.cue('wheel'), keys: ['Ctrl', 'Wheel Up'] },
  ];
  const variantsIn = variants.map(v => pop(f, v.cue - 6, { damping: 14, stiffness: 150 }));
  const variantsHome = ease(f, hotkey2.to - 6, hotkey2.to + 20, SMOOTH);
  const hotkeys = [
    { keys: ['Ctrl', 'Alt', 'W'], fresh: inOut(f, keyCues[2] + 4, keyCues[2] + 50, 8) },
    ...variants.map(v => ({ keys: v.keys, fresh: inOut(f, hotkey2.to + 8, hotkey2.to + 50, 8) })).filter(() => f > hotkey2.to + 8),
  ].filter((_, i) => i > 0 || chip1 > 0);
  const rowCues = ['run', 'wait', 'window', 'volume', 'folder'].map(c => actions.cue(c));
  const shown = rowCues.map(at => glide(f, at - 6, 14));
  const hot = rowCues.reduce((h, at, i) => (f >= at - 6 && f < at + 30 ? i : h), -1);

  const cursorPath = [
    { at: 0, x: 1300, y: 700 },
    { at: newAt - 22, x: NEW_BTN.x, y: NEW_BTN.y, dur: 18 },
    { at: nameAt - 18, x: 1060, y: 190, dur: 14 },
    { at: hotkey.from - 2, x: ADD_HOTKEY.x, y: ADD_HOTKEY.y, dur: 12 },
    { at: actions.from - 2, x: ADD_ACTION.x, y: ADD_ACTION.y, dur: 14 },
    { at: rowCues[4] + 30, x: 1500, y: 800, dur: 20 },
  ];
  const clicks = [{ at: newAt - 2 }, { at: nameAt - 4 }, { at: hotkey.from + 8 }, ...rowCues.map(c => ({ at: c - 8 }))];

  const shots = [
    { at: 0, x: 960, y: 540, s: 1.0, ry: 6, rx: 4 },
    { at: 4, dur: 40, x: 960, y: 540, s: 1.02, ry: 0, rx: 0 },
    { at: newAt - 30, dur: 26, x: 760, y: 360, s: 1.55 },
    { at: nameAt - 18, dur: 22, x: 1040, y: 300, s: 1.6 },
    { at: hotkey.from - 8, dur: 24, x: 1080, y: 320, s: 1.45 },
    { at: hotkey2.from - 6, dur: 30, x: 960, y: 540, s: 1.0 },
    { at: hotkey2.to - 4, dur: 26, x: 1100, y: 330, s: 1.45 },
    { at: actions.from - 6, dur: 30, x: 1200, y: 600, s: 1.28 },
    { at: to - 30, dur: 40, x: 1200, y: 600, s: 1.18 },
  ];
  const dim = inOut(f, hotkey2.from - 4, hotkey2.to + 6, 12);
  const pressNew = inOut(f, newAt - 2, newAt + 6, 3);
  return (
    <AbsoluteFill>
      <Camera shots={shots}>
        <div style={{ position: 'absolute', left: WIN.x, top: WIN.y, transform: `translateY(${(1 - glide(f, 0, 30)) * 80}px)` }}>
          <KdWindow page="commands">
            <CommandsPage list={COMMAND_LIST.slice(0, 9)} newPress={pressNew}
              fresh={{ name: f > nameAt ? name || t('New command') : t('New command'), sub: f > rowCues[0] ? 'notepad.exe' : '', hotkey: chip1 > 0 ? t('CTRL + ALT + W') : undefined, icon: 'flash_24_filled', color: 'gold', show: glide(f, newAt, 14) }}
              editor={f > newAt ? {
                name: f > nameAt - 4 ? name : t('New command'), caret: f > nameAt - 4 && f < nameAt + 40,
                hotkeys, capturing: captureOn, actions: WORK_MODE, shown, hot, addAction: Math.max(...rowCues.map(c => inOut(f, c - 8, c, 3))),
              } : undefined} />
          </KdWindow>
        </div>
        <Cursor path={cursorPath} clicks={clicks} />
      </Camera>

      {/* Ctrl + Alt + W */}
      <ComboKeys keys={['Ctrl', 'Alt', 'W']} at={keyCues} release={keyCues[2] + 16} show={inOut(f, keyCues[0] - 10, keyCues[2] + 26, 8)} />

      {/* Other kinds of hotkeys */}
      <AbsoluteFill style={{ background: `rgba(8,8,12,${0.6 * dim})` }} />
      <div style={{ position: 'absolute', left: 0, right: 0, top: 230, display: 'flex', justifyContent: 'center', gap: 70 }}>
        {variants.map((v, i) => {
          const k = variantsIn[i] * (1 - variantsHome);
          const lit = inOut(f, v.cue - 4, v.cue + 40, 6);
          return (
            <div key={i} style={{
              width: 400, height: 470, borderRadius: 28, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${lit > 0.1 ? 'rgba(242,193,74,0.7)' : C.line2}`,
              display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'space-between', padding: '40px 0 34px', boxSizing: 'border-box',
              opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - variantsIn[i]) * 120 - variantsHome * 120}px) scale(${0.7 + 0.3 * k})`,
              boxShadow: lit > 0.1 ? `0 0 50px rgba(242,193,74,${0.25 * lit})` : '0 20px 50px rgba(0,0,0,0.5)',
            }}>
              <div style={{ height: 300, display: 'flex', alignItems: 'center', gap: 18 }}>
                {i === 0 && <MouseShape scale={0.8} side={inOut(f, v.cue + 6, v.cue + 14, 3)} glow={lit} />}
                {i === 1 && <MouseShape scale={0.8} glow={lit} left={inOut(f, v.cue + 10, v.cue + 30, 4)} side={lit} />}
                {i === 2 && (
                  <>
                    <Keycap label={t('Ctrl')} height={86} width={120} size={28} down={lit} />
                    <MouseShape scale={0.62} wheel={Math.max(0, f - v.cue) / 8} />
                  </>
                )}
              </div>
              <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 700, color: C.accentText }}>{v.keys.join(' + ')}</div>
            </div>
          );
        })}
      </div>

      <Sfx at={newAt - 2} name="click" volume={0.3} />
      {Array.from({ length: 9 }, (_, i) => <Sfx key={i} at={nameAt - 4 + i * 2.2} name="key" volume={0.12} />)}
      <Sfx at={hotkey.from + 8} name="click" volume={0.3} />
      {keyCues.map(c => <Sfx key={c} at={c - 3} name="key" volume={0.5} />)}
      <Sfx at={keyCues[2] + 6} name="chime" volume={0.2} />
      {variants.map(v => <Sfx key={v.cue} at={v.cue - 6} name="pop" volume={0.25} />)}
      <Sfx at={hotkey2.to - 4} name="whoosh-short" volume={0.25} />
      {rowCues.map(c => <Sfx key={c} at={c - 6} name="pop" volume={0.22} />)}
    </AbsoluteFill>
  );
};

/** Press Ctrl + Alt + W on the desktop: every action runs, in order. */
export const RunPart: React.FC<{ run: BeatTimes }> = ({ run }) => {
  const f = useCurrentFrame();
  const keyCues = [run.cue('ctrl'), run.cue('alt'), run.cue('w')];
  const t0 = keyCues[2] + 6;
  const steps = [t0, t0 + 10, t0 + 30, t0 + 42, t0 + 52];
  const notepad = pop(f, steps[0], { damping: 15, stiffness: 170 });
  const snap = glide(f, steps[2], 12);
  const np = { x: tween(f, steps[2], steps[2] + 12, 560, 8, OUT), y: tween(f, steps[2], steps[2] + 12, 200, 8, OUT), w: tween(f, steps[2], steps[2] + 12, 800, 944, OUT), h: tween(f, steps[2], steps[2] + 12, 520, 1008, OUT) };
  const osd = inOut(f, steps[3], steps[3] + 70, 8);
  const explorer = pop(f, steps[4], { damping: 15, stiffness: 170 });
  const waitRing = ease(f, steps[1], steps[2]);
  const shots = [
    { at: 0, x: 960, y: 540, s: 1.12, rx: 6 },
    { at: run.from, dur: 60, x: 960, y: 540, s: 1.0, rx: 0 },
    { at: steps[4] + 20, dur: 80, x: 960, y: 540, s: 1.05, rx: 0 },
  ];
  return (
    <AbsoluteFill>
      <Camera shots={shots}>
        <Wallpaper />
        <div style={{ position: 'absolute', left: np.x, top: np.y, transform: `scale(${notepad})`, opacity: Math.min(1, notepad * 2), transformOrigin: '50% 50%' }}>
          <AppWindow kind="notepad" width={np.w} height={np.h} style={{ position: 'relative' }} focus={snap < 1 || explorer < 0.5} />
        </div>
        <div style={{ position: 'absolute', left: 968, top: 8, transform: `scale(${explorer})`, opacity: Math.min(1, explorer * 2) }}>
          <AppWindow kind="explorer" width={944} height={1008} style={{ position: 'relative' }} />
        </div>
        <Taskbar apps={[{ icon: 'folder_24_filled', color: 'yellow', active: explorer > 0.5 }, { icon: 'globe_24_filled', color: 'blue' }, { icon: 'document_text_24_filled', color: 'sky', active: notepad > 0.5 }, { icon: 'music_note_2_24_filled', color: 'pink' }]} />
        <div style={{ position: 'absolute', left: 960, top: 880, transform: 'translateX(-50%)' }}>
          <Osd icon="speaker_2_24_filled" color="teal" text={t('Volume: 20 %')} bar={20} show={osd} />
        </div>
      </Camera>

      {/* What ran */}
      <div style={{ position: 'absolute', right: 70, top: 120, width: 400, padding: 22, borderRadius: 22, background: 'rgba(24,24,30,0.9)', border: `1px solid ${C.line2}`, opacity: inOut(f, t0 - 6, run.to + 60, 10), boxShadow: '0 20px 60px rgba(0,0,0,0.5)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 14 }}>
          <KdIcon icon="flash_24_filled" color="gold" size={40} />
          <div style={{ fontFamily: SANS, fontSize: 24, fontWeight: 700, color: C.text }}>{t('Work mode')}</div>
          <div style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 15, color: C.accentText, fontWeight: 600 }}>{t('Ctrl + Alt + W')}</div>
        </div>
        {WORK_MODE.map((a, i) => {
          const done = f > steps[i] + (i === 1 ? 20 : 4);
          const running = f > steps[i] && !done;
          return (
            <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 12, height: 46, opacity: f > steps[i] - 2 ? 1 : 0.35 }}>
              <div style={{ width: 26, height: 26, borderRadius: 13, display: 'flex', alignItems: 'center', justifyContent: 'center', background: done ? C.success : running ? C.accentText : 'rgba(255,255,255,0.1)' }}>
                {done ? <Fluent name="checkmark_24_filled" size={16} color="#0b0b0e" /> : i === 1 && running ? (
                  <svg width="26" height="26" viewBox="0 0 26 26"><circle cx="13" cy="13" r="10" stroke="#1a1306" strokeWidth="3" fill="none" strokeDasharray={`${63 * waitRing} 63`} transform="rotate(-90 13 13)" /></svg>
                ) : null}
              </div>
              <KdIcon icon={a.icon} color={a.color} size={30} />
              <div style={{ fontFamily: SANS, fontSize: 18, fontWeight: 600, color: C.text }}>{a.name}</div>
              <div style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 15, color: C.dim }}>{a.detail}</div>
            </div>
          );
        })}
      </div>

      <ComboKeys keys={['Ctrl', 'Alt', 'W']} at={keyCues} release={keyCues[2] + 10} show={inOut(f, keyCues[0] - 12, keyCues[2] + 22, 8)} />
      {keyCues.map(c => <Sfx key={c} at={c - 3} name="key" volume={0.55} />)}
      <Sfx at={steps[0]} name="pop" volume={0.35} />
      <Sfx at={steps[2]} name="whoosh-short" volume={0.3} />
      <Sfx at={steps[3]} name="blip" volume={0.3} />
      <Sfx at={steps[4]} name="pop" volume={0.35} />
      <Sfx at={steps[4] + 14} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};

