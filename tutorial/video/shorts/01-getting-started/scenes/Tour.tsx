import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera, Shot } from '../../../src/components/Camera';
import { FocusLayer, useCapture } from '../../../src/components/Capture';
import { Cursor } from '../../../src/components/Cursor';
import { Sfx } from '../../../src/components/Sfx';
import { Callout } from '../../../src/components/Ui';
import { ease, glide, inOut } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';

// The real pages (tutorial/captures, 1920 × 1080): they share the side menu, so going from one to the next looks like
// clicking through K-Desktop. `row` is the page's place in the menu (Commands at y 138, then every 44 px).
const PAGES = [
  { beat: 'core', cue: 'commands', capture: 'commands', row: 0, label: 'Actions you start with a hotkey' },
  { beat: 'core', cue: 'events', capture: 'events', row: 1, label: 'When something happens' },
  { beat: 'core', cue: 'quickmenu', capture: 'quickmenu-editor', row: 2, label: 'A menu under your mouse' },
  { beat: 'tools', cue: 'kboard', capture: 'kboard', row: 3, label: 'Remap keys and buttons' },
  { beat: 'tools', cue: 'macros', capture: 'macros', row: 4, label: 'Record and play back' },
  { beat: 'tools', cue: 'texts', capture: 'snippets', row: 5, label: 'Abbreviations into text' },
  { beat: 'tools', cue: 'layouts', capture: 'arrangements', row: 6, label: 'Where your windows go' },
  { beat: 'rest', cue: 'assistant', capture: 'assistant-panel', row: 7, label: 'Optional characters' },
  { beat: 'rest', cue: 'agenda', capture: 'agenda', row: 8, label: 'Your appointments' },
  { beat: 'rest', cue: 'history', capture: 'history', row: 9, label: 'Everything that ran' },
  { beat: 'rest', cue: 'settings', capture: 'settings-general', row: 10, label: 'Grouped by topic' },
  { beat: 'rest', cue: 'achievements', capture: 'achievements', row: 11, label: 'Unlocked as you go' },
];
const navY = (row: number) => 138 + 44 * row;

const Layer: React.FC<{ name: string; k: number }> = ({ name, k }) => {
  const cap = useCapture(name);
  if (k <= 0) return null;
  return <Img src={staticFile(cap.file)} style={{ position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, opacity: k }} />;
};

/** Chapter «A quick tour»: every page of the side menu, clicked as the narrator names it. */
export const Tour: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const rest = B('rest');
  const more = B('more');
  // A page arrives a few frames before its name is said (the click), and stays until the next one.
  const at = PAGES.map(p => B(p.beat).cue(p.cue) - 4);
  const shown = PAGES.map((_, i) => {
    const next = at[i + 1] ?? durationInFrames + 10;
    return i === 0 ? 1 - ease(f, next, next + 5) : Math.min(ease(f, at[i], at[i] + 5), 1 - ease(f, next, next + 5));
  });
  // The camera follows the menu down, slowly, and pulls back for the last words.
  const shots: Shot[] = [
    { at: 0, x: 960, y: 540, s: 0.92, rx: 8, ry: -8 },
    { at: 4, dur: 50, x: 860, y: 520, s: 1.05, rx: 0, ry: 0 },
    { at: at[3] - 10, dur: 120, x: 900, y: 560, s: 1.08 },
    { at: at[8] - 10, dur: 120, x: 880, y: 580, s: 1.08 },
    { at: rest.cue('search') - 20, dur: 24, x: 620, y: 300, s: 1.5 },
    { at: at[11] - 16, dur: 26, x: 960, y: 540, s: 1.04 },
    { at: more.from, dur: 60, x: 960, y: 540, s: 0.96 },
  ];
  const cursor = [
    { at: 0, x: 700, y: 700 },
    ...PAGES.map((p, i) => ({ at: at[i] - 12, x: 110, y: navY(p.row), dur: 10 })),
    { at: more.from, x: 900, y: 760, dur: 30 },
  ];
  const search = rest.cue('search');
  return (
    <AbsoluteFill>
      <Camera shots={shots} width={1920} height={1080} float={0.5} clamp>
        <div style={{ position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, overflow: 'hidden', borderRadius: 14, boxShadow: '0 40px 100px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.1)', opacity: glide(f, 0, 20) }}>
          {PAGES.map((p, i) => <Layer key={p.capture} name={p.capture} k={shown[i]} />)}
          <FocusLayer width={1920} height={1080} focus={[
            { from: search - 8, to: search + 44, rect: [260, 124, 282, 36], label: 'Ctrl + F', at: 'right' },
            // The phone in the status bar at the bottom of the menu: the web app and the Android app.
            { from: more.cue('phone') - 6, to: more.to + 10, rect: [104, 1024, 32, 32], label: t('Your phone as a remote'), at: 'right' },
          ]} />
        </div>
        <Cursor path={cursor} clicks={at.map(a => ({ at: a - 1 }))} />
      </Camera>
      {/* What each page is for, beside the menu */}
      {PAGES.map((p, i) => {
        const next = at[i + 1] ?? more.from;
        return (
          <Callout key={p.capture} show={inOut(f, at[i] + 2, next + 2, 6)} x={1880} y={110} align="right" size={30} gold>
            {t(p.label)}
          </Callout>
        );
      })}
      {at.map(a => <Sfx key={a} at={a - 1} name="click" volume={0.22} />)}
      <Sfx at={search - 8} name="blip" volume={0.2} />
    </AbsoluteFill>
  );
};
