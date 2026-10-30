import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../../../src/components/Camera';
import { FocusLayer, useCapture } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { ActionRow } from '../../../src/components/CommandCard';
import { CommandsPage } from '../../../src/components/CommandsPage';
import { Cursor } from '../../../src/components/Cursor';
import { AppWindow, Taskbar, Wallpaper } from '../../../src/components/Desktop';
import { KdWindow, NAV_CURRENT } from '../../../src/components/KdWindow';
import { Sfx } from '../../../src/components/Sfx';
import { Callout } from '../../../src/components/Ui';
import { ease, glide, inOut, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes, useBeats, useChapter } from '../../../src/lib/timeline';
import { ComboKeys } from '../../../src/scenes/commands/Build';

const WIN = { x: 180, y: 70 };
// Stage points of the page (as in the long video's «Work mode»): New command, Add hotkey, Add action, Run.
const NEW_BTN = { x: 590, y: 312 };
const ADD_HOTKEY = { x: 880, y: 326 };
const ADD_ACTION = { x: 1620, y: 508 };
const RUN_BTN = { x: 1531, y: 188 };
// The real «Add action» window (940 × 660), over the page, and its «Run application» card.
const PICKER = { x: 420, y: 160, s: 1.15 };
const RUN_CARD = { x: PICKER.x + (228 + 148) * PICKER.s, y: PICKER.y + (128 + 36) * PICKER.s };

const NOTEPAD: ActionRow[] = [{ icon: 'open_24_filled', color: 'blue', name: t('Run application'), detail: t('notepad.exe') }];

/** The real «Add action» window popping over the page, «Run application» lit. */
const Picker: React.FC<{ show: number; from: number; to: number }> = ({ show, from, to }) => {
  const cap = useCapture('action-picker');
  if (show <= 0) return null;
  return (
    <div style={{
      position: 'absolute', left: PICKER.x, top: PICKER.y, width: cap.width, height: cap.height, transformOrigin: '0 0',
      transform: `scale(${PICKER.s * (0.92 + 0.08 * show)})`, opacity: show, borderRadius: 8, overflow: 'hidden',
      boxShadow: '0 40px 100px rgba(0,0,0,0.7), 0 0 0 1px rgba(255,255,255,0.12)',
    }}>
      <Img src={staticFile(cap.file)} style={{ width: cap.width, height: cap.height, display: 'block' }} />
      <FocusLayer width={cap.width} height={cap.height} focus={[{ from, to, rect: [228, 128, 296, 72] }]} />
    </div>
  );
};

