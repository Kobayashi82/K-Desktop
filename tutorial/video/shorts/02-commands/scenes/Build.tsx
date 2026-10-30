import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { NAV_CURRENT } from '../../../src/components/KdWindow';
import { Sfx } from '../../../src/components/Sfx';
import { ease } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes, useBeats, useChapter } from '../../../src/lib/timeline';
import { BuildPart, RunPart } from '../../../src/scenes/commands/Build';
import { IconsPart } from '../../../src/scenes/commands/Extras';

// The action rows of the real Commands page (tutorial/captures/en/commands.png): each row's centre and its buttons.
const ROW_Y = [469, 534, 599, 664, 729, 794];
const BTN = { check: 1703, test: 1742, copy: 1770, up: 1798, down: 1826 };

/** On the real page: test one action, copy it, move it, switch it off (drawn over the screenshot). */
const RowsPart: React.FC<{ b: BeatTimes; from: number }> = ({ b, from }) => {
  const f = useCurrentFrame();
  const at = { test: b.cue('test'), copy: b.cue('copy'), move: b.cue('move'), off: b.cue('off') };
  const off = ease(f, at.off + 2, at.off + 8);
  const box = (x: number, y: number, w = 30): [number, number, number, number] => [x - w / 2, y - 16, w, 32];
  return (
    <CaptureShot name="commands"
      shots={[{ at: from, x: 1100, y: 620, s: 1.0 }, { at: from + 6, dur: 34, x: 1320, y: 630, s: 1.55 }]}
      focus={[
        { from: at.test - 6, to: at.copy - 4, rect: box(BTN.test, ROW_Y[0]), label: t('Test only this action'), at: 'left' },
        { from: at.copy - 4, to: at.move - 2, rect: box(BTN.copy, ROW_Y[0]), label: t('Copy'), at: 'left' },
        { from: at.move - 2, to: at.off - 4, rect: [BTN.up - 15, ROW_Y[1] - 16, BTN.down - BTN.up + 30, 32], label: t('Move'), at: 'left' },
        { from: at.off - 4, to: b.to + 30, rect: box(BTN.check, ROW_Y[3]), label: t('Switch off'), at: 'left' },
      ]}
      cursor={[
        { at: from, x: 1500, y: 900 },
        { at: at.test - 16, x: BTN.test, y: ROW_Y[0], dur: 12 },
        { at: at.copy - 12, x: BTN.copy, y: ROW_Y[0], dur: 8 },
        { at: at.move - 12, x: BTN.up, y: ROW_Y[1], dur: 8 },
        { at: at.off - 14, x: BTN.check, y: ROW_Y[3], dur: 10 },
      ]}
      clicks={[{ at: at.test - 2 }, { at: at.off }]}>
      {/* The fourth action, switched off: its box empties and the row fades */}
      <div style={{ position: 'absolute', left: 577, top: ROW_Y[3] - 28, width: 1303, height: 56, borderRadius: 8, background: `rgba(32,32,36,${0.55 * off})` }} />
      <div style={{
        position: 'absolute', left: BTN.check - 12, top: ROW_Y[3] - 12, width: 24, height: 24, borderRadius: 6, boxSizing: 'border-box',
        background: '#2b2b30', border: '1.5px solid rgba(255,255,255,0.6)', opacity: off,
      }} />
      <Sfx at={at.test - 2} name="click" volume={0.25} />
      <Sfx at={at.off} name="click" volume={0.25} />
    </CaptureShot>
  );
};

/** Chapter «Build a command»: the real page, then the long video's animated build, the run, the rows and the icons. */
export const Build: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const page = B('page');
  const run = B('run');
  const rows = B('rows');
  const icons = B('icons');
  const newAt = page.cue('new');
  return (
    <AbsoluteFill>
      <Part from={0} to={newAt - 2} first>
        <CaptureShot name="commands"
          shots={[{ at: 0, x: 960, y: 540, s: 1.0, rx: 6 }, { at: 10, dur: 50, x: 960, y: 540, s: 1.04, rx: 0 }, { at: newAt - 40, dur: 34, x: 520, y: 330, s: 1.7 }]}
          focus={[{ from: newAt - 30, to: newAt + 10, rect: [255, 216, 252, 40] }]}
          cursor={[{ at: 0, x: 1200, y: 700 }, { at: newAt - 36, x: 390, y: 238, dur: 26 }]} clicks={[{ at: newAt - 6 }]} />
      </Part>
      <Part from={newAt - 2} to={run.from - 6}>
        <BuildPart page={page} hotkey={B('hotkey')} hotkey2={B('hotkey2')} actions={B('actions')} to={run.from - 6} nav={NAV_CURRENT} />
      </Part>
      <Part from={run.from - 6} to={rows.from - 6}><RunPart run={run} /></Part>
      <Part from={rows.from - 6} to={icons.from - 6}><RowsPart b={rows} from={rows.from - 6} /></Part>
      <Part from={icons.from - 6} to={durationInFrames} last><IconsPart b={icons} /></Part>
    </AbsoluteFill>
  );
};
