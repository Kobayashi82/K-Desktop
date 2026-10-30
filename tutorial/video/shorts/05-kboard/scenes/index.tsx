import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../../src/components/Capture';
import { CommandCard } from '../../../src/components/CommandCard';
import { Keycap } from '../../../src/components/Keys';
import { MouseShape } from '../../../src/components/Props';
import { ShortAgenda, ShortEnd, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { Toggle } from '../../../src/components/Ui';
import { ease, IN, inOut, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { KBoard } from '../../../src/scenes/KBoard';
import { ComboKeys } from '../../../src/scenes/commands/Build';
import type { Short } from '../../../src/shorts';
import { C, MOODS, SANS } from '../../../src/theme';

/** A Caps Lock in the way and idle side buttons; the title and what the video covers. */
const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const caps = pop(f, idea.from + 2, { damping: 14 });
  const mouse = pop(f, idea.cue('side') - 8, { damping: 14 });
  const away = ease(f, idea.to - 8, idea.to + 12, IN);
  const nag = Math.sin(f / 3) * 4 * inOut(f, idea.cue('caps') - 4, idea.cue('caps') + 30, 4);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 620, top: 470, transform: `translate(-50%, -50%) translateX(${nag}px) scale(${caps * (1 - 0.3 * away)})`, opacity: 1 - away }}>
        <Keycap label={t('Caps Lock')} height={150} width={300} size={44} lit={inOut(f, idea.cue('caps') - 4, idea.cue('caps') + 30, 6)} />
      </div>
      <div style={{ position: 'absolute', left: 1300, top: 470, transform: `translate(-50%, -50%) scale(${mouse * (1 - 0.3 * away)})`, opacity: 1 - away }}>
        <MouseShape scale={1.1} glow={inOut(f, idea.cue('side'), idea.cue('side') + 40, 6)} />
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('How it works'), icon: 'keyboard_layout_float_24_filled', color: 'purple', at: plan.cue('how') },
          { label: t('A real profile'), icon: 'xbox_controller_24_filled', color: 'indigo', at: plan.cue('real') },
          { label: t('One key to switch it'), icon: 'flash_24_filled', color: 'gold', at: plan.cue('tip') },
        ]} />
      )}
      <Sfx at={idea.from + 2} name="pop" volume={0.22} />
      <Sfx at={idea.cue('side') - 8} name="pop" volume={0.22} />
    </AbsoluteFill>
  );
};

// The real «Gaming» profile (tutorial/captures/en/kboard.png): its remaps, one row each.
const REMAP = (i: number): [number, number, number, number] => [588, 721 + 46 * i - 21, 1294, 42];

/** Chapter «A real profile»: the remaps of the real page, as the narrator names them. */
const Real: React.FC = () => {
  const B = useBeats();
  const p = B('profile');
  const press = B('press');
  return (
    <CaptureShot name="kboard"
      shots={[
        { at: 0, x: 960, y: 540, s: 0.96, rx: 6 },
        { at: 6, dur: 40, x: 1100, y: 300, s: 1.35, rx: 0 },
        { at: p.cue('caps') - 20, dur: 30, x: 1200, y: 800, s: 1.35 },
        { at: press.from - 10, dur: 30, x: 1300, y: 300, s: 1.4 },
      ]}
      focus={[
        { from: p.cue('gaming') - 4, to: p.cue('caps') - 6, rect: [570, 86, 1310, 76], label: t('Only in eldenring.exe'), at: 'bottom' },
        { from: p.cue('caps') - 6, to: p.cue('win') - 4, rect: REMAP(0) },
        { from: p.cue('win') - 4, to: p.cue('f1') - 4, rect: REMAP(1) },
        { from: p.cue('f1') - 4, to: p.cue('side') - 4, rect: REMAP(2) },
        { from: p.cue('side') - 4, to: p.cue('f12') - 4, rect: [588, 838, 1294, 88] },
        { from: p.cue('f12') - 4, to: press.from, rect: REMAP(5) },
        { from: press.cue('press') - 6, to: press.to + 30, rect: [1754, 206, 104, 32], label: t('Press key'), at: 'left' },
      ]} />
  );
};

/** Chapter «One more thing»: a command with the K-Board action switches the profile, from one hotkey. */
const Tip: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const tip = B('tip');
  const card = pop(f, tip.cue('command') - 8, { damping: 14 });
  const at = [tip.cue('one') - 6, tip.cue('one'), tip.cue('one') + 6];
  const on = ease(f, at[2] + 4, at[2] + 12);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 400, transform: `translate(-50%, -50%) scale(${0.8 + 0.2 * card})`, opacity: Math.min(1, card * 1.4) }}>
        <CommandCard name={t('Gaming setup')} hotkey={f > tip.cue('hotkey') - 4 ? t('Ctrl + Alt + G') : undefined} width={720}
          rows={[{ icon: 'keyboard_layout_float_24_filled', color: 'purple', name: t('K-Board'), detail: t('Gaming · on / off') }]}
          shown={[ease(f, tip.cue('toggle') - 8, tip.cue('toggle') + 6)]} glow={inOut(f, at[2], at[2] + 40, 6)} />
      </div>
      <div style={{
        position: 'absolute', left: 960, top: 640, transform: 'translateX(-50%)', display: 'flex', alignItems: 'center', gap: 20,
        opacity: ease(f, tip.cue('toggle') - 4, tip.cue('toggle') + 8), fontFamily: SANS, fontSize: 34, fontWeight: 700, color: C.text,
      }}>
        <span style={{ color: C.dim }}>{t('Gaming')}</span>
        <Toggle on={on} scale={2} />
        <span style={{ color: on > 0.5 ? C.accentText : C.faint }}>{on > 0.5 ? t('On') : t('Off')}</span>
      </div>
      <ComboKeys keys={['Ctrl', 'Alt', 'G']} at={at} release={at[2] + 14} show={inOut(f, at[0] - 10, tip.to + 30, 8)} />
      <Sfx at={tip.cue('command') - 8} name="pop" volume={0.22} />
      {at.map(a => <Sfx key={a} at={a - 3} name="key" volume={0.45} />)}
      <Sfx at={at[2] + 6} name="chime" volume={0.22} />
    </AbsoluteFill>
  );
};

/** Tutorial 05, «K-Board»: the long video's chapter, a real profile, and switching it with one key. */
export const KBOARD: Short = {
  scenes: { hook: Hook, kboard: KBoard, real: Real, tip: Tip, end: ShortEnd },
  moods: { hook: MOODS.intro, kboard: MOODS.kboard, real: MOODS.settings, tip: MOODS.kboard, end: MOODS.outro },
};