/** New command, its name, its hotkey, its action, and Run. */
const BuildPart: React.FC<{ b: { new: BeatTimes; hotkey: BeatTimes; action: BeatTimes; try: BeatTimes }; to: number }> = ({ b, to }) => {
  const f = useCurrentFrame();
  const newAt = b.new.cue('new');
  const nameAt = b.new.cue('name');
  const name = t('Notepad').slice(0, Math.max(0, Math.floor((f - nameAt + 4) / 2.6)));
  const keyCues = [b.hotkey.cue('ctrl'), b.hotkey.cue('alt'), b.hotkey.cue('n')];
  const captureOn = f > b.hotkey.from + 8 && f < keyCues[2] + 6;
  const chip = ease(f, keyCues[2] + 4, keyCues[2] + 14);
  const addAt = b.action.cue('add');
  const runCardAt = b.action.cue('run');
  const notepadAt = b.action.cue('notepad');
  const iconAt = b.action.cue('icon');
  const runAt = b.try.cue('run');
  const picker = inOut(f, addAt - 2, notepadAt - 4, 8);
  const row = glide(f, notepadAt - 4, 14);
  const iconPop = pop(f, iconAt - 4, { damping: 10, stiffness: 170 });
  const hasIcon = f >= iconAt - 4;
  const icon = hasIcon ? { icon: 'document_text_24_filled', color: 'sky' } : { icon: 'flash_24_filled', color: 'gold' };
  const notepad = pop(f, runAt + 8, { damping: 15, stiffness: 170 });

  const cursor = [
    { at: b.new.from, x: 1300, y: 700 },
    { at: newAt - 22, x: NEW_BTN.x, y: NEW_BTN.y, dur: 18 },
    { at: nameAt - 18, x: 1060, y: 190, dur: 14 },
    { at: b.hotkey.from - 2, x: ADD_HOTKEY.x, y: ADD_HOTKEY.y, dur: 12 },
    { at: addAt - 22, x: ADD_ACTION.x, y: ADD_ACTION.y, dur: 16 },
    { at: runCardAt - 16, x: RUN_CARD.x, y: RUN_CARD.y, dur: 12 },
    { at: notepadAt + 4, x: 1300, y: 640, dur: 18 },
    { at: runAt - 24, x: RUN_BTN.x, y: RUN_BTN.y, dur: 18 },
  ];
  const clicks = [{ at: newAt - 2 }, { at: nameAt - 4 }, { at: b.hotkey.from + 8 }, { at: addAt - 2 }, { at: runCardAt + 2 }, { at: runAt - 2 }];
  const shots = [
    { at: 0, x: 960, y: 540, s: 0.94, ry: 6, rx: 4 },
    { at: 4, dur: 40, x: 960, y: 540, s: 1.0, ry: 0, rx: 0 },
    { at: newAt - 30, dur: 26, x: 760, y: 360, s: 1.5 },
    { at: nameAt - 18, dur: 22, x: 1040, y: 300, s: 1.55 },
    { at: b.hotkey.from - 8, dur: 24, x: 1080, y: 330, s: 1.4 },
    { at: addAt - 24, dur: 26, x: 960, y: 520, s: 1.05 },
    { at: notepadAt, dur: 26, x: 1180, y: 460, s: 1.35 },
    { at: iconAt - 16, dur: 24, x: 760, y: 330, s: 1.5 },
    { at: runAt - 30, dur: 26, x: 1300, y: 340, s: 1.3 },
    { at: runAt + 10, dur: 30, x: 1100, y: 520, s: 1.05 },
  ];
  return (
    <AbsoluteFill>
      <Camera shots={shots}>
        <div style={{ position: 'absolute', left: WIN.x, top: WIN.y }}>
          <KdWindow page="commands" nav={NAV_CURRENT} assistant={false}>
            <CommandsPage list={[]} newPress={inOut(f, newAt - 2, newAt + 6, 3)}
              fresh={{ name: f > nameAt ? name || t('New command') : t('New command'), sub: f > notepadAt ? 'notepad.exe' : '', hotkey: chip > 0 ? t('CTRL + ALT + N') : undefined, ...icon, show: glide(f, newAt, 14) }}
              editor={f > newAt ? {
                name: f > nameAt - 4 ? name : t('New command'), caret: f > nameAt - 4 && f < nameAt + 40, ...icon,
                hotkeys: chip > 0 ? [{ keys: ['Ctrl', 'Alt', 'N'], fresh: inOut(f, keyCues[2] + 4, keyCues[2] + 50, 8) }] : [],
                capturing: captureOn, actions: NOTEPAD, shown: [row], hot: f > notepadAt - 4 && f < notepadAt + 40 ? 0 : -1,
                addAction: inOut(f, addAt - 4, addAt + 4, 3),
              } : undefined} />
          </KdWindow>
        </div>
        {/* «It even picks up the program's icon»: a ring on the two places it shows */}
        {[{ x: WIN.x + 250 + 24 + 12 + 20, y: WIN.y + 46 + 22 + 54 + 52 + 52 + 58 + 35 }, { x: WIN.x + 250 + 378 + 18 + 32, y: WIN.y + 46 + 22 + 18 + 32 }].map((p, i) => (
          <div key={i} style={{
            position: 'absolute', left: p.x - 46, top: p.y - 46, width: 92, height: 92, borderRadius: 24, border: '3px solid #F2C14A',
            opacity: inOut(f, iconAt - 4, iconAt + 34, 6), transform: `scale(${0.7 + 0.3 * iconPop})`, boxShadow: '0 0 30px rgba(242,193,74,0.6)',
          }} />
        ))}
        <Picker show={picker} from={runCardAt - 10} to={notepadAt} />
        {/* Run: Notepad opens */}
        <div style={{ position: 'absolute', left: 1000, top: 330, transform: `scale(${notepad})`, opacity: Math.min(1, notepad * 2), transformOrigin: '50% 30%' }}>
          <AppWindow kind="notepad" width={760} height={480} style={{ position: 'relative' }} />
        </div>
        <Cursor path={cursor} clicks={clicks} />
      </Camera>

      <ComboKeys keys={['Ctrl', 'Alt', 'N']} at={keyCues} release={keyCues[2] + 16} show={inOut(f, keyCues[0] - 10, keyCues[2] + 26, 8)} />
      <Callout show={inOut(f, iconAt - 2, iconAt + 40, 8)} x={960} y={90} align="center" size={28}>{t('Its icon, by itself')}</Callout>

      <Sfx at={newAt - 2} name="click" volume={0.3} />
      {Array.from({ length: 7 }, (_, i) => <Sfx key={i} at={nameAt - 4 + i * 2.6} name="key" volume={0.12} />)}
      <Sfx at={b.hotkey.from + 8} name="click" volume={0.3} />
      {keyCues.map(c => <Sfx key={c} at={c - 3} name="key" volume={0.5} />)}
      <Sfx at={keyCues[2] + 6} name="chime" volume={0.2} />
      <Sfx at={addAt - 2} name="click" volume={0.3} />
      <Sfx at={addAt} name="whoosh-short" volume={0.2} />
      <Sfx at={runCardAt + 2} name="click" volume={0.3} />
      <Sfx at={notepadAt - 4} name="pop" volume={0.25} />
      <Sfx at={iconAt - 4} name="pop-high" volume={0.2} />
      <Sfx at={runAt - 2} name="click" volume={0.3} />
      <Sfx at={runAt + 8} name="pop" volume={0.3} />
      <Sfx at={to} name="whoosh" volume={0.2} />
    </AbsoluteFill>
  );
};

