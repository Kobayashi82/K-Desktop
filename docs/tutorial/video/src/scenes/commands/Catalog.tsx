import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { FAMILIES } from '../../components/ActionFamilies';
import { Camera, Shot } from '../../components/Camera';
import { FocusLayer, useCapture } from '../../components/Capture';
import { Img, staticFile } from 'remotion';
import { KdIcon } from '../../components/Icon';
import { Sfx } from '../../components/Sfx';
import { ease, inOut, OUT, pop, rand } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, SANS } from '../../theme';
import { DEMOS } from './Demos';
import { t } from '../../lib/i18n';

const COL_W = 400;
const COL_GAP = 40;
const colX = (i: number) => 200 + i * (COL_W + COL_GAP);
const STAGE_W = colX(FAMILIES.length) + 160;
const TOP = 280;

/** Which action of the family each cue of its beat names. */
const CUE_ACTION: Record<string, Record<string, number>> = {
  apps: { programs: 0, folders: 1, special: 1, web: 2, scripts: 3 },
  windows: { max: 1, halves: 1, quarters: 1, thirds: 1, center: 1, monitor: 1, transparent: 1, top: 2 },
  input: { keys: 0, clipboard: 1, macro: 2, kboard: 3, dictate: 4 },
  sound: { volume: 0, output: 1, play: 2, record: 3 },
  screen: { shot: 0, ocr: 1, magnifier: 2, brightness: 3, theme: 4 },
  network: { http: 0, wol: 1, radio: 2 },
  system: { hidden: 0, plan: 1, power: 2, countdown: 2 },
  kdesktop: { message: 0, wait: 2, menu: 4, commands: 3 },
};
const BEAT_OF: Record<string, string> = {
  apps: 'cat-apps', windows: 'cat-windows', input: 'cat-input', sound: 'cat-sound', screen: 'cat-screen', network: 'cat-network', system: 'cat-system', kdesktop: 'cat-flow',
};

