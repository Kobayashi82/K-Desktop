import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Keycap } from '../../../src/components/Keys';
import { Phone } from '../../../src/components/Phone';
import { MAIN_MENU, QuickMenuPanel } from '../../../src/components/QuickMenu';
import { Callout } from '../../../src/components/Ui';
import { ShortAgenda, ShortEnd, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, IN, inOut, keys, pop, rand } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { Launcher } from '../../../src/scenes/Launcher';
import { QuickMenuScene } from '../../../src/scenes/QuickMenu';
import type { Short } from '../../../src/shorts';
import { C, MOODS, SANS } from '../../../src/theme';

const COMBOS = ['Ctrl + Alt + B', 'Ctrl + Shift + S', 'Win + Alt + C', 'Ctrl + Alt + M', 'Alt + F7', 'Ctrl + Win + 3', 'Shift + F11', 'Ctrl + Alt + J'];

/** Hotkeys piling up until nobody remembers them; then the title and what the video covers. */
const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const forget = idea.cue('remember');
  const away = ease(f, idea.to - 8, idea.to + 12, IN);
  const q = pop(f, forget - 4, { damping: 10, stiffness: 150 });
  return (
    <AbsoluteFill>
      {COMBOS.map((c, i) => {
        const k = pop(f, idea.from + 10 + i * 9, { damping: 14 });
        const x = 260 + (i % 4) * 460 + (rand(i) - 0.5) * 80;
        const y = 300 + Math.floor(i / 4) * 300 + (rand(i + 9) - 0.5) * 60;
        const fade = ease(f, forget - 6, forget + 14);
        return (
          <div key={c} style={{
            position: 'absolute', left: x, top: y, transform: `translate(-50%, -50%) scale(${(0.6 + 0.4 * k) * (1 - 0.3 * away)}) rotate(${(rand(i + 3) - 0.5) * 14}deg)`,
            opacity: Math.min(1, k * 1.4) * (1 - 0.65 * fade) * (1 - away), filter: fade > 0.05 ? `blur(${fade * 5}px)` : undefined,
          }}>
            <Keycap label={c} height={86} size={28} />
          </div>
        );
      })}
      <div style={{
        position: 'absolute', left: 960, top: 450, transform: `translate(-50%, -50%) scale(${q * (1 - 0.4 * away)})`, opacity: 1 - away,
        fontFamily: SANS, fontSize: 300, fontWeight: 800, color: C.accentText, textShadow: '0 0 60px rgba(242,193,74,0.5)',
      }}>?</div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Open a QuickMenu'), icon: 'text_bullet_list_tree_24_filled', color: 'indigo', at: plan.cue('qm') },
          { label: t('Build your own'), icon: 'wrench_24_filled', color: 'gold', at: plan.cue('build') },
          { label: t('The launcher'), icon: 'search_24_filled', color: 'yellow', at: plan.cue('search') },
        ]} />
      )}
      {COMBOS.map((c, i) => <Sfx key={c} at={idea.from + 10 + i * 9} name="pop-high" volume={0.1} />)}
      <Sfx at={forget - 4} name="impact" volume={0.25} />
    </AbsoluteFill>
  );
};

// The real Settings › Launcher page: its rows (tutorial/captures/en/settings-launcher.png).
const ROW = (y: number): [number, number, number, number] => [572, y - 22, 960, 44];

/** Files and folders, the clipboard history, and the switches of each (the real Settings › Launcher). */
const Search: React.FC = () => {
  const B = useBeats();
  const files = B('files');
  const clip = B('clip');
  return (
    <CaptureShot name="settings-launcher"
      shots={[{ at: 0, x: 960, y: 540, s: 0.96, rx: 6 }, { at: 4, dur: 40, x: 1050, y: 420, s: 1.4, rx: 0 }, { at: clip.cue('switch') - 16, dur: 26, x: 1200, y: 420, s: 1.3 }]}
      focus={[
        { from: files.cue('files') - 4, to: files.cue('question') - 4, rect: ROW(516) },
        { from: files.cue('question') - 4, to: files.cue('ctrl') - 4, rect: ROW(516), label: t('«?» first: only files'), at: 'top' },
        { from: files.cue('ctrl') - 4, to: clip.from, rect: ROW(516), label: t('Ctrl + Enter: its folder'), at: 'top' },
        { from: clip.cue('clipboard') - 4, to: clip.cue('switch') - 6, rect: ROW(464), label: t('Enter pastes it'), at: 'top' },
        { from: clip.cue('switch') - 6, to: clip.to + 30, rect: [1470, 232, 64, 356], label: t('On or off'), at: 'left' },
      ]} />
  );
};

