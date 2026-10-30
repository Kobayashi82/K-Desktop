import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { FAMILIES_NOW } from '../../../src/components/ActionFamilies';
import { Camera, Shot } from '../../../src/components/Camera';
import { CaptureShot } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { ease, glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { BeatTimes, useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

const COL_W = 384;
const colX = (i: number) => 120 + i * 420;
const WALL_W = colX(FAMILIES_NOW.length) + 100;
const TOP = 280;
// Which beat and cue names each family.
const CUE: Record<string, [string, string]> = {
  apps: ['fam1', 'apps'], windows: ['fam1', 'windows'], input: ['fam1', 'input'],
  sound: ['fam2', 'sound'], screen: ['fam2', 'screen'], network: ['fam2', 'network'],
  calendar: ['fam3', 'calendar'], system: ['fam3', 'system'], flow: ['fam3', 'flow'], assistants: ['fam3', 'assistants'],
};

/** Every action as K-Desktop lists them, one column per family; the camera travels along as each family is named. */
const WallPart: React.FC<{ B: (id: string) => BeatTimes; from: number; to: number }> = ({ B, from, to }) => {
  const f = useCurrentFrame();
  const at = FAMILIES_NOW.map(fam => B(CUE[fam.id][0]).cue(CUE[fam.id][1]));
  const current = at.reduce((c, a, i) => (f >= a - 8 ? i : c), -1);
  const focusX = (i: number) => Math.min(colX(i) + COL_W / 2 + 460, WALL_W - 960 / 1.06);
  const shots: Shot[] = [
    { at: from, x: WALL_W / 2, y: 560, s: 0.42, rx: 8 },
    { at: from + 4, dur: 30, x: WALL_W / 2, y: 540, s: 0.44, rx: 0 },
    ...at.map((a, i) => ({ at: a - 18, dur: 26, x: focusX(i), y: 545, s: 1.06 })),
    { at: to - 50, dur: 46, x: WALL_W / 2, y: 545, s: 0.44 },
  ];
  return (
    <AbsoluteFill>
      <Camera shots={shots} width={WALL_W} height={1080} clamp>
        {FAMILIES_NOW.map((fam, i) => {
          const lit = current === i;
          const seen = f >= at[i] - 8;
          const k = pop(f, from + 4 + i * 3, { damping: 15 });
          return (
            <div key={fam.id} style={{
              position: 'absolute', left: colX(i), top: TOP, width: COL_W, padding: 22, boxSizing: 'border-box', borderRadius: 26,
              background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))',
              border: `1.5px solid ${lit ? 'rgba(242,193,74,0.75)' : C.line2}`, boxShadow: lit ? '0 0 60px rgba(242,193,74,0.25)' : '0 20px 50px rgba(0,0,0,0.45)',
              opacity: Math.min(1, k * 1.4) * (lit || current < 0 || f > to - 50 ? 1 : 0.5), transform: `translateY(${(1 - k) * 80}px) scale(${lit ? 1.02 : 1})`,
            }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 18 }}>
                <KdIcon icon={fam.icon} color={fam.color} size={60} glow={lit ? 0.5 : 0} />
                <div>
                  <div style={{ fontFamily: SANS, fontSize: 15, fontWeight: 700, letterSpacing: 2, color: C.accentText }}>{String(i + 1).padStart(2, '0')}</div>
                  <div style={{ fontFamily: SANS, fontSize: 27, fontWeight: 800, color: C.text, whiteSpace: 'nowrap' }}>{fam.name}</div>
                </div>
              </div>
              {fam.actions.map((a, j) => {
                const r = seen ? glide(f, at[i] - 6 + j * 3, 12) : 1;
                return (
                  <div key={a.name} style={{
                    display: 'flex', alignItems: 'center', gap: 14, height: 54, padding: '0 12px', marginBottom: 6, borderRadius: 12,
                    background: lit ? 'rgba(255,255,255,0.06)' : 'rgba(255,255,255,0.03)', opacity: 0.4 + 0.6 * r, transform: `translateX(${(1 - r) * -20}px)`,
                  }}>
                    <KdIcon icon={a.icon} color={a.color} size={36} />
                    <span style={{ fontFamily: SANS, fontSize: 20, fontWeight: 600, color: C.text, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{a.name}</span>
                  </div>
                );
              })}
            </div>
          );
        })}
      </Camera>
      {at.map(a => <Sfx key={a} at={a - 8} name="whoosh-short" volume={0.18} />)}
    </AbsoluteFill>
  );
};

/** Chapter «Every kind of action»: the real «Add action» window, then the wall of every family. */
export const Catalog: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const count = B('count');
  const wallFrom = B('fam1').from - 10;
  return (
    <AbsoluteFill>
      <Part from={0} to={wallFrom} first>
        <CaptureShot name="action-picker" frame
          shots={[{ at: 0, x: 470, y: 330, s: 1.2, rx: 6 }, { at: 4, dur: 40, x: 470, y: 330, s: 1.32, rx: 0 }, { at: count.cue('count') - 16, dur: 20, x: 620, y: 360, s: 1.2 }, { at: count.cue('search') - 14, dur: 20, x: 470, y: 330, s: 1.32 }]}
          focus={[
            { from: count.cue('count') - 6, to: count.cue('ten') - 4, rect: [228, 100, 610, 556], label: t('More than 50 actions'), at: 'right' },
            { from: count.cue('ten') - 4, to: count.cue('search') - 4, rect: [16, 100, 198, 360], label: t('10 families'), at: 'right' },
            { from: count.cue('search') - 4, to: wallFrom + 10, rect: [16, 52, 908, 36], label: t('Search'), at: 'bottom' },
          ]} />
      </Part>
      <Part from={wallFrom} to={durationInFrames} last><WallPart B={B} from={wallFrom} to={durationInFrames} /></Part>
      <Sfx at={count.cue('count') - 6} name="blip" volume={0.18} />
      <Sfx at={count.cue('ten') - 4} name="blip" volume={0.18} />
      <Sfx at={count.cue('search') - 4} name="blip" volume={0.18} />
    </AbsoluteFill>
  );
};