/** The wall of every action, by family; the camera travels along it while each family is told, with its showcase. */
export const CatalogPart: React.FC<{ catalog: BeatTimes; beats: Record<string, BeatTimes>; to: number }> = ({ catalog, beats, to }) => {
  const f = useCurrentFrame();
  const families = FAMILIES.slice(0, 8);
  const ninth = beats.kdesktop.cue('ninth');

  // The family being told, and the action of it being named now.
  const current = families.findIndex(fam => f >= beats[fam.id].from - 6 && f < beats[fam.id].to + 6);
  const litAction = (famIndex: number): number => {
    const fam = families[famIndex];
    if (!fam) return -1;
    const b = beats[fam.id];
    let lit = -1;
    for (const [cue, action] of Object.entries(CUE_ACTION[fam.id])) if (f >= b.cue(cue) - 4) lit = action;
    return lit;
  };

  const shots: Shot[] = [
    { at: 0, x: STAGE_W / 2, y: 560, s: 0.3, rx: 10 },
    { at: beats.apps.from - 40, dur: 30, x: STAGE_W / 2, y: 540, s: 0.42, rx: 6 },
    ...families.map((fam, i) => ({ at: beats[fam.id].from - 14, dur: 28, x: colX(i) + COL_W / 2 + 437, y: 545, s: 1.12, rx: 0, ry: -3 })),
    { at: ninth - 10, dur: 30, x: (colX(7) + colX(8)) / 2 + COL_W / 2, y: 545, s: 1.05, ry: 0 },
    { at: to - 20, dur: 40, x: (colX(7) + colX(8)) / 2 + COL_W / 2, y: 545, s: 1.18 },
  ];

  const picker = useCapture('action-picker');
  const pickerIn = ease(f, catalog.from - 10, catalog.from + 6, OUT);
  const pickerOut = ease(f, beats.apps.from - 24, beats.apps.from - 6);
  return (
    <AbsoluteFill>
      <Camera shots={shots} width={STAGE_W}>
        {FAMILIES.map((fam, i) => {
          const isCurrent = i === current;
          const isNinth = i === 8 && f >= ninth - 4;
          const lit = litAction(i);
          const dimmed = current >= 0 && !isCurrent && !isNinth;
          return (
            <div key={fam.id} style={{ position: 'absolute', left: colX(i), top: TOP, width: COL_W, opacity: dimmed ? 0.35 : 1 }}>
              <div style={{
                display: 'flex', alignItems: 'center', gap: 14, padding: '14px 16px', marginBottom: 16, borderRadius: 16,
                background: isCurrent || isNinth ? 'rgba(242,193,74,0.14)' : 'rgba(255,255,255,0.05)', border: `1.5px solid ${isCurrent || isNinth ? 'rgba(242,193,74,0.6)' : C.line}`,
                opacity: ease(f, beats.apps.from - 30 + i * 2, beats.apps.from - 16 + i * 2),
              }}>
                <KdIcon icon={fam.icon} color={fam.color} size={44} />
                <span style={{ fontFamily: SANS, fontSize: 24, fontWeight: 800, color: C.text, whiteSpace: 'nowrap' }}>{fam.name}</span>
                <span style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 16, fontWeight: 700, color: C.accentText }}>{fam.actions.length}</span>
              </div>
              {fam.actions.map((a, j) => {
                const seed = i * 7 + j;
                const k = pop(f, beats.apps.from - 30 + seed * 0.5, { damping: 15, stiffness: 110 });
                const on = (isCurrent && j === lit) || isNinth;
                return (
                  <div key={a.name} style={{
                    display: 'flex', alignItems: 'center', gap: 14, padding: '14px 16px', marginBottom: 12, borderRadius: 14,
                    background: on ? 'rgba(242,193,74,0.16)' : 'rgba(30,30,36,0.94)', border: `1.5px solid ${on ? 'rgba(242,193,74,0.8)' : C.line2}`,
                    boxShadow: on ? '0 0 30px rgba(242,193,74,0.3)' : '0 10px 24px rgba(0,0,0,0.35)',
                    transform: `translate(${(1 - k) * (rand(seed) - 0.5) * 1600}px, ${(1 - k) * (rand(seed + 50) - 0.5) * 1200}px) rotate(${(1 - k) * (rand(seed + 9) - 0.5) * 60}deg) scale(${on ? 1.04 : 1})`,
                    opacity: Math.min(1, k * 2),
                  }}>
                    <KdIcon icon={a.icon} color={a.color} size={46} />
                    <div style={{ minWidth: 0 }}>
                      <div style={{ fontFamily: SANS, fontSize: 20, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{a.name}</div>
                      <div style={{ fontFamily: SANS, fontSize: 15, color: C.dim, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', width: 290 }}>{a.desc}</div>
                    </div>
                  </div>
                );
              })}
            </div>
          );
        })}
      </Camera>

      {/* The real «Add action» dialog, with its nine families */}
      <div style={{
        position: 'absolute', left: 960, top: 540, transform: `translate(-50%, -50%) scale(${1.3 * (0.9 + 0.1 * pickerIn) * (1 + 0.15 * pickerOut)})`,
        opacity: pickerIn * (1 - pickerOut), filter: pickerOut > 0.05 ? `blur(${pickerOut * 12}px)` : undefined,
      }}>
        <div style={{ position: 'relative', width: picker.width, height: picker.height, borderRadius: 12, overflow: 'hidden', boxShadow: '0 40px 100px rgba(0,0,0,0.7), 0 0 0 1px rgba(255,255,255,0.12)' }}>
          <Img src={staticFile(picker.file)} style={{ width: picker.width, height: picker.height }} />
          <FocusLayer width={picker.width} height={picker.height} focus={[{ from: catalog.cue('nine') - 8, to: beats.apps.from, rect: [18, 100, 200, 326], label: t('9 families'), at: 'right' }]} />
        </div>
      </div>

      {/* The showcase of the family being told */}
      {families.map(fam => {
        const b = beats[fam.id];
        const Demo = DEMOS[fam.id];
        const end = fam.id === 'kdesktop' ? ninth - 6 : b.to + 4;
        const show = inOut(f, b.from - 10, end, 12);
        if (show <= 0) return null;
        return (
          <div key={fam.id} style={{ position: 'absolute', left: 1010, top: 250, opacity: show, transform: `translateX(${(1 - show) * 60}px)` }}>
            <Demo b={b} />
          </div>
        );
      })}

      {families.map((_, i) => <Sfx key={i} at={beats[families[i].id].from - 14} name="whoosh-short" volume={0.22} />)}
      {[0, 10, 20].map(d => <Sfx key={d} at={catalog.from + 4 + d} name="pop" volume={0.15} />)}
      <Sfx at={ninth - 8} name="whoosh-short" volume={0.22} />
    </AbsoluteFill>
  );
};