/** The QuickMenus on the phone: a drawn phone with the menu, ready to tap. */
const OnPhone: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const ph = B('phone');
  const k = pop(f, ph.cue('phone') - 10, { damping: 15 });
  const tap = ph.cue('phone') + 30;
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 540, transform: `translate(-50%, -50%) scale(${0.8 + 0.2 * k}) rotate(${(1 - k) * -6}deg)`, opacity: Math.min(1, k * 1.4) }}>
        <Phone width={430}>
          <div style={{ position: 'absolute', inset: 0, background: 'linear-gradient(180deg, #1a1a20, #101014)', display: 'flex', justifyContent: 'center', paddingTop: 70 }}>
            <QuickMenuPanel items={MAIN_MENU} scale={1.55} width={250} hot={f > tap ? 1 : -1} flash={inOut(f, tap, tap + 14, 4)} />
          </div>
        </Phone>
      </div>
      <Callout show={inOut(f, ph.cue('menus') - 4, ph.to + 40, 8)} x={1420} y={300} size={30}>{t('Your QuickMenus')}</Callout>
      <Callout show={inOut(f, ph.cue('phone'), ph.to + 40, 8)} x={1420} y={380} size={26} gold={false}>{t('Web app · Android app')}</Callout>
      <Sfx at={ph.cue('phone') - 10} name="whoosh-short" volume={0.22} />
      <Sfx at={tap} name="click" volume={0.25} />
    </AbsoluteFill>
  );
};

/** Chapter «More than programs»: files, the clipboard history, and the QuickMenus on your phone. */
const More: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const ph = B('phone');
  return (
    <AbsoluteFill>
      <Part from={0} to={ph.from - 4} first><Search /></Part>
      <Part from={ph.from - 4} to={durationInFrames} last><OnPhone /></Part>
    </AbsoluteFill>
  );
};

/** Chapter «One more thing»: commands without a hotkey, found by name in the real launcher; hidden ones too. */
const Tip: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const tip = B('tip');
  const hidden = tip.cue('hidden') - 10;
  const row = (y: number): [number, number, number, number] => [648, y - 24, 612, 48];
  return (
    <AbsoluteFill>
      <Part from={0} to={hidden} first>
        <CaptureShot name="launcher"
          shots={[{ at: 0, x: 960, y: 540, s: 1.0 }, { at: tip.cue('nokey') - 10, dur: 40, x: 960, y: 450, s: 1.5 }]}
          focus={[
            { from: tip.cue('launcher') - 6, to: tip.cue('steam') - 6, rect: [640, 190, 640, 518] },
            { from: tip.cue('steam') - 6, to: tip.cue('headphones') - 2, rect: row(274), label: t('Command · no hotkey'), at: 'top' },
            { from: tip.cue('headphones') - 2, to: tip.cue('lights') - 4, rect: row(524) },
            { from: tip.cue('lights') - 4, to: hidden + 10, rect: row(574) },
          ]} />
      </Part>
      <Part from={hidden} to={durationInFrames} last>
        <CaptureShot name="settings-launcher"
          shots={[{ at: hidden, x: 1050, y: 300, s: 1.3 }, { at: hidden + 4, dur: 40, x: 1050, y: 300, s: 1.5 }]}
          focus={[{ from: hidden + 8, to: durationInFrames, rect: ROW(308) }]} />
      </Part>
    </AbsoluteFill>
  );
};

/** Tutorial 03, «QuickMenu and the launcher»: the long video's two chapters, plus files, the clipboard and a tip. */
export const QUICKMENU_LAUNCHER: Short = {
  scenes: { hook: Hook, quickmenu: QuickMenuScene, launcher: Launcher, more: More, tip: Tip, end: ShortEnd },
  moods: { hook: MOODS.intro, quickmenu: MOODS.quickmenu, launcher: MOODS.launcher, more: MOODS.settings, tip: MOODS.launcher, end: MOODS.outro },
};