/** From anywhere: Ctrl + Alt + N over the browser, and Notepad is there. */
const AnywherePart: React.FC<{ b: BeatTimes; from: number }> = ({ b, from }) => {
  const f = useCurrentFrame();
  const keyCues = [b.cue('ctrl'), b.cue('alt'), b.cue('n')];
  const notepad = pop(f, keyCues[2] + 6, { damping: 15, stiffness: 170 });
  return (
    <AbsoluteFill>
      <Camera shots={[{ at: from, x: 960, y: 540, s: 1.1 }, { at: from, dur: 90, x: 960, y: 540, s: 1.0 }]}>
        <Wallpaper />
        <AppWindow kind="browser" width={1300} height={800} style={{ left: 120, top: 60 }} focus={notepad < 0.5} />
        <div style={{ position: 'absolute', left: 820, top: 240, transform: `scale(${notepad})`, opacity: Math.min(1, notepad * 2) }}>
          <AppWindow kind="notepad" width={900} height={600} style={{ position: 'relative' }} />
        </div>
        <Taskbar apps={[{ icon: 'folder_24_filled', color: 'yellow' }, { icon: 'globe_24_filled', color: 'blue', active: notepad < 0.5 }, { icon: 'document_text_24_filled', color: 'sky', active: notepad > 0.5 }]} />
      </Camera>
      <ComboKeys keys={['Ctrl', 'Alt', 'N']} at={keyCues} release={keyCues[2] + 10} show={inOut(f, keyCues[0] - 12, keyCues[2] + 26, 8)} />
      {keyCues.map(c => <Sfx key={c} at={c - 3} name="key" volume={0.55} />)}
      <Sfx at={keyCues[2] + 6} name="pop" volume={0.35} />
      <Sfx at={keyCues[2] + 18} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};

/** Chapter «Your first command». */
export const First: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const b = { new: B('new'), hotkey: B('hotkey'), action: B('action'), try: B('try') };
  const split = b.try.cue('anywhere') - 8;
  return (
    <AbsoluteFill>
      <Part from={0} to={split} first><BuildPart b={b} to={split} /></Part>
      <Part from={split} to={durationInFrames} last><AnywherePart b={b.try} from={split} /></Part>
    </AbsoluteFill>
  );
};
