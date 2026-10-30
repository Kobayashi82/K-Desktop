import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../../../src/components/Camera';
import { FocusLayer, useCapture } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Cursor } from '../../../src/components/Cursor';
import { AppWindow, Taskbar, Wallpaper } from '../../../src/components/Desktop';
import { Osd } from '../../../src/components/Osd';
import { MouseShape } from '../../../src/components/Props';
import { Sfx } from '../../../src/components/Sfx';
import { Callout } from '../../../src/components/Ui';
import { ease, inOut, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes, useBeats, useChapter } from '../../../src/lib/timeline';
import { AdminPart, OptionsPart } from '../../../src/scenes/commands/Extras';

/** A hot corner: the pointer rests in the top-left corner, the corner fills up, and the command runs. */
const CornerPart: React.FC<{ b: BeatTimes; from: number }> = ({ b, from }) => {
  const f = useCurrentFrame();
  const arrive = b.cue('corner') - 4;
  const fill = ease(f, arrive, arrive + 24);
  const fired = arrive + 26;
  return (
    <AbsoluteFill>
      <Camera shots={[{ at: from, x: 960, y: 540, s: 1.0 }, { at: arrive - 30, dur: 30, x: 835, y: 470, s: 1.15 }, { at: fired + 10, dur: 40, x: 960, y: 540, s: 1.0 }]}>
        <Wallpaper />
        <AppWindow kind="browser" width={1200} height={760} style={{ left: 380, top: 110 }} />
        <Taskbar apps={[{ icon: 'folder_24_filled', color: 'yellow' }, { icon: 'globe_24_filled', color: 'blue', active: true }]} />
        <svg width="400" height="400" style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }}>
          <defs>
            <radialGradient id="corner-glow" cx="0" cy="0" r="1">
              <stop offset="0" stopColor="#F2C14A" stopOpacity="0.8" />
              <stop offset="1" stopColor="#F2C14A" stopOpacity="0" />
            </radialGradient>
          </defs>
          <circle cx="0" cy="0" r={60 + 240 * fill} fill="url(#corner-glow)" opacity={inOut(f, arrive, fired + 40, 6)} />
          <circle cx="0" cy="0" r="70" fill="none" stroke="#F2C14A" strokeWidth="6" strokeDasharray={`${110 * fill} 200`} opacity={inOut(f, arrive, fired + 10, 4)} />
        </svg>
        <Cursor path={[{ at: from, x: 1100, y: 700 }, { at: arrive - 26, x: 4, y: 4, dur: 24 }, { at: fired + 30, x: 700, y: 500, dur: 30 }]} />
      </Camera>
      <div style={{ position: 'absolute', left: 960, top: 820, transform: 'translateX(-50%)' }}>
        <Osd icon="flash_24_filled" color="gold" text={t('Work mode')} sub={t('Corner ↖')} show={inOut(f, fired, b.to + 20, 8)} />
      </div>
      <Callout show={inOut(f, from + 20, arrive - 6, 8)} x={960} y={110} align="center" size={28}>{t('Rest the mouse in a corner')}</Callout>
      <Sfx at={arrive} name="rise" volume={0.18} />
      <Sfx at={fired} name="chime" volume={0.25} />
    </AbsoluteFill>
  );
};

// The real «Mouse gesture» window (760 × 913), on the right.
const DLG = { x: 1060, y: 70, s: 0.92 };

/** A mouse gesture: hold the right button and draw a C; the real window with every drawing, and «Draw your own». */
const GesturePart: React.FC<{ b: BeatTimes; from: number }> = ({ b, from }) => {
  const f = useCurrentFrame();
  const cap = useCapture('mouse-gesture');
  const letter = b.cue('letter');
  const own = b.cue('own');
  const dlg = pop(f, from + 6, { damping: 15, stiffness: 120 });
  const draw = ease(f, b.cue('gesture') + 6, b.cue('gesture') + 40);
  const held = inOut(f, b.cue('gesture'), b.cue('gesture') + 48, 4);
  // A «C»: an arc from the top right, round the left, to the bottom right.
  const cx = 560;
  const cy = 560;
  const r = 170;
  const a0 = -Math.PI / 4;
  const sweep = (3 * Math.PI) / 2;
  const end = a0 - sweep * draw;
  const tip = { x: cx + r * Math.cos(end), y: cy + r * Math.sin(end) };
  const arc = `M ${cx + r * Math.cos(a0)} ${cy + r * Math.sin(a0)} A ${r} ${r} 0 ${sweep * draw > Math.PI ? 1 : 0} 0 ${tip.x} ${tip.y}`;
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 90, top: 330 }}>
        <MouseShape scale={0.85} right={held} />
      </div>
      <svg width="1000" height="1080" style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }}>
        {draw > 0 && <path d={arc} fill="none" stroke="#F2C14A" strokeWidth="16" strokeLinecap="round" style={{ filter: 'drop-shadow(0 0 14px rgba(242,193,74,0.7))' }} opacity={1 - ease(f, letter + 40, letter + 60)} />}
      </svg>
      {draw > 0 && f < letter + 60 && <Cursor path={[{ at: 0, x: tip.x, y: tip.y }]} />}
      <div style={{
        position: 'absolute', left: DLG.x, top: DLG.y, width: cap.width, height: cap.height, transformOrigin: '0 0', transform: `scale(${DLG.s * (0.9 + 0.1 * dlg)})`,
        opacity: Math.min(1, dlg * 1.4), borderRadius: 8, overflow: 'hidden', boxShadow: '0 40px 100px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.12)',
      }}>
        <Img src={staticFile(cap.file)} style={{ width: cap.width, height: cap.height, display: 'block' }} />
        <FocusLayer width={cap.width} height={cap.height} focus={[
          { from: b.cue('gesture') + 10, to: letter - 6, rect: [20, 46, 600, 32] },
          { from: letter - 6, to: own - 6, rect: [408, 107, 88, 80] },
          { from: own - 6, to: b.to + 30, rect: [24, 398, 88, 80] },
        ]} />
      </div>
      <Sfx at={b.cue('gesture') + 6} name="whoosh-short" volume={0.25} />
      <Sfx at={letter - 6} name="chime" volume={0.2} />
      <Sfx at={own - 6} name="blip" volume={0.2} />
    </AbsoluteFill>
  );
};

/** Chapter «Hotkeys and the mouse»: hot corners, gestures, the options and administrator rights. */
export const Launch: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const corner = B('corner');
  const gesture = B('gesture');
  const options = B('options');
  const admin = B('admin');
  return (
    <AbsoluteFill>
      <Part from={0} to={gesture.from - 6} first><CornerPart b={corner} from={0} /></Part>
      <Part from={gesture.from - 6} to={options.from - 6}><GesturePart b={gesture} from={gesture.from - 6} /></Part>
      <Part from={options.from - 6} to={admin.from - 6}><OptionsPart b={options} /></Part>
      <Part from={admin.from - 6} to={durationInFrames} last><AdminPart b={admin} /></Part>
    </AbsoluteFill>
  );
};
